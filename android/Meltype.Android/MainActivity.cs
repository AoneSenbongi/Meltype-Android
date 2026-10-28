// SPDX-License-Identifier: GPL-3.0-or-later
using Android.App;
using Android.Content;
using Android.Graphics;
using Path = System.IO.Path;
using Resource = Meltype.Android.Resource;
using Android.OS;
using Android.Provider;
using Settings = Android.Provider.Settings;
using Android.Text;
using Android.Views;
using Android.Views.InputMethods;
using Android.Widget;
using Meltype.AndroidCore;
using Meltype.Config;
using System.Text;

namespace Meltype.Mobile;

[Activity(Name = "jp.aonesenbongi.meltype.MainActivity", Label = "Meltype", MainLauncher = true, Exported = true)]
public sealed class MainActivity : Activity
{
    private TextView? _setupStatus;
    private Action? _refreshPreferences;
    private TextView? _dictionaryStatus;
    private ISharedPreferences? _layoutPreferences;
    private Action? _refreshLayout;
    private ISharedPreferencesOnSharedPreferenceChangeListener? _layoutListener;
    private const int QwertyLayout = 0, GodanLayout = 1;
    private string DictionaryPath => Path.Combine(FilesDir!.AbsolutePath, "user-dictionary.json");
    private int Dp(float value) => MobileStyle.Dp(this, value);
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        MobileStyle.Apply(this);
        Window!.SetSoftInputMode(SoftInput.AdjustResize);
        if (OperatingSystem.IsAndroidVersionAtLeast(30) && !OperatingSystem.IsAndroidVersionAtLeast(35)) Window.SetDecorFitsSystemWindows(false);
        var layout = new LinearLayout(this) { Orientation = Orientation.Vertical };
        layout.SetPadding(Dp(20), Dp(16), Dp(20), Dp(20));
        layout.SetBackgroundColor(MobileStyle.PageBackground);
        var header = new LinearLayout(this); header.SetGravity(GravityFlags.CenterVertical);
        var mark = new ImageView(this); mark.SetImageResource(Resource.Drawable.ic_meltype_mark);
        mark.ContentDescription = "Meltypeのアイコン";
        header.AddView(mark, new LinearLayout.LayoutParams(Dp(28), Dp(28)) { RightMargin = Dp(8) });
        header.AddView(Label("Meltype · Android", 22, MobileStyle.Ink)); layout.AddView(header);
        _setupStatus = Label("設定を確認しています…", 13, MobileStyle.Muted);
        _setupStatus.SetPadding(0, Dp(12), 0, Dp(12)); layout.AddView(_setupStatus);
        var basicHeading = Label("基本設定", 18, MobileStyle.Ink);
        basicHeading.SetPadding(0, Dp(8), 0, Dp(4)); layout.AddView(basicHeading);
        Button Add(string text, Action action, LinearLayout? parent = null)
        {
            var button = new Button(this) { Text = text, TextSize = 14 };
            MobileStyle.Button(button);
            button.Click += (_, _) => action();
            (parent ?? layout).AddView(button, parent == null
                ? new LinearLayout.LayoutParams(-1, Dp(48)) { TopMargin = Dp(8) }
                : new LinearLayout.LayoutParams(0, Dp(48), 1) { LeftMargin = Dp(2), RightMargin = Dp(2) });
            return button;
        }
        var setup = new LinearLayout(this) { BaselineAligned = false }; layout.AddView(setup);
        Add("有効にする", () => StartActivity(new Intent(Settings.ActionInputMethodSettings)), setup);
        Add("キーボードを選ぶ", () => ((InputMethodManager)GetSystemService(InputMethodService)!).ShowInputMethodPicker(), setup);
        Add("アプリを更新", ShowUpdate);
        Add("単語登録・辞書の取り込み", ShowDictionary);
        _dictionaryStatus = Label("", 12, MobileStyle.Muted);
        _dictionaryStatus.SetPadding(0, Dp(4), 0, Dp(12)); layout.AddView(_dictionaryStatus); RefreshDictionary();

        var preferences = new InputPreferenceStore(Path.Combine(FilesDir!.AbsolutePath, "input-preferences.json"));
        var punctuation = new LinearLayout(this) { BaselineAligned = false };
        layout.AddView(punctuation);
        Spinner Choice(string title, string[] values)
        {
            var field = new LinearLayout(this) { Orientation = Orientation.Vertical };
            field.AddView(Label(title, 13, MobileStyle.Ink));
            var spinner = new Spinner(this);
            var adapter = new ArrayAdapter<string>(this, global::Android.Resource.Layout.SimpleSpinnerItem, values);
            adapter.SetDropDownViewResource(global::Android.Resource.Layout.SimpleSpinnerDropDownItem);
            spinner.Adapter = adapter;
            field.AddView(spinner, new LinearLayout.LayoutParams(-1, Dp(48)));
            punctuation.AddView(field, new LinearLayout.LayoutParams(0, -2, 1)); return spinner;
        }
        var commas = new[] { "、", "，", "," }; var periods = new[] { "。", "．", "." };
        var comma = Choice("読点", ["、（読点）", "，（全角）", ",（半角）"]);
        var period = Choice("句点", ["。（句点）", "．（全角）", ".（半角）"]);
        Add("句読点を保存", () =>
        {
            try
            {
                preferences.Save(preferences.Current with { Comma = commas[comma.SelectedItemPosition], Period = periods[period.SelectedItemPosition] });
                Toast.MakeText(this, "句読点を保存しました。次の入力から反映します。", ToastLength.Short)!.Show();
            }
            catch (IOException) { SaveError(); }
        });
        void SaveError() => Toast.MakeText(this, "設定を保存できません。空き容量を確認してください。", ToastLength.Long)!.Show();
        var learning = new Switch(this) { Text = "変換の学習", TextSize = 14 };
        learning.SetTextColor(MobileStyle.Ink);
        layout.AddView(learning, new LinearLayout.LayoutParams(-1, Dp(56)) { TopMargin = Dp(8) });
        var learningStatus = Label("既存の辞書・履歴は保持します。", 12, MobileStyle.Muted); layout.AddView(learningStatus);
        var featureSwitches = new List<(Switch Control, InputFeatureSetting Feature)>();
        var refreshing = false;
        void RefreshPreferences()
        {
            refreshing = true;
            try
            {
                var settings = preferences.Current;
                comma.SetSelection(Array.IndexOf(commas, settings.Comma));
                period.SetSelection(Array.IndexOf(periods, settings.Period));
                learning.Checked = settings.LearningEnabled;
                foreach (var (control, feature) in featureSwitches) control.Checked = feature.Read(settings);
            }
            finally { refreshing = false; }
        }
        learning.CheckedChange += (_, e) =>
        {
            if (refreshing) return;
            try
            {
                preferences.Save(preferences.Current with { LearningEnabled = e.IsChecked });
            }
            catch (IOException) { SaveError(); }
            RefreshPreferences();
        };
        _refreshPreferences = RefreshPreferences;
        layout.AddView(Label("入力設定", 18, MobileStyle.Ink));
        layout.AddView(Label("変更後はキーボードを開き直してください。自動・カナキーの上フリックで文字種、123キーの長押しで追加記号を選べます。予測候補は長押しで学習から削除します。", 12, MobileStyle.Muted));
        string? section = null;
        foreach (var feature in InputFeatureSettings.All)
        {
            if (section != feature.Section)
            {
                section = feature.Section;
                var heading = Label(section, 15, MobileStyle.Ink); heading.SetPadding(0, Dp(16), 0, Dp(4)); layout.AddView(heading);
            }
            var control = new Switch(this) { Text = feature.Title, TextSize = 14, ContentDescription = "設定 " + feature.Key };
            control.SetTextColor(MobileStyle.Ink);
            featureSwitches.Add((control, feature));
            layout.AddView(control, new LinearLayout.LayoutParams(-1, -2) { TopMargin = Dp(6), BottomMargin = Dp(6) });
            control.CheckedChange += (_,e) =>
            {
                if (refreshing) return;
                try { preferences.Save(feature.Write(preferences.Current, e.IsChecked)); }
                catch (IOException) { SaveError(); }
            };
        }
        var keyboardHeading = Label("キーボード", 18, MobileStyle.Ink);
        keyboardHeading.SetPadding(0, Dp(24), 0, Dp(4)); layout.AddView(keyboardHeading);
        // キーボード配列 (QWERTY / GODAN)。キーボード左下の QWERTY / 五段 と同じ設定を保存する。
        _layoutPreferences = GetSharedPreferences("keyboard-layout", FileCreationMode.Private);
        var layoutHeading = Label("キーボード配列", 15, MobileStyle.Ink);
        layoutHeading.SetPadding(0, Dp(8), 0, Dp(4)); layout.AddView(layoutHeading);
        var layoutSpinner = new Spinner(this);
        var layoutAdapter = new ArrayAdapter<string>(this, global::Android.Resource.Layout.SimpleSpinnerItem,
            new[] { "QWERTY", "GODAN" });
        layoutAdapter.SetDropDownViewResource(global::Android.Resource.Layout.SimpleSpinnerDropDownItem);
        layoutSpinner.Adapter = layoutAdapter;
        layout.AddView(layoutSpinner, new LinearLayout.LayoutParams(-1, Dp(48)));
        var refreshingLayout = false;
        void RefreshLayout()
        {
            refreshingLayout = true;
            try { layoutSpinner.SetSelection(_layoutPreferences!.GetBoolean("godan-enabled", false) ? 1 : 0); }
            finally { refreshingLayout = false; }
        }
        layoutSpinner.ItemSelected += (_, _) =>
        {
            if (refreshingLayout) return;
            var godan = layoutSpinner.SelectedItemPosition == 1;
            // Spinner は起動直後の初期選択を、レイアウトのあとで ItemSelected として知らせる。
            // 保存した選択と同じなら利用者の操作ではないので、今の配列 (layout-mode) を書き換えず通知もしない。
            if (_layoutPreferences!.GetBoolean("godan-enabled", false) == godan) return;
            var editor = _layoutPreferences.Edit()!;
            editor.PutBoolean("godan-enabled", godan);
            editor.PutInt("layout-mode", godan ? GodanLayout : QwertyLayout);
            editor.Apply();
            Toast.MakeText(this, godan ? "GODANとQWERTYに切り替えました。" : "QWERTYのみに切り替えました。", ToastLength.Short)!.Show();
        };
        RefreshLayout();
        _refreshLayout = RefreshLayout;
        _layoutListener = new LayoutSettingListener(() => RunOnUiThread(() => _refreshLayout?.Invoke()));
        _layoutPreferences!.RegisterOnSharedPreferenceChangeListener(_layoutListener);

        RefreshPreferences();
        var trial = Label("試し書き", 16, MobileStyle.Ink); trial.SetPadding(0, Dp(24), 0, Dp(8)); layout.AddView(trial);
        var editor = new EditText(this) { Id = Resource.Id.test_editor, Hint = "kyouhagoogledekensaku", TextSize = 17, InputType = InputTypes.ClassText | InputTypes.TextFlagMultiLine, Gravity = GravityFlags.Top };
        editor.SetTextColor(MobileStyle.Ink); editor.SetHintTextColor(MobileStyle.Muted);
        editor.Background = MobileStyle.Outline(this);
        editor.SetPadding(Dp(16), Dp(16), Dp(16), Dp(16));
        layout.AddView(editor, new LinearLayout.LayoutParams(-1, Dp(120)));
        var tip = Label("空白キーで変換 · 確定キーで確定", 12, MobileStyle.Muted);
        tip.SetPadding(0, Dp(10), 0, Dp(6)); layout.AddView(tip);
        Add("ライセンス", () =>
        {
            var text = ReadAsset("LICENSE.txt") + "\n\n" + ReadAsset("THIRD_PARTY_NOTICES.txt");
            var scroll = new ScrollView(this); scroll.AddView(new TextView(this) { Text = text, TextSize = 12 });
            using var dialog = new AlertDialog.Builder(this);
            dialog.SetTitle("Meltype / Mozc / 同梱ライブラリ");
            dialog.SetView(scroll);
            dialog.SetPositiveButton("閉じる", (_, _) => { });
            dialog.Show();
        });
        var version = PackageManager?.GetPackageInfo(PackageName!, (global::Android.Content.PM.PackageInfoFlags)0)?.VersionName;
        var about = Label($"Android {version}\n雪代／Yukishiro氏のMeltypeを基にした非公式Forkです。変換にはOSS版Mozcを使用し、入力は端末内で処理します。", 12, MobileStyle.Muted);
        about.SetPadding(0, Dp(18), 0, 0); layout.AddView(about);
        var page = new ScrollView(this) { FillViewport = true }; page.AddView(layout);
        var frame = new FrameLayout(this); frame.AddView(page, new FrameLayout.LayoutParams(-1, -1));
        void ScrollEditor() { if (editor.HasFocus) page.Post(() => page.SmoothScrollTo(0, Math.Max(0, editor.Top - Dp(16)))); }
        editor.FocusChange += (_, e) => { if (e.HasFocus) page.PostDelayed(ScrollEditor, 200); };
        frame.SetOnApplyWindowInsetsListener(new SetupInsets(Dp(12), ScrollEditor));
        SetContentView(frame); frame.RequestApplyInsets();
        if (Intent?.GetBooleanExtra("zenzselftest", false) == true) ZenzDeviceSelfTest.Start(this);
        if (Intent?.GetBooleanExtra("zenzfastselftest", false) == true) FastZenzSelfTest.Start(this);
        else if (Intent?.GetBooleanExtra("selftest", false) == true) RunSelfTest(editor);
    }
    private void ShowUpdate() => StartActivity(new Intent(this, typeof(UpdateActivity)));
    internal sealed class SetupInsets(int gap, Action scroll) : Java.Lang.Object, View.IOnApplyWindowInsetsListener
    {
        public WindowInsets OnApplyWindowInsets(View? view, WindowInsets? insets)
        {
            if (view == null || insets == null) throw new ArgumentNullException();
            if (OperatingSystem.IsAndroidVersionAtLeast(30))
            {
                var bars = insets.GetInsets(WindowInsets.Type.SystemBars() | WindowInsets.Type.DisplayCutout())!;
                var keyboard = insets.GetInsets(WindowInsets.Type.Ime())!;
                view.SetPadding(bars.Left, bars.Top + gap, bars.Right, Math.Max(bars.Bottom, keyboard.Bottom) + gap);
            }
            else view.SetPadding(insets.SystemWindowInsetLeft, insets.SystemWindowInsetTop + gap, insets.SystemWindowInsetRight, insets.SystemWindowInsetBottom + gap);
            view.Post(scroll); return insets;
        }
    }
    private void RefreshDictionary()
    {
        try { if (_dictionaryStatus != null) _dictionaryStatus.Text = $"ユーザー辞書 · {new AndroidUserDictionary(DictionaryPath).Count}件"; }
        catch (Exception ex) when (ex is IOException or DecoderFallbackException) { if (_dictionaryStatus != null) _dictionaryStatus.Text = "辞書を読めません。取り込み元を確認してください。"; }
    }
    private void ShowDictionary()
    {
        var fields = new LinearLayout(this) { Orientation = Orientation.Vertical }; fields.SetPadding(Dp(20), Dp(12), Dp(20), Dp(12));
        var reading = new EditText(this) { Hint = "読み（かな・英数字）", InputType = InputTypes.ClassText, ImeOptions = (ImeAction)0x1000000 };
        var word = new EditText(this) { Hint = "登録する単語・定型文", InputType = InputTypes.ClassText | InputTypes.TextFlagMultiLine, ImeOptions = (ImeAction)0x1000000 };
        reading.SetSingleLine(true); fields.AddView(reading); fields.AddView(word, new LinearLayout.LayoutParams(-1, Dp(96)));
        fields.AddView(Label("1文字の読みは候補のみ。既存の単語は保持します。", 12, MobileStyle.Muted));
        var register = new Button(this) { Text = "単語を登録" }; MobileStyle.Button(register); fields.AddView(register);
        register.Click += (_, _) =>
        {
            try
            {
                var added = new AndroidUserDictionary(DictionaryPath).Add(reading.Text ?? "", word.Text ?? "");
                RefreshDictionary(); Toast.MakeText(this, added == 0 ? "同じ単語は登録済みです。" : "登録しました。次の入力から反映します。", ToastLength.Short)!.Show();
                reading.Text = ""; word.Text = "";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Toast.MakeText(this, ex is InvalidDataException ? ex.Message : "辞書を保存できません。空き容量を確認してください。", ToastLength.Long)!.Show(); }
        };
        using var dialog = new AlertDialog.Builder(this);
        dialog.SetTitle("ユーザー辞書"); dialog.SetView(fields);
        dialog.SetNeutralButton("ファイルを取り込む", (_, _) =>
        {
            var intent = new Intent(Intent.ActionOpenDocument); intent.SetType("*/*"); intent.AddCategory(Intent.CategoryOpenable);
            StartActivityForResult(intent, 501);
        });
        dialog.SetPositiveButton("閉じる", (_, _) => { }); dialog.Show();
    }
    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (requestCode != 501 || resultCode != Result.Ok || data?.Data == null) return;
        try
        {
            using var input = ContentResolver!.OpenInputStream(data.Data)!;
            using var buffer = new MemoryStream(); var chunk = new byte[8192]; int count;
            while ((count = input.Read(chunk, 0, chunk.Length)) > 0) { buffer.Write(chunk, 0, count); if (buffer.Length > 1_000_000) throw new InvalidDataException("辞書ファイルは1MBまでです。"); }
            var result = new AndroidUserDictionary(DictionaryPath).Import(buffer.ToArray()); RefreshDictionary();
            Toast.MakeText(this, $"{result.Added}件登録しました。取り込めない項目は{result.Skipped}件です。", ToastLength.Long)!.Show();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DecoderFallbackException)
        { Toast.MakeText(this, ex is InvalidDataException ? ex.Message : "辞書を読めません。UTF-8／UTF-16の辞書を選んでください。", ToastLength.Long)!.Show(); }
    }
    private TextView Label(string text, int size, Color color)
    {
        var label = new TextView(this) { Text = text, TextSize = size }; label.SetTextColor(color); return label;
    }
    protected override void OnDestroy()
    {
        if (_layoutListener != null) _layoutPreferences?.UnregisterOnSharedPreferenceChangeListener(_layoutListener);
        _layoutListener = null;
        base.OnDestroy();
    }

    private sealed class LayoutSettingListener : Java.Lang.Object, ISharedPreferencesOnSharedPreferenceChangeListener
    {
        private readonly Action _refresh;
        public LayoutSettingListener(Action refresh) => _refresh = refresh;
        public void OnSharedPreferenceChanged(ISharedPreferences? sharedPreferences, string? key)
        {
            if (key is "layout-mode" or "godan-enabled") _refresh();
        }
    }

    protected override void OnResume()
    {
        base.OnResume();
        _refreshPreferences?.Invoke();
        _refreshLayout?.Invoke();
        var manager = (InputMethodManager)GetSystemService(InputMethodService)!;
        var enabled = manager.EnabledInputMethodList?.Any(method => method.PackageName == PackageName) == true;
        var selected = Settings.Secure.GetString(ContentResolver, Settings.Secure.DefaultInputMethod)?.StartsWith(PackageName + "/", StringComparison.Ordinal) == true;
        if (_setupStatus != null) _setupStatus.Text = selected ? "有効 · Meltypeを選択中" : enabled ? "有効 · キーボード未選択" : "キーボード未設定";
    }
    private string ReadAsset(string name)
    {
        using var reader = new StreamReader(Assets!.Open(name)); return reader.ReadToEnd();
    }
    private void RunSelfTest(EditText editor)
    {
        var path = Path.Combine(FilesDir!.AbsolutePath, "selftest.txt");
        Task.Run(() =>
        {
            try
            {
                var converter = MozcJniBridge.Create(this, learning: false);
                if (!converter.Candidates("ごじ").Contains("誤字")) throw new Exception("Whole-reading homophone candidate missing: 誤字");
                var preview = "";
                var session = new AndroidInputSession(converter, converter.Candidates, (operation, text, _) => { if (operation == 0) preview = text; });
                foreach (var c in "kyouhagoogledekensaku") session.Character(c);
                if (!preview.Contains("google") || !preview.Contains("検索")) throw new Exception("Mixed live conversion failed: " + preview);
                foreach (var c in ",.?")
                {
                    session.Character(c);
                    if (!preview.Contains("検索")) throw new Exception("Punctuation removed conversion");
                }
                var output = preview;
                RunOnUiThread(() =>
                {
                    try
                    {
                        var connection = editor.OnCreateInputConnection(new EditorInfo())!;
                        connection.SetComposingText(output, 1);
                        if (editor.Text != output) throw new Exception("Editor composition failed");
                        connection.CommitText(output, 1);
                        if (editor.Text != output) throw new Exception("Editor commit duplicated composition");
                        File.WriteAllText(path, "PASS: JNI dictionary, goji homophones, mixed live conversion, punctuation, Android editor composition and commit\n" + output);
                    }
                    catch (Exception ex) { File.WriteAllText(path, "FAIL: " + ex); }
                });
            }
            catch (Exception ex) { File.WriteAllText(path, "FAIL: " + ex); }
        });
    }
}

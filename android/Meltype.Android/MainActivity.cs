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

#if TRAINING_BUILD
[Activity(Name = "jp.aonesenbongi.meltype.MainActivity", Label = "Meltype 学習用", MainLauncher = true, Exported = true)]
#else
[Activity(Name = "jp.aonesenbongi.meltype.MainActivity", Label = "Meltype", MainLauncher = true, Exported = true)]
#endif
public sealed class MainActivity : Activity
{
    private TextView? _setupStatus;
    private Action? _refreshPreferences;
    private TextView? _dictionaryStatus;
    private string DictionaryPath => Path.Combine(FilesDir!.AbsolutePath, "user-dictionary.json");
    private int Dp(float value) => MobileStyle.Dp(this, value);
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        Window!.SetSoftInputMode(SoftInput.AdjustResize);
        if (OperatingSystem.IsAndroidVersionAtLeast(30) && !OperatingSystem.IsAndroidVersionAtLeast(35)) Window.SetDecorFitsSystemWindows(false);
        var layout = new LinearLayout(this) { Orientation = Orientation.Vertical };
        layout.SetPadding(Dp(24), Dp(28), Dp(24), Dp(24));
        layout.SetBackgroundColor(MobileStyle.Background);
        var header = new LinearLayout(this); header.SetGravity(GravityFlags.CenterVertical);
        var mark = new ImageView(this); mark.SetImageResource(Resource.Drawable.ic_meltype_mark);
        mark.ContentDescription = "Meltypeのアイコン";
        header.AddView(mark, new LinearLayout.LayoutParams(Dp(64), Dp(64)));
        var title = new LinearLayout(this) { Orientation = Orientation.Vertical };
        title.AddView(Label("Meltype", 28, MobileStyle.Ink));
        title.AddView(Label("ローマ字で、日英をそのまま。", 13, MobileStyle.Muted));
        header.AddView(title); layout.AddView(header);
        var intro = Label("日本語はライブ変換。英語は英字のまま。\n「自動／英語」ボタンで入力モードを切り替えられます。", 14, MobileStyle.Muted);
        intro.SetPadding(0, Dp(16), 0, Dp(20)); layout.AddView(intro);
        _setupStatus = Label("設定を確認しています…", 14, MobileStyle.Accent);
        _setupStatus.SetPadding(Dp(16), Dp(14), Dp(16), Dp(14));
        _setupStatus.Background = MobileStyle.Rounded(this, MobileStyle.Soft); layout.AddView(_setupStatus);
        Button Add(string text, Action action, bool primary = false)
        {
            var button = new Button(this) { Text = text, TextSize = 15 };
            MobileStyle.Button(button, primary);
            button.Click += (_, _) => action();
            layout.AddView(button, new LinearLayout.LayoutParams(-1, Dp(52)) { TopMargin = Dp(12) }); return button;
        }
        Add("1. キーボードを有効にする", () => StartActivity(new Intent(Settings.ActionInputMethodSettings)), true);
        Add("2. キーボードを選ぶ", () => ((InputMethodManager)GetSystemService(InputMethodService)!).ShowInputMethodPicker());
        _dictionaryStatus = Label("", 14, MobileStyle.Muted); layout.AddView(_dictionaryStatus);
        RefreshDictionary();
        Add("ユーザー辞書 · 登録／取り込み", ShowDictionary);
        var preferences = new InputPreferenceStore(Path.Combine(FilesDir!.AbsolutePath, "input-preferences.json"));
        layout.AddView(Label("日本語入力の句読点", 16, MobileStyle.Ink));
        Spinner Choice(string title, string[] values)
        {
            layout.AddView(Label(title, 14, MobileStyle.Muted));
            var spinner = new Spinner(this);
            var adapter = new ArrayAdapter<string>(this, global::Android.Resource.Layout.SimpleSpinnerItem, values);
            adapter.SetDropDownViewResource(global::Android.Resource.Layout.SimpleSpinnerDropDownItem);
            spinner.Adapter = adapter; layout.AddView(spinner); return spinner;
        }
        var commas = new[] { "、", "，", "," }; var periods = new[] { "。", "．", "." };
        var comma = Choice("読点", ["、（読点）", "，（全角カンマ）", ",（半角カンマ）"]);
        var period = Choice("句点", ["。（句点）", "．（全角ピリオド）", ".（半角ピリオド）"]);
        var learningStatus = Label("", 14, MobileStyle.Muted); layout.AddView(learningStatus);
        Action? refreshCollection = null;
        Button? learningButton = null;
        void RefreshPreferences()
        {
            var settings = preferences.Current;
            comma.SetSelection(Array.IndexOf(commas, settings.Comma));
            period.SetSelection(Array.IndexOf(periods, settings.Period));
            learningStatus.Text = settings.LearningEnabled ? "学習中" : "学習停止中 · 既存の辞書・履歴は残ります";
            if (learningButton != null) learningButton.Text = settings.LearningEnabled ? "学習を停止" : "学習を再開";
            refreshCollection?.Invoke();
        }
        Add("句読点を保存", () =>
        {
            try
            {
                preferences.Save(preferences.Current with { Comma = commas[comma.SelectedItemPosition], Period = periods[period.SelectedItemPosition] });
                Toast.MakeText(this, "句読点を保存しました。次の入力から反映します。", ToastLength.Short)!.Show();
            }
            catch (IOException) { Toast.MakeText(this, "設定を保存できません。空き容量を確認してください。", ToastLength.Long)!.Show(); }
        });
        learningButton = Add("学習を停止", () =>
        {
            try
            {
                var current = preferences.Current;
                preferences.Save(current with { LearningEnabled = !current.LearningEnabled });
#if TRAINING_BUILD
                if (current.LearningEnabled) new TypoTrainingStore(Path.Combine(FilesDir!.AbsolutePath, "typo-training")).Stop();
#endif
                RefreshPreferences();
            }
            catch (IOException) { Toast.MakeText(this, "設定を保存できません。空き容量を確認してください。", ToastLength.Long)!.Show(); }
        });
        _refreshPreferences = RefreshPreferences;
        RefreshPreferences();
#if TRAINING_BUILD
        var store = new TypoTrainingStore(Path.Combine(FilesDir!.AbsolutePath, "typo-training"));
        var collection = Label("", 14, MobileStyle.Ink); layout.AddView(collection);
        void UpdateCollection() => collection.Text = store.Active && preferences.Current.LearningEnabled
            ? $"収集中 · {store.Count}件 · {store.Until?.ToLocalTime():M月d日 HH:mm}まで"
            : $"収集停止中 · {store.Count}件";
        refreshCollection = UpdateCollection;
        UpdateCollection();
        Add("7日間の収集を開始", () =>
        {
            if (!preferences.Current.LearningEnabled) { Toast.MakeText(this, "先に学習を再開してください。", ToastLength.Short)!.Show(); return; }
            using var dialog = new AlertDialog.Builder(this);
            dialog.SetTitle("編集・句読点の履歴を端末内に保存します");
            dialog.SetMessage("削除前後の文字と、句読点入力直前の最大64文字・入力した句読点を記録します。機密情報を入力する時は停止してください。USBデバッグを許可したPCから取得できます。記録は自動修正に使いません。");
            dialog.SetPositiveButton("開始", (_, _) => { store.Start(); UpdateCollection(); });
            dialog.SetNegativeButton("取消", (_, _) => { }); dialog.Show();
        });
        Add("収集を停止", () => { store.Stop(); UpdateCollection(); });
        Add("端末の収集記録を削除", () =>
        {
            using var dialog = new AlertDialog.Builder(this);
            dialog.SetMessage("端末の編集記録を削除して収集を停止します。PCへ取得済みの記録は残ります。");
            dialog.SetPositiveButton("削除", (_, _) => { store.Clear(); UpdateCollection(); });
            dialog.SetNegativeButton("取消", (_, _) => { }); dialog.Show();
        });
#endif
        var trial = Label("試し書き", 16, MobileStyle.Ink); trial.SetPadding(0, Dp(24), 0, Dp(8)); layout.AddView(trial);
        var editor = new EditText(this) { Id = Resource.Id.test_editor, Hint = "kyouhagoogledekensaku", TextSize = 17, InputType = InputTypes.ClassText | InputTypes.TextFlagMultiLine, Gravity = GravityFlags.Top };
        editor.SetTextColor(MobileStyle.Ink); editor.SetHintTextColor(MobileStyle.Muted);
        editor.Background = MobileStyle.Rounded(this, Color.White);
        editor.SetPadding(Dp(16), Dp(16), Dp(16), Dp(16));
        layout.AddView(editor, new LinearLayout.LayoutParams(-1, Dp(120)));
        var tip = Label("Spaceで変換 · Enterで確定\n自動 → 日英自動判別 ／ 英語 → 英語専用\n入力中のカナ／かな → カタカナ／ひらがな", 12, MobileStyle.Muted);
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
        var about = Label($"Android試作版 {version}\n雪代／Yukishiro氏のMeltypeを基にした非公式Forkです。変換にはOSS版Mozcを使用し、入力は端末内で処理します。", 12, MobileStyle.Muted);
        about.SetPadding(0, Dp(18), 0, 0); layout.AddView(about);
        var page = new ScrollView(this) { FillViewport = true }; page.AddView(layout);
        var frame = new FrameLayout(this); frame.AddView(page, new FrameLayout.LayoutParams(-1, -1));
        void ScrollEditor() { if (editor.HasFocus) page.Post(() => page.SmoothScrollTo(0, Math.Max(0, editor.Top - Dp(16)))); }
        editor.FocusChange += (_, e) => { if (e.HasFocus) page.PostDelayed(ScrollEditor, 200); };
        frame.SetOnApplyWindowInsetsListener(new SetupInsets(Dp(12), ScrollEditor));
        SetContentView(frame); frame.RequestApplyInsets();
        if (Intent?.GetBooleanExtra("selftest", false) == true) RunSelfTest(editor);
    }
    private sealed class SetupInsets(int gap, Action scroll) : Java.Lang.Object, View.IOnApplyWindowInsetsListener
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
    protected override void OnResume()
    {
        base.OnResume();
        _refreshPreferences?.Invoke();
        var manager = (InputMethodManager)GetSystemService(InputMethodService)!;
        var enabled = manager.EnabledInputMethodList?.Any(method => method.PackageName == PackageName) == true;
        var selected = Settings.Secure.GetString(ContentResolver, Settings.Secure.DefaultInputMethod)?.StartsWith(PackageName + "/", StringComparison.Ordinal) == true;
        if (_setupStatus != null) _setupStatus.Text = selected ? "準備完了 · Meltypeを選択しています" : enabled ? "あと1つ · キーボードを選んでください" : "まずはキーボードを有効にしてください";
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
                        File.WriteAllText(path, "PASS: JNI dictionary, mixed live conversion, punctuation, Android editor composition and commit\n" + output);
                    }
                    catch (Exception ex) { File.WriteAllText(path, "FAIL: " + ex); }
                });
            }
            catch (Exception ex) { File.WriteAllText(path, "FAIL: " + ex); }
        });
    }
}

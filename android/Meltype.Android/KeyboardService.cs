// SPDX-License-Identifier: GPL-3.0-or-later
using Android.App;
using Android.Content;
using Android.Graphics;
using Android.InputMethodServices;
using Android.OS;
using Android.Text;
using Android.Views;
using Android.Views.InputMethods;
using Android.Widget;
using Keycode = Android.Views.Keycode;
using Path = System.IO.Path;
using Meltype.AndroidCore;
using Meltype.Composition;
using Meltype.Config;

namespace Meltype.Mobile;

#if TRAINING_BUILD
[Service(Label = "Meltype 学習用", Permission = "android.permission.BIND_INPUT_METHOD", Exported = true)]
#else
[Service(Label = "Meltype Android 試作版", Permission = "android.permission.BIND_INPUT_METHOD", Exported = true)]
#endif
[IntentFilter(new[] { "android.view.InputMethod" })]
[MetaData("android.view.im", Resource = "@xml/input_method")]
public sealed class KeyboardService : InputMethodService
{
    private HandlerThread? _thread;
    private Handler? _worker, _main;
    private AndroidInputSession? _session;
    private LinearLayout? _candidates;
    private string? _candidateSignature;
    private readonly List<KeyTouch> _touches = [];
    private Button? _mode;
    private Button? _shift;
    private Button? _punctuation;
    private CompositionView? _shownView;
    private readonly KeyboardShift _shiftState = new();
    private Button? _hide;
    private LinearLayout? _keyRows;
    private bool _symbols;
    private bool _shownKatakana, _choosingBrackets;
    private readonly List<(Button Key, char Letter)> _letters = [];
    private bool _english, _restricted, _ready;
    private bool _preeditActive;
    private int _generation;
    private int _jobGeneration;
    private InputPreferenceStore? _preferences;
    private char _punctuationInput = ',';
#if TRAINING_BUILD
    private TypoTrainingStore? _trainingStore;
    private TypoEditCapture? _editCapture;
    private bool _collectAllowed;
#endif

    public override void OnCreate()
    {
        base.OnCreate();
        _preferences = new InputPreferenceStore(System.IO.Path.Combine(FilesDir!.AbsolutePath, "input-preferences.json"));
#if TRAINING_BUILD
        _trainingStore = new TypoTrainingStore(System.IO.Path.Combine(FilesDir!.AbsolutePath, "typo-training"));
        _editCapture = new TypoEditCapture((before, after) =>
        {
            _worker?.Post(() =>
            {
                try { if (_preferences!.Current.LearningEnabled) _trainingStore.Append(before, after); }
                catch (IOException)
                {
                    try { _trainingStore.Stop(); } catch (IOException) { }
                    _main?.Post(() => Toast.MakeText(this, "履歴を保存できないため収集を停止しました。", ToastLength.Long)!.Show());
                }
            });
        });
#endif
        _main = new Handler(Looper.MainLooper!);
        _thread = new HandlerThread("MeltypeInput"); _thread.Start();
        _worker = new Handler(_thread.Looper!);
        _worker.Post(() =>
        {
            try
            {
                var mozc = MozcJniBridge.Create(this);
                mozc.LearningEnabled = () => _preferences.Current.LearningEnabled;
                _session = new AndroidInputSession(mozc, mozc.Candidates, Output, () => _preferences.Current,
                    userDictionary: new AndroidUserDictionary(Path.Combine(FilesDir!.AbsolutePath, "user-dictionary.json")));
                _main.Post(() => { _ready = true; Status(); });
            }
            catch (Exception) { _main.Post(() => Toast.MakeText(this, "変換エンジンを起動できません。入力方法を切り替えてください。", ToastLength.Long)!.Show()); }
        });
    }
    public override View OnCreateInputView()
    {
        _letters.Clear();
        var root = new LinearLayout(this) { Orientation = Orientation.Vertical };
        root.SetBackgroundColor(MobileStyle.KeyboardBackground);
        root.SetPadding(0, 0, 0, Dp(24));
        root.SetOnApplyWindowInsetsListener(new KeyboardInsets(Dp(24)));
        _candidateSignature = null;
        var scroll = new HorizontalScrollView(this) { HorizontalScrollBarEnabled = false };
        _candidates = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        _candidates.SetGravity(GravityFlags.CenterVertical); scroll.AddView(_candidates);
        scroll.SetBackgroundColor(Color.White);
        var candidateRow = new LinearLayout(this) { Orientation = Orientation.Horizontal, BaselineAligned = false };
        candidateRow.AddView(scroll, new LinearLayout.LayoutParams(0, Dp(42), 1));
        _hide = new Button(this) { Text = "⌄", TextSize = 24, ContentDescription = "キーボードを閉じる" };
        MobileStyle.KeyboardKey(_hide, function: true); _hide.Click += (_, _) => HideKeyboard();
        candidateRow.AddView(_hide, new LinearLayout.LayoutParams(Dp(44), Dp(42)));
        root.AddView(candidateRow, new LinearLayout.LayoutParams(-1, Dp(42)));
        _keyRows = new LinearLayout(this) { Orientation = Orientation.Vertical }; root.AddView(_keyRows);
        BuildKeyRows(); Status(); return root;
    }
    private void BuildKeyRows()
    {
        if (_keyRows == null) return;
        CancelTouches();
        _touches.Clear();
        _keyRows.RemoveAllViews(); _letters.Clear(); _shift = null;
        foreach (var row in _symbols ? new[] { "1234567890", "@#¥%&-+(/", "「.,?!:;\"" } : new[] { "qwertyuiop", "asdfghjkl-", "zxcvbnm" })
        {
            var line = new LinearLayout(this) { BaselineAligned = false };
            var shortRow = row.Length == 7;
            if (shortRow)
            {
                if (_symbols) AddKey(line, "ABC", () => { _symbols = false; BuildKeyRows(); Status(); }, 1.5f, description: "英字配列に戻る");
                else _shift = AddKey(line, "⇧", () => { _shiftState.Toggle(); Status(); }, 1.5f, description: "大文字・小文字を切り替える");
            }
            foreach (var c in row)
            {
                var pair = c switch { '(' => "()", '「' => "「」", _ => null };
                var key = pair != null ? AddKey(line, pair, () => BeginBrackets(pair), description: "括弧 " + pair + "の種類を選ぶ")
                    : AddKey(line, c.ToString(), () => Input(_shiftState.Apply(c)));
                if (char.IsLetter(c)) _letters.Add((key, c));
            }
            if (shortRow) AddKey(line, "⌫", () => Special(0x08), 1.5f, description: "削除、長押しで連続削除", repeat: true);
            else if (_symbols && row.StartsWith('「')) AddKey(line, "⌫", () => Special(0x08), description: "削除、長押しで連続削除", repeat: true);
            _keyRows.AddView(line);
        }
        var controls = new LinearLayout(this) { BaselineAligned = false };
        AddKey(controls, _symbols ? "ABC" : "123", () => { _symbols = !_symbols; BuildKeyRows(); Status(); }, description: "数字・記号と英字配列を切り替える");
        _mode = AddKey(controls, "日英", () =>
        {
#if TRAINING_BUILD
            _editCapture?.Cancel();
#endif
            if (_restricted || !_ready) return;
            if (!_english && !_choosingBrackets && !string.IsNullOrEmpty(_shownView?.Text))
            { Queue(() => _session?.ToggleKana()); return; }
            _english = !_english;
            var english = _english;
            Queue(() => _session?.SetEnglish(english)); Status();
        }, description: "英語専用モードに切り替える");
        _punctuation = AddKey(controls, ",", () => Input(_punctuationInput));
        _punctuation.ContentDescription = "句読点、文末はピリオド、長押しでピリオド";
        _punctuation.LongClick += (_, e) => { Input('.'); _shiftState.Consume(); Status(); e.Handled = true; };
        AddKey(controls, "空白", () => Input(' '), 3, description: "空白・変換");
        AddKey(controls, "←", () => Special(0x25), description: "左へ移動");
        AddKey(controls, "→", () => Special(0x27), description: "右へ移動");
        AddKey(controls, "↵", () => Special(0x0D), 2, description: "確定・改行"); _keyRows.AddView(controls);
    }
    private int Dp(int pixels) => MobileStyle.Dp(this, pixels);
    private Button AddKey(LinearLayout row, string label, Action action, float weight = 1, int height = 54, string? description = null, bool repeat = false)
    {
        var key = new Button(this) { Text = label, TextSize = label.Length > 1 ? 13 : 20, ContentDescription = description ?? label };
        key.SetSingleLine(true);
        MobileStyle.KeyboardKey(key, function: label.Length > 1 || label is "⇧" or "⌫" or "↵" or "←" or "→"); Action invoke = () => { action(); if (label != "⇧") { _shiftState.Consume(); Status(); } };
        key.Click += (_, _) => invoke();
        if (label != ",")
        {
            var touch = new KeyTouch(_main!, invoke, repeat);
            _touches.Add(touch); key.SetOnTouchListener(touch);
        }
        row.AddView(key, new LinearLayout.LayoutParams(0, Dp(height), weight)); return key;
    }
    public override bool OnEvaluateFullscreenMode() => false;
    public override void OnStartInput(EditorInfo? attribute, bool restarting)
    {
        base.OnStartInput(attribute, restarting);
        CancelTouches();
        Interlocked.Increment(ref _generation);
        _preeditActive = false; _shownView = null; _choosingBrackets = false; _shiftState.Reset();
        var type = attribute?.InputType ?? InputTypes.Null;
        var inputClass = type & InputTypes.MaskClass;
        var variation = type & InputTypes.MaskVariation;
        _restricted = inputClass is InputTypes.ClassNumber or InputTypes.ClassPhone or InputTypes.ClassDatetime ||
            variation is InputTypes.TextVariationPassword or InputTypes.TextVariationVisiblePassword or InputTypes.TextVariationWebPassword;
        var allowLearning = !_restricted && ((int)(attribute?.ImeOptions ?? 0) & 0x1000000) == 0;
#if TRAINING_BUILD
        _editCapture?.Cancel();
        _collectAllowed = TypoTrainingPolicy.Allows((int)type, (int)(attribute?.ImeOptions ?? 0));
#endif
        _candidates?.RemoveAllViews();
        _candidateSignature = null;
        if (_hide != null) _hide.Visibility = ViewStates.Visible;
        var english = _english;
        _worker?.Post(() =>
        {
            _session?.Reset();
            // Fixed policy per editor: an asynchronous learn operation cannot inherit another field's policy.
            var mozc = MozcJniBridge.Create(this, learning: allowLearning);
            mozc.LearningEnabled = () => _preferences!.Current.LearningEnabled;
            _session = new AndroidInputSession(mozc, mozc.Candidates, Output, () => _preferences!.Current, allowLearning,
                new AndroidUserDictionary(Path.Combine(FilesDir!.AbsolutePath, "user-dictionary.json")));
            _session.SetEnglish(english);
        }); Status();
    }
    public override void OnFinishInput()
    {
#if TRAINING_BUILD
        _editCapture?.Cancel();
        _collectAllowed = false;
#endif
        CancelTouches();
        CurrentInputConnection?.FinishComposingText();
        _preeditActive = false; _shownView = null; _choosingBrackets = false; _shiftState.Reset();
        Interlocked.Increment(ref _generation);
        _worker?.Post(() => _session?.Reset());
        _candidates?.RemoveAllViews(); base.OnFinishInput();
        _candidateSignature = null;
    }
    public override void OnFinishInputView(bool finishingInput)
    {
        CancelTouches(); _shiftState.Reset();
        if (_symbols) { _symbols = false; BuildKeyRows(); }
        Status(); base.OnFinishInputView(finishingInput);
    }
    public override void OnUpdateSelection(int oldSelStart, int oldSelEnd, int newSelStart, int newSelEnd, int candidatesStart, int candidatesEnd)
    {
        base.OnUpdateSelection(oldSelStart, oldSelEnd, newSelStart, newSelEnd, candidatesStart, candidatesEnd);
#if TRAINING_BUILD
        if (newSelStart != newSelEnd || candidatesStart >= 0 && newSelEnd != candidatesEnd ||
            candidatesStart < 0 && _preeditActive || candidatesStart < 0 && Math.Abs(newSelEnd - oldSelEnd) > 1)
            _editCapture?.Cancel();
#endif
        if (candidatesStart >= 0 && (newSelStart != candidatesEnd || newSelEnd != candidatesEnd))
        {
            CurrentInputConnection?.FinishComposingText();
            _preeditActive = false; _shownView = null;
            Interlocked.Increment(ref _generation);
            _worker?.Post(() => _session?.Reset()); _candidates?.RemoveAllViews();
            CancelTouches(); _candidateSignature = null;
        }
        RefreshPunctuation();
    }
    private void Input(char c)
    {
        if (_restricted) { CurrentInputConnection?.CommitText(c.ToString(), 1); return; }
#if TRAINING_BUILD
        if (Collecting())
        {
            if (char.IsWhiteSpace(c) || char.IsPunctuation(c)) FinishTrainingQueued();
            else _editCapture?.Typed();
        }
        else _editCapture?.Cancel();
#endif
        Queue(() => _session?.Character(c));
    }
    private void Special(int key)
    {
#if TRAINING_BUILD
        if (Collecting())
        {
            if (key == 0x08)
            {
                if (_editCapture?.Pending != true)
                    _editCapture?.BeforeDelete(CurrentInputConnection?.GetTextBeforeCursor(64, (GetTextFlags)0));
            }
            else if (key == 0x0D) FinishTrainingQueued();
            else _editCapture?.Cancel();
        }
        else _editCapture?.Cancel();
#endif
        if (_restricted)
        {
            if (key == 0x08) CurrentInputConnection?.DeleteSurroundingTextInCodePoints(1, 0);
            else if (key == 0x0D) Enter();
            else if (key is 0x25 or 0x27) MoveCursor(key == 0x25 ? Keycode.DpadLeft : Keycode.DpadRight);
            return;
        }
        if (key == 0x0D && _choosingBrackets && _symbols) { _symbols = false; BuildKeyRows(); Status(); }
        Queue(() => _session?.Key(key));
    }
    private void Queue(Action action)
    {
        if (!_ready) return;
        var generation = Volatile.Read(ref _generation);
        _worker!.Post(() =>
        {
            if (generation != Volatile.Read(ref _generation)) return;
            _jobGeneration = generation; action();
        });
    }
    private void Output(int operation, string text, CompositionView? view)
    {
        var generation = _jobGeneration;
        var katakana = _session?.Katakana == true;
        var choosingBrackets = _session?.ChoosingBrackets == true;
        _main!.Post(() =>
        {
            if (generation != Volatile.Read(ref _generation)) return;
            var editor = CurrentInputConnection; if (editor == null) return;
            if (operation == 0) { editor.SetComposingText(text, 1); _preeditActive = true; }
            else if (operation == 1) { editor.CommitText(text, 1); _preeditActive = false; }
            else if (operation == 2)
            {
                if (_preeditActive) editor.SetComposingText("", 1);
                editor.FinishComposingText(); _preeditActive = false;
            }
            else if (operation == 3) editor.DeleteSurroundingTextInCodePoints(int.Parse(text), 0);
            else if (operation == 4) Enter();
            else if (operation == 7) { editor.CommitText(text, 1); _preeditActive = false; MoveCursor(Keycode.DpadLeft); }
            else if (operation is 5 or 6) MoveCursor(operation == 5 ? Keycode.DpadLeft : Keycode.DpadRight);
            _shownView = view; _shownKatakana = katakana; _choosingBrackets = choosingBrackets; Status();
            if (_hide != null) _hide.Visibility = view is { Candidates.Count: > 0 } ? ViewStates.Gone : ViewStates.Visible;
            var signature = view == null ? "" : string.Join('\0', view.Candidates.Take(24)) + "\u0001" + view.SelectedIndex;
            if (_candidateSignature == signature) return;
            _candidateSignature = signature;
            _candidates?.RemoveAllViews();
            if (view is { Candidates.Count: > 0 } && _candidates != null)
                for (var i = 0; i < Math.Min(view.Candidates.Count, 24); i++)
                {
                    var index = i; var button = new Button(this) { Text = view.Candidates[index], ContentDescription = "候補 " + index };
                    MobileStyle.KeyboardKey(button); button.TextSize = 17;
                    button.SetPadding(Dp(12), 0, Dp(12), 0);
                    button.SetTypeface(null, index == view.SelectedIndex ? TypefaceStyle.Bold : TypefaceStyle.Normal);
                    button.Click += (_, _) =>
                    {
#if TRAINING_BUILD
                        FinishTrainingQueued();
#endif
                        _shiftState.Consume(); Status(); Queue(() => _session?.Select(index));
                    };
                    _candidates.AddView(button, new LinearLayout.LayoutParams(-2, Dp(42)));
                }
        });
    }
    private void BeginBrackets(string pair)
    {
#if TRAINING_BUILD
        FinishTrainingQueued();
#endif
        if (_restricted) { CurrentInputConnection?.CommitText(pair, 1); MoveCursor(Keycode.DpadLeft); return; }
        Queue(() => _session?.BeginBracketConversion(pair == "「」"));
    }
    private void MoveCursor(Keycode code)
    {
        var editor = CurrentInputConnection;
        if (editor == null) return;
        using var down = new global::Android.Views.KeyEvent(KeyEventActions.Down, code);
        using var up = new global::Android.Views.KeyEvent(KeyEventActions.Up, code);
        editor.SendKeyEvent(down); editor.SendKeyEvent(up);
    }
    private void Enter() { if (!SendDefaultEditorAction(true)) CurrentInputConnection?.CommitText("\n", 1); }
    private void HideKeyboard()
    {
#if TRAINING_BUILD
        FinishTrainingQueued();
#endif
        _shiftState.Reset(); Status(); CancelTouches();
        if (!_ready || _restricted) { RequestHideSelf((HideSoftInputFlags)0); return; }
        var generation = Volatile.Read(ref _generation);
        Queue(() =>
        {
            _session?.Commit();
            _main!.Post(() => { if (generation == Volatile.Read(ref _generation)) RequestHideSelf((HideSoftInputFlags)0); });
        });
    }
    private void RefreshPunctuation()
    {
        if (_punctuation == null) return;
        if (_restricted || _english) { _punctuationInput = ','; _punctuation.Text = ","; return; }
        var text = _shownView?.Text;
        if (string.IsNullOrEmpty(text)) text = CurrentInputConnection?.GetTextBeforeCursor(128, (GetTextFlags)0);
        _punctuationInput = AndroidInputSession.PredictPunctuation(text, false);
        var preferences = _preferences!.Current;
        _punctuation.Text = _punctuationInput == '.' ? preferences.Period : preferences.Comma;
    }
    private void Status()
    {
        RefreshPunctuation();
        if (_mode != null)
        {
            var pending = !_restricted && !_english && !_choosingBrackets && !string.IsNullOrEmpty(_shownView?.Text);
            _mode.Text = pending ? (_shownKatakana ? "かな" : "カナ") : _restricted || _english ? "英語" : "自動";
            _mode.ContentDescription = pending ? (_shownKatakana ? "未確定文字をひらがなにする" : "未確定文字をカタカナにする") : _english ? "日英自動判別に戻る" : "英語専用モードに切り替える";
            _mode.Enabled = _ready && !_restricted;
        }
        foreach (var (key, letter) in _letters) key.Text = (_shiftState.Active ? char.ToUpperInvariant(letter) : letter).ToString();
        if (_shift != null) { _shift.Text = _shiftState.Locked ? "⇪" : "⇧";
            _shift.ContentDescription = _shiftState.Locked ? "大文字固定を解除" : "Shift、2回押すと大文字固定";
            _shift.SetTypeface(null, _shiftState.Active ? TypefaceStyle.Bold : TypefaceStyle.Normal); }
    }
    public override void OnDestroy()
    {
        CancelTouches();
        Interlocked.Increment(ref _generation); _thread?.QuitSafely(); base.OnDestroy();
    }

    private void CancelTouches() { foreach (var touch in _touches) touch.Cancel(); }
#if TRAINING_BUILD
    private bool Collecting() => _collectAllowed && _trainingStore?.Active == true && _preferences?.Current.LearningEnabled == true;
    private void FinishTrainingQueued()
    {
        if (_editCapture?.Pending != true) return;
        var generation = Volatile.Read(ref _generation);
        // Wait for preceding worker output to reach the editor; snapshot precedes the next conversion/commit.
        Queue(() => _main!.Post(() =>
        {
            if (generation != Volatile.Read(ref _generation) || !Collecting()) { _editCapture?.Cancel(); return; }
            _editCapture?.Finish(CurrentInputConnection?.GetTextBeforeCursor(64, (GetTextFlags)0));
        }));
    }
#endif

    private sealed class KeyTouch : Java.Lang.Object, View.IOnTouchListener
    {
        private readonly Handler _handler;
        private readonly Action _action;
        private readonly bool _repeats;
        private readonly Java.Lang.Runnable _repeat;
        private View? _view;
        private bool _held;
        public KeyTouch(Handler handler, Action action, bool repeats)
        {
            _handler = handler; _action = action; _repeats = repeats;
            _repeat = new Java.Lang.Runnable(() => { if (!_held) return; _action(); _handler.PostDelayed(_repeat!, 60); });
        }
        public bool OnTouch(View? view, MotionEvent? e)
        {
            if (view == null || e == null) return false;
            if (e.ActionMasked == MotionEventActions.Down)
            {
                Cancel(); _view = view; _held = true; view.Pressed = true; _action();
                if (_repeats) _handler.PostDelayed(_repeat, 400);
            }
            else if (e.ActionMasked is MotionEventActions.Up or MotionEventActions.Cancel ||
                e.ActionMasked == MotionEventActions.Move && (e.GetX() < 0 || e.GetX() >= view.Width || e.GetY() < 0 || e.GetY() >= view.Height)) Cancel();
            return true;
        }
        public void Cancel() { _held = false; _handler.RemoveCallbacks(_repeat); if (_view != null) _view.Pressed = false; _view = null; }
    }

    private sealed class KeyboardInsets(int padding) : Java.Lang.Object, View.IOnApplyWindowInsetsListener
    {
        public WindowInsets OnApplyWindowInsets(View? view, WindowInsets? insets)
        {
            if (view == null || insets == null) throw new ArgumentNullException();
            var bottom = OperatingSystem.IsAndroidVersionAtLeast(30)
                ? insets.GetInsets(WindowInsets.Type.NavigationBars())!.Bottom
                : insets.SystemWindowInsetBottom;
            view.SetPadding(view.PaddingLeft, view.PaddingTop, view.PaddingRight, padding + bottom);
            return insets;
        }
    }
}

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
using Resource = Meltype.Android.Resource;
using Meltype.AndroidCore;
using Meltype.Composition;
using Meltype.Config;

namespace Meltype.Mobile;

[Service(Label = "Meltype Android", Permission = "android.permission.BIND_INPUT_METHOD", Exported = true)]
[IntentFilter(new[] { "android.view.InputMethod" })]
[MetaData("android.view.im", Resource = "@xml/input_method")]
public sealed partial class KeyboardService : InputMethodService
{
    private View? _inputRoot;
    private KeyboardInsets? _keyboardInsets;
    private HandlerThread? _thread;
    private Handler? _worker, _main;
    private AndroidInputSession? _session;
    private LinearLayout? _candidates;
    private HorizontalScrollView? _candidateScroll;
    private NextKeyPrediction? _nextKeyPrediction;
    private string _predictionPrefix = "";
    private LetterTouch? _pendingLetterTouch;
    private bool _adaptiveAllowed;
    private string? _contextBefore, _contextAfter;
    private string? _candidateSignature;
    private readonly List<KeyTouch> _touches = [];
    private readonly KeyPressOrder _pressOrder = new();
    private Button? _mode;
    private Button? _shift;
    private Button? _punctuation;
    private TextView? _punctuationHint;
    private char _punctuationAlternate = '.';
    private CompositionView? _shownView;
    private readonly KeyboardShift _shiftState = new();
    private Button? _hide, _clipboardButton;
    private bool _clipboardOpen;
    private bool _clipboardAllowed;
    private ClipboardSuggestion _clipboardSuggestion = new();
    private ISharedPreferences? _clipboardOfferPreferences;
    private readonly ClipboardHistory _clipboardHistory = new();
    private LinearLayout? _keyRows;
    private LinearLayout? _keyboardBody;
    private View? _leftHandSpace, _rightHandSpace;
    private Button? _oneHandButton;
    private ISharedPreferences? _layoutPreferences;
    private int _oneHandMode; // 0: full width, 1: left, 2: right
    private bool _symbols;
    private string[]? _numberRows;
    private bool _phone;
    private bool _shownKatakana, _choosingBrackets;
    private readonly List<(Button Key, char Letter)> _letters = [];
    private bool _english, _restricted, _ready;
    private bool _passwordField;
    private bool _preeditActive;
    private int _generation;
    private int _jobGeneration;
    private List<Action>? _editorUpdates; // Accessed only by the serialized worker.
    private InputPreferenceStore? _preferences;
    private InputPreferences _activePreferences = new();
    private char _punctuationInput = ',';

    public override void OnCreate()
    {
        SetTheme(Resource.Style.MeltypeImeTheme);
        base.OnCreate();
        if (OperatingSystem.IsAndroidVersionAtLeast(29) && Window?.Window is { } imeWindow)
            imeWindow.NavigationBarContrastEnforced = false;
        _layoutPreferences = GetSharedPreferences("keyboard-layout", FileCreationMode.Private);
        _oneHandMode = _layoutPreferences!.GetInt("one-hand-mode", 0);
        if (_oneHandMode is < 0 or > 2) _oneHandMode = 0;
        _clipboardOfferPreferences = GetSharedPreferences("clipboard-offer", FileCreationMode.Private);
        _clipboardSuggestion = new ClipboardSuggestion(_clipboardOfferPreferences!.GetLong("last-copy", 0));
        _preferences = new InputPreferenceStore(System.IO.Path.Combine(FilesDir!.AbsolutePath, "input-preferences.json"));
        _main = new Handler(Looper.MainLooper!);
        _thread = new HandlerThread("MeltypeInput"); _thread.Start();
        _worker = new Handler(_thread.Looper!);
        _worker.Post(() =>
        {
            try
            {
                var mozc = MozcJniBridge.Create(this);
                var prediction = NextKeyPrediction.BuiltIn();
                _activePreferences = _preferences!.Current;
                mozc.LearningEnabled = () => _preferences.Current.LearningEnabled;
                _session = new AndroidInputSession(mozc, mozc.Candidates, Output, () => _activePreferences,
                    userDictionary: new AndroidUserDictionary(Path.Combine(FilesDir!.AbsolutePath, "user-dictionary.json")), personalTypos: LoadPersonalTypos(), clipboard: ReadClipboardCandidate,
                    surrounding: () => (_contextBefore, _contextAfter), stateDirectory: FilesDir!.AbsolutePath);
                _main.Post(() => { _nextKeyPrediction = prediction; _ready = true; Status(); });
            }
            catch (Exception) { _main.Post(() => Toast.MakeText(this, "変換エンジンを起動できません。入力方法を切り替えてください。", ToastLength.Long)!.Show()); }
        });
    }
    public override void OnWindowShown()
    {
        base.OnWindowShown();
        if (_inputRoot is { } root)
            root.Post(() => { root.RequestApplyInsets(); _keyboardInsets?.Apply(root); });
    }
    public override View OnCreateInputView()
    {
        _letters.Clear();
        var root = new LinearLayout(this) { Orientation = Orientation.Vertical };
        root.SetBackgroundColor(MobileStyle.KeyboardBackground);
        _inputRoot = root;
        var keyboardInsets = _keyboardInsets = new KeyboardInsets();
        root.SetOnApplyWindowInsetsListener(keyboardInsets);
        root.LayoutChange += (_, _) => root.Post(() => keyboardInsets.Apply(root));
        root.Post(() => { root.RequestApplyInsets(); keyboardInsets.Apply(root); });
        var keyboardArea = new LinearLayout(this) { Orientation = Orientation.Horizontal, BaselineAligned = false };
        _keyboardBody = new LinearLayout(this) { Orientation = Orientation.Vertical };
        _leftHandSpace = new View(this); _rightHandSpace = new View(this);
        keyboardArea.AddView(_leftHandSpace, new LinearLayout.LayoutParams(0, Dp(258), 1));
        keyboardArea.AddView(_keyboardBody, new LinearLayout.LayoutParams(0, Dp(258), 4));
        keyboardArea.AddView(_rightHandSpace, new LinearLayout.LayoutParams(0, Dp(258), 1));
        root.AddView(keyboardArea, new LinearLayout.LayoutParams(-1, Dp(258)));
        ApplyHandLayout();
        _candidateSignature = null;
        var scroll = new HorizontalScrollView(this) { HorizontalScrollBarEnabled = false };
        _candidateScroll = scroll;
        _candidates = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        _candidates.SetGravity(GravityFlags.CenterVertical); scroll.AddView(_candidates);
        scroll.SetBackgroundColor(Color.White);
        var candidateRow = new LinearLayout(this) { Orientation = Orientation.Horizontal, BaselineAligned = false };
        _clipboardButton = new Button(this) { Text = "▤", TextSize = 23, ContentDescription = "クリップボードを開く" };
        MobileStyle.KeyboardKey(_clipboardButton, function: true); _clipboardButton.Click += (_, _) =>
        {
            if (_clipboardOpen) { BuildKeyRows(); Status(); }
            else ShowClipboard();
        };
        candidateRow.AddView(_clipboardButton, new LinearLayout.LayoutParams(Dp(44), Dp(42)));
        _oneHandButton = new Button(this) { Text = "片手", TextSize = 12, ContentDescription = "片手モードを右寄せ・左寄せ・全幅の順に切り替える" };
        MobileStyle.KeyboardKey(_oneHandButton, function: true);
        _oneHandButton.Click += (_, _) => SetHandLayout(_oneHandMode switch { 0 => 2, 2 => 1, _ => 0 });
        candidateRow.AddView(_oneHandButton, new LinearLayout.LayoutParams(Dp(44), Dp(42)));
        candidateRow.AddView(scroll, new LinearLayout.LayoutParams(0, Dp(42), 1));
        _hide = new Button(this) { Text = "⌄", TextSize = 24, ContentDescription = "キーボードを閉じる" };
        MobileStyle.KeyboardKey(_hide, function: true); _hide.Click += (_, _) => HideKeyboard();
        candidateRow.AddView(_hide, new LinearLayout.LayoutParams(Dp(44), Dp(42)));
        _keyboardBody.AddView(candidateRow, new LinearLayout.LayoutParams(-1, Dp(42)));
        _keyRows = new LinearLayout(this) { Orientation = Orientation.Vertical }; _keyboardBody.AddView(_keyRows, new LinearLayout.LayoutParams(-1, Dp(216)));
        BuildKeyRows(); Status(); return root;
    }
    private void SetHandLayout(int mode)
    {
        CancelTouches();
        _oneHandMode = mode;
        _layoutPreferences!.Edit()!.PutInt("one-hand-mode", mode)!.Apply();
        ApplyHandLayout();
    }
    private void ApplyHandLayout()
    {
        if (_leftHandSpace == null || _rightHandSpace == null) return;
        _leftHandSpace.Visibility = _oneHandMode == 2 ? ViewStates.Visible : ViewStates.Gone;
        _rightHandSpace.Visibility = _oneHandMode == 1 ? ViewStates.Visible : ViewStates.Gone;
    }

    private void BuildKeyRows()
    {
        if (_keyRows == null) return;
        CancelTouches();
        _clipboardOpen = false;
        _touches.Clear();
        _pendingLetterTouch = null; _predictionPrefix = "";
        _keyRows.RemoveAllViews(); _letters.Clear(); _shift = null; _mode = null; _punctuation = null;
        if (_numberRows != null)
        {
            for (var i = 0; i < _numberRows.Length; i++)
            {
                var line = new LinearLayout(this) { BaselineAligned = false };
                foreach (var c in _numberRows[i])
                {
                    if (c == ' ') line.AddView(new View(this), new LinearLayout.LayoutParams(0, Dp(54), 1));
                    else AddKey(line, c.ToString(), () => Input(c),
                        longAction: _phone && c == '0' ? () => Input('+') : null);
                }
                if (i == 0) AddKey(line, "⌫", () => Special(0x08), description: "削除、長押しで連続削除", repeat: true);
                else if (i == 1) AddKey(line, "←", () => Special(0x25), description: "左へ移動");
                else if (i == 2) AddKey(line, "→", () => Special(0x27), description: "右へ移動");
                else AddKey(line, "↵", () => Special(0x0D), description: "確定・次へ");
                _keyRows.AddView(line);
            }
            return;
        }
        foreach (var row in _symbols ? new[] { "1234567890", "@#¥%&-+(/「", "_':;\"!?" } : new[] { "qwertyuiop", "asdfghjkl-", "zxcvbnm" })
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
                    : AddKey(line, c.ToString(), () => Input(_shiftState.Apply(c)), hint: _symbols ? null : KeyboardSymbols.For(c),
                        longAction: _symbols || KeyboardSymbols.For(c) == null ? null : () => InputSymbol(KeyboardSymbols.For(c)!));
                if (char.IsLetter(c)) _letters.Add((key, c));
            }
            if (shortRow) AddKey(line, "⌫", () => Special(0x08), 1.5f, description: "削除、長押しで連続削除", repeat: true);
            _keyRows.AddView(line);
        }
        var controls = new LinearLayout(this) { BaselineAligned = false };
        AddKey(controls, _symbols ? "ABC" : "123", () => { _symbols = !_symbols; BuildKeyRows(); Status(); }, description: "数字・記号と英字配列を切り替える", longAction: ShowExtraSymbols);
        _mode = AddKey(controls, "日英", () =>
        {
            _predictionPrefix = "";
            if (_restricted || !_ready) return;
            if (!_english && !_choosingBrackets && !string.IsNullOrEmpty(_shownView?.Text))
            { Queue(() => _session?.ToggleKana()); return; }
            _english = !_english;
            var english = _english;
            Queue(() => _session?.SetEnglish(english)); Status();
        }, description: "英語専用モードに切り替える", hint: "▴", longAction: ShowCharacterModes, longPressEnabled: false);
        _punctuation = AddKey(controls, ",", () => Input(_punctuationInput), hint: ".", longAction: () => Input(_punctuationAlternate));
        _punctuationHint = (TextView)((FrameLayout)_punctuation.Parent!).GetChildAt(1)!;
        _punctuation.ContentDescription = "句読点、右上の文字は上フリック・長押しで入力";
        AddKey(controls, "空白", InputSpace, 3, description: "空白・変換");
        AddKey(controls, "←", () => Special(0x25), description: "左へ移動");
        AddKey(controls, "→", () => Special(0x27), description: "右へ移動");
        AddKey(controls, "↵", () => Special(0x0D), 2, description: "確定・改行"); _keyRows.AddView(controls);
    }
    private int Dp(int pixels) => MobileStyle.Dp(this, pixels);
    private Button AddKey(LinearLayout row, string label, Action action, float weight = 1, int height = 54, string? description = null, bool repeat = false, string? hint = null, Action? longAction = null, bool longPressEnabled = true)
    {
        var key = new Button(this) { Text = label, TextSize = label.Length > 1 ? 13 : 20, ContentDescription = description ?? label };
        key.SetSingleLine(true);
        MobileStyle.KeyboardKey(key, function: label is "⇧" or "⌫" or "↵" or "←" or "→" or "ABC" or "123" or "日英" or "空白" or "全消去"); Action invoke = () => { action(); if (label != "⇧") { _shiftState.Consume(); Status(); } };
        key.Click += (_, _) => { _pressOrder.BeforePress(); invoke(); };
        {
            var touch = new KeyTouch(_main!, invoke, repeat, _pressOrder, ViewConfiguration.Get(this)!.ScaledTouchSlop,
                longAction == null ? null : () => { _predictionPrefix = ""; _pendingLetterTouch = null; longAction(); _shiftState.Consume(); Status(); },
                label.Length == 1 && label[0] is >= 'a' and <= 'z' ? CaptureLetterTouch : null, hint == null ? 0 : Dp(24), longPressEnabled);
            _touches.Add(touch); key.SetOnTouchListener(touch);
        }
        View cell = key;
        if (hint != null)
        {
            var frame = new FrameLayout(this); frame.AddView(key, new FrameLayout.LayoutParams(-1,-1));
            var mark = new TextView(this) { Text = hint, TextSize = 11,
                Gravity = GravityFlags.Top | GravityFlags.Right, ImportantForAccessibility = ImportantForAccessibility.No,
                Clickable = false, Focusable = false };
            mark.SetIncludeFontPadding(false); mark.SetTextColor(MobileStyle.Muted);
            frame.AddView(mark, new FrameLayout.LayoutParams(Dp(24), Dp(18), GravityFlags.Top | GravityFlags.Right)
                { TopMargin = Dp(4), RightMargin = Dp(4) }); cell = frame;
        }
        row.AddView(cell, new LinearLayout.LayoutParams(0, Dp(height), weight)); return key;
    }
    public override bool OnEvaluateFullscreenMode() => false;

    public override bool OnEvaluateInputViewShown() => _passwordField || base.OnEvaluateInputViewShown();
    public override void OnStartInput(EditorInfo? attribute, bool restarting)
    {
        base.OnStartInput(attribute, restarting);
        _consumedKeys.Clear();
        _english = false;
        if (!restarting) _clipboardSuggestion.Dismiss();
        _predictionPrefix = "";
        if (_clipboardOpen) BuildKeyRows();
        CancelTouches();
        Interlocked.Increment(ref _generation);
        _preeditActive = false; _shownView = null; _choosingBrackets = false; _shiftState.Reset();
        var type = attribute?.InputType ?? InputTypes.Null;
        var inputClass = type & InputTypes.MaskClass;
        _keyTextEditor = inputClass == InputTypes.ClassText;
        var variation = type & InputTypes.MaskVariation;
        _passwordField = variation is InputTypes.TextVariationPassword or InputTypes.TextVariationVisiblePassword
            or InputTypes.TextVariationWebPassword or InputTypes.NumberVariationPassword;
        var wasNumeric = _numberRows != null;
        _clipboardAllowed = ClipboardInputPolicy.Allows((int)type);
        _numberRows = NumericKeyboard.Rows((int)type);
        _phone = inputClass == InputTypes.ClassPhone;
        if (wasNumeric || _numberRows != null) { _symbols = false; BuildKeyRows(); }
        _restricted = inputClass is InputTypes.ClassNumber or InputTypes.ClassPhone or InputTypes.ClassDatetime ||
            variation is InputTypes.TextVariationPassword or InputTypes.TextVariationVisiblePassword or InputTypes.TextVariationWebPassword;
        var allowLearning = !_restricted && ((int)(attribute?.ImeOptions ?? 0) & 0x1000000) == 0;
        _adaptiveAllowed = InputPersonalizationPolicy.Allows((int)type, (int)(attribute?.ImeOptions ?? 0));
        var personalCandidatesAllowed = _adaptiveAllowed;
        _candidates?.RemoveAllViews();
        _candidateSignature = null;
        if (_hide != null) _hide.Visibility = ViewStates.Visible;
        var english = _english;
        _worker?.Post(() =>
        {
            _activePreferences = _preferences!.Current;
            _session?.Reset();
            // Fixed policy per editor: an asynchronous learn operation cannot inherit another field's policy.
            var mozc = MozcJniBridge.Create(this, learning: allowLearning);
            mozc.LearningEnabled = () => _preferences!.Current.LearningEnabled;
            _session = new AndroidInputSession(mozc, mozc.Candidates, Output, () => _activePreferences, allowLearning,
                new AndroidUserDictionary(Path.Combine(FilesDir!.AbsolutePath, "user-dictionary.json")),
                personalCandidatesAllowed ? LoadPersonalTypos() : null, clipboard: ReadClipboardCandidate,
                surrounding: () => (_contextBefore, _contextAfter), stateDirectory: FilesDir!.AbsolutePath,
                contextEvaluator: ContextEvaluatorForEditor(allowLearning), coherentRomanReview: true);
            _session.SetEnglish(english);
        }); Status();
    }
    public override void OnStartInputView(EditorInfo? info, bool restarting)
    {
        base.OnStartInputView(info, restarting);
        if (_passwordField)
            Window?.Window?.DecorView?.Post(() => RequestShowSelf(ShowFlags.Implicit));
        RefreshClipboardSuggestion(offer: true);
        Status();
    }

    public override void OnFinishInput()
    {
        _consumedKeys.Clear();_keyTextEditor=false;
        CancelTouches();
        CurrentInputConnection?.FinishComposingText();
        _preeditActive = false; _shownView = null; _choosingBrackets = false; _shiftState.Reset();
        Interlocked.Increment(ref _generation);
        _worker?.Post(() => { _session?.Reset(); _zenzEvaluator?.ClearCaches(); });
        _candidates?.RemoveAllViews(); base.OnFinishInput();
        _candidateSignature = null;
    }
    public override void OnFinishInputView(bool finishingInput)
    {
        if (finishingInput) _clipboardSuggestion.Dismiss();
        CancelTouches(); _shiftState.Reset();
        _english = false;
        Queue(() => _session?.SetEnglish(false));
        if (_symbols || _clipboardOpen) { _symbols = false; BuildKeyRows(); }
        Status(); base.OnFinishInputView(finishingInput);
    }
    public override void OnUpdateSelection(int oldSelStart, int oldSelEnd, int newSelStart, int newSelEnd, int candidatesStart, int candidatesEnd)
    {
        base.OnUpdateSelection(oldSelStart, oldSelEnd, newSelStart, newSelEnd, candidatesStart, candidatesEnd);
        if (EditorComposition.ShouldReset(_preeditActive, newSelStart, newSelEnd, candidatesStart, candidatesEnd))
        {
            CurrentInputConnection?.FinishComposingText();
            _preeditActive = false; _shownView = null; _shownKatakana = false; _choosingBrackets = false;
            Interlocked.Increment(ref _generation);
            _worker?.Post(() => _session?.Reset()); _candidates?.RemoveAllViews();
            CancelTouches(); _candidateSignature = null;
            Status();
            if (_hide != null) _hide.Visibility = ViewStates.Visible;
            if (_clipboardButton != null) _clipboardButton.Visibility = _clipboardAllowed ? ViewStates.Visible : ViewStates.Gone;
        }
        else if (_preeditActive && candidatesStart >= 0 &&
            newSelStart >= candidatesStart && newSelEnd <= candidatesEnd &&
            (newSelStart != candidatesEnd || newSelEnd != candidatesEnd))
        {
            var offset = Math.Min(newSelStart, newSelEnd) - candidatesStart;
            Queue(() => _session?.SelectPosition(offset));
        }
        RefreshPunctuation();
    }
    private void Input(char c)
    {
        var touch = _pendingLetterTouch?.Original.Letter == c ? _pendingLetterTouch : null;
        _pendingLetterTouch = null;
        _clipboardSuggestion.Dismiss();
        _predictionPrefix = char.ToLowerInvariant(c) is >= 'a' and <= 'z'
            ? (_predictionPrefix + char.ToLowerInvariant(c))[^Math.Min(_predictionPrefix.Length + 1, 32)..] : "";
        if (_restricted) { CurrentInputConnection?.CommitText(c.ToString(), 1); return; }
        Queue(() => _session?.Character(c, touch));
    }
    private void InputSpace()
        =>InputSpace(_shiftState.Active);
    private void InputSpace(bool shifted)
    {
        _clipboardSuggestion.Dismiss();
        _predictionPrefix = "";
        var p = _preferences!.Current;
        var wide = p.FullWidthSpaceByDefault ? !shifted : shifted && !p.ShiftSpaceHalfWidth;
        if (_restricted) CurrentInputConnection?.CommitText(wide ? "　" : " ", 1);
        else Queue(() => _session?.SpaceKey(shifted));
    }

    private void Special(int key)
    {
        var shift = _shiftState.Active;
        Special(key,shift);
    }
    private void Special(int key,bool shift)
    {
        _clipboardSuggestion.Dismiss();
        _predictionPrefix = key == 0x08 && _predictionPrefix.Length > 0 ? _predictionPrefix[..^1] : "";
        if (_restricted)
        {
            if (key == 0x08) CurrentInputConnection?.DeleteSurroundingTextInCodePoints(1, 0);
            else if (key == 0x0D) Enter();
            else if (key is 0x25 or 0x27) MoveCursor(key == 0x25 ? Keycode.DpadLeft : Keycode.DpadRight);
            return;
        }
        if (key == 0x0D && _choosingBrackets && _symbols) { _symbols = false; BuildKeyRows(); Status(); }
        Queue(() => _session?.Key(key, shift: shift));
    }
    private void Queue(Action action)
    {
        if (!_ready) return;
        _contextBefore = _adaptiveAllowed && !_restricted ? CurrentInputConnection?.GetTextBeforeCursor(128, (GetTextFlags)0) : null;
        _contextAfter = _adaptiveAllowed && !_restricted ? CurrentInputConnection?.GetTextAfterCursor(64, (GetTextFlags)0) : null;
        var generation = Volatile.Read(ref _generation);
        var preferences = _preferences!.Current;
        _worker!.Post(() =>
        {
            if (generation != Volatile.Read(ref _generation)) return;
            _jobGeneration = generation;
            _activePreferences = preferences;
            _editorUpdates = [];
            try { action(); }
            finally
            {
                ScheduleZenzPoll();
                var updates = _editorUpdates.ToArray();
                _editorUpdates = null;
                if (updates.Length > 0) _main!.Post(() =>
                {
                    if (generation != Volatile.Read(ref _generation)) return;
                    var editor = CurrentInputConnection;
                    if (editor == null) return;
                    editor.BeginBatchEdit();
                    try
                    {
                        foreach (var update in updates) update();
                        // A new composition can emit Hide followed by Show in the same edit.
                        if (string.IsNullOrEmpty(_shownView?.Text)) _predictionPrefix = "";
                    }
                    finally { editor.EndBatchEdit(); }
                });
            }
        });
    }
    private void PostEditorUpdate(Action update)
    {
        if (_editorUpdates is { } updates) updates.Add(update);
        else _main!.Post(update);
    }
    private void Output(int operation, string text, CompositionView? view)
    {
        var generation = _jobGeneration;
        var katakana = _session?.Katakana == true;
        var choosingBrackets = _session?.ChoosingBrackets == true;
        PostEditorUpdate(() =>
        {
            if (generation != Volatile.Read(ref _generation)) return;
            var editor = CurrentInputConnection; if (editor == null) return;
            if (operation == 0) { editor.SetComposingText(text, 1); _preeditActive = true; }
            else if (operation == 1) {
                editor.CommitText(text, 1); _preeditActive = false; }
            else if (operation == 2)
            {
                if (_preeditActive) editor.SetComposingText("", 1);
                editor.FinishComposingText(); _preeditActive = false;
            }
            else if (operation == 3) editor.DeleteSurroundingTextInCodePoints(int.Parse(text), 0);
            else if (operation == 4) Enter();
            else if (operation == 7) { editor.CommitText(text, 1); _preeditActive = false; MoveCursor(Keycode.DpadLeft); }
            else if (operation is 5 or 6) MoveCursor(operation == 5 ? Keycode.DpadLeft : Keycode.DpadRight);
            var previousView = _shownView;
            if (operation is 1 or 7) _predictionPrefix = "";
            if (operation is 1 or 2 or 7 || previousView?.SelectedClause != view?.SelectedClause ||
                previousView?.Text != view?.Text || previousView?.Candidates.Count is not > 0)
                _candidateScroll?.ScrollTo(0, 0);
            _shownView = view; _shownKatakana = katakana; _choosingBrackets = choosingBrackets;
            if (_clipboardOpen && view is { Candidates.Count: > 0 }) BuildKeyRows();
            Status();
            if (_hide != null) _hide.Visibility = view is { Candidates.Count: > 0 } ? ViewStates.Gone : ViewStates.Visible;
            if (_clipboardButton != null) _clipboardButton.Visibility = !_clipboardAllowed || view is { Candidates.Count: > 0 } ? ViewStates.Gone : ViewStates.Visible;
        });
    }
    private void RenderCandidates()
    {
        var view = _shownView;
        var suggestion = _clipboardAllowed && !_clipboardOpen &&
            string.IsNullOrEmpty(view?.Text) && _clipboardHistory.Items.Contains(_clipboardSuggestion.Text ?? "")
                ? _clipboardSuggestion.Text : null;
        var signature = view is { Candidates.Count: > 0 }
                ? "c:" + string.Join('\0', view.Candidates.Take(24)) + "\u0001" + view.SelectedIndex
                : "p:" + suggestion;
        signature += "\u0002" + view?.Typed + "\u0003" + string.Join('\0', view?.Predictions ?? []);
        if (_candidateSignature == signature) return;
        _candidateSignature = signature;
        _candidates?.RemoveAllViews();
        if (!string.IsNullOrEmpty(view?.Typed) && _candidates != null)
        {
            var typed = new TextView(this) { Text = view.Typed, TextSize = 13, ContentDescription = "入力したローマ字" };
            typed.SetTextColor(MobileStyle.Muted); typed.Gravity = GravityFlags.Center;
            typed.SetPadding(Dp(8), 0, Dp(8), 0); _candidates.AddView(typed, new LinearLayout.LayoutParams(-2, Dp(42)));
        }
        if (view is { Candidates.Count: > 0 } && _candidates != null)
            for (var i = 0; i < Math.Min(view.Candidates.Count, 24); i++)
            {
                var index = i; var button = new Button(this) { Text = view.Candidates[index], ContentDescription = "候補 " + index };
                MobileStyle.KeyboardKey(button); button.TextSize = 17;
                button.SetPadding(Dp(12), 0, Dp(12), 0);
                button.SetTypeface(null, index == view.SelectedIndex ? TypefaceStyle.Bold : TypefaceStyle.Normal);
                button.Click += (_, _) =>
                {
                    _shiftState.Consume(); Status(); Queue(() => _session?.Select(index));
                };
                _candidates.AddView(button, new LinearLayout.LayoutParams(-2, Dp(42)));
            }
        if (view is { Predictions.Count: > 0 } && _candidates != null)
            for (var i = 0; i < view.Predictions.Count; i++)
            {
                var index = i;
                var predicted = new Button(this) { Text = view.Predictions[index], TextSize = 17, ContentDescription = "予測候補 " + i };
                MobileStyle.KeyboardKey(predicted);
                predicted.Click += (_,_) => Queue(() => _session?.SelectPrediction(index));
                predicted.LongClick += (_,e) => { Queue(() => _session?.ForgetPrediction(index)); e.Handled = true; };
                _candidates.AddView(predicted, new LinearLayout.LayoutParams(-2, Dp(42)));
            }
        if (suggestion != null && _candidates != null)
        {
            var generation = Volatile.Read(ref _generation);
            var button = new Button(this) { Text = suggestion, TextSize = 17,
                ContentDescription = "コピーしたテキストを貼り付け" };
            MobileStyle.KeyboardKey(button);
            button.SetSingleLine(true);
            button.Ellipsize = global::Android.Text.TextUtils.TruncateAt.End;
            button.SetMaxWidth(Dp(240));
            button.SetPadding(Dp(12), 0, Dp(12), 0);
            button.Click += (_, _) =>
            {
                if (generation == Volatile.Read(ref _generation)) PasteClipboard(suggestion);
            };
            _candidates.AddView(button, new LinearLayout.LayoutParams(-2, Dp(42)));
        }
    }
    private void ShowCharacterModes()
    {
        if (_mode == null || _restricted) return;
        var popup = new PopupMenu(this, _mode);
        var commands = new (string Title, int Key)[] { ("ひらがな", 0x75), ("カタカナ", 0x76), ("半角カタカナ", 0x77), ("全角英数字", 0x78), ("半角英数字", 0x79) };
        for (var i = 0; i < commands.Length; i++) popup.Menu!.Add(0, i, i, commands[i].Title);
        popup.MenuItemClick += (_,e) =>
        {
            var key = commands[e.Item!.ItemId].Key;
            Queue(() => _session?.Key(key));
        };
        popup.Show();
    }
    private void ShowExtraSymbols()
    {
        if (_mode == null || _restricted) return;
        var popup = new PopupMenu(this, _mode);
        var symbols = new[] { "*", "=", "(", ")", "[", "]", "{", "}", "\\", "<", ">", "^", "|", "~" };
        for (var i = 0; i < symbols.Length; i++) popup.Menu!.Add(0, i, i, symbols[i]);
        popup.MenuItemClick += (_,e) => Input(symbols[e.Item!.ItemId][0]);
        popup.Show();
    }
    private string? ReadClipboardCandidate()
    {
        if (_restricted || !_adaptiveAllowed) return null;
        var clip = ((global::Android.Content.ClipboardManager?)GetSystemService(ClipboardService))?.PrimaryClip;
        if (clip?.Description?.Extras?.GetBoolean("android.content.extra.IS_SENSITIVE", false) == true) return null;
        if (clip == null) return null;
        for (var i = 0; i < clip.ItemCount; i++)
        {
            var item = clip.GetItemAt(i);
            if (item?.Uri == null && item?.Intent == null && item?.Text is { Length: > 0 and <= 10000 } text) return text;
        }
        return null;
    }

    private void InputSymbol(string symbol)
    {
        if(symbol is "()" or "「」") BeginBrackets(symbol);
        else Input(symbol[0]);
    }
    private void RefreshClipboardSuggestion(bool offer = false)
    {
        if (!offer) _clipboardSuggestion.Dismiss();
        var manager = (global::Android.Content.ClipboardManager?)GetSystemService(ClipboardService);
        var clip = manager?.PrimaryClip;
        if (clip?.Description is not { } description) { _clipboardSuggestion.Dismiss(); return; }
        var timestamp = description.Timestamp;
        var eligible = _clipboardAllowed &&
            description.Extras?.GetBoolean("android.content.extra.IS_SENSITIVE", false) != true;
        string? suggestion = null;
        if (eligible)
        for (var i = 0; i < clip.ItemCount; i++)
        {
            var item = clip.GetItemAt(i);
            if (item?.Uri != null || item?.Intent != null) continue;
            var text = item?.Text;
            if (string.IsNullOrWhiteSpace(text) || text.Length > 10000) continue;
            _clipboardHistory.Add(text);
            suggestion ??= text;
        }
        if (offer)
        {
            _clipboardSuggestion.Open(timestamp, suggestion);
            _clipboardOfferPreferences?.Edit()?.PutLong("last-copy", _clipboardSuggestion.LastTimestamp)?.Apply();
        }
    }
    private void ShowClipboard()
    {
        if (!_clipboardAllowed || _keyRows == null) return;
        CancelTouches();
        RefreshClipboardSuggestion();
        _clipboardOpen = true; _keyRows.RemoveAllViews(); _letters.Clear(); _shift = null;
        var toolbar = new LinearLayout(this) { BaselineAligned = false };
        AddKey(toolbar, "全消去", () => { _clipboardHistory.Clear(); RenderClipboardItems(); }, height: 42, description: "クリップボード履歴を全消去");
        _keyRows.AddView(toolbar);
        RenderClipboardItems();
        Status();
    }
    private void RenderClipboardItems()
    {
        if(_keyRows == null) return;
        while(_keyRows.ChildCount>1) _keyRows.RemoveViewAt(1);
        var scroll = new ScrollView(this); var list = new LinearLayout(this) { Orientation = Orientation.Vertical }; scroll.AddView(list);
        foreach(var text in _clipboardHistory.Items)
        {
            var button = new Button(this) { Text = text, TextSize = 15, ContentDescription = "クリップボード項目" };
            MobileStyle.KeyboardKey(button); button.SetMaxLines(3); button.SetPadding(Dp(12),Dp(4),Dp(12),Dp(4));
            button.Click += (_,_) => PasteClipboard(text); list.AddView(button,new LinearLayout.LayoutParams(-1,-2));
        }
        if(list.ChildCount==0) list.AddView(new TextView(this) { Text = "コピーしたテキストはありません。", TextSize = 15 });
        _keyRows.AddView(scroll,new LinearLayout.LayoutParams(-1,Dp(174)));
    }
    private void PasteClipboard(string text)
    {
        _clipboardSuggestion.Dismiss();
        if (!_clipboardHistory.Items.Contains(text)) { RenderClipboardItems(); return; }
        var generation = Volatile.Read(ref _generation);
        BuildKeyRows(); Status();
        if(!_clipboardAllowed) return;
        Queue(()=>
        {
            _session?.Commit();
            _main!.Post(()=> { if(generation == Volatile.Read(ref _generation)) CurrentInputConnection?.CommitText(text,1); });
        });
    }
    private void BeginBrackets(string pair)
    {
        if (_restricted) { CurrentInputConnection?.CommitText(pair, 1); MoveCursor(Keycode.DpadLeft); return; }
        Queue(() => _session?.BeginBracketConversion(pair == "「」"));
    }
    private void MoveCursor(Keycode code)
    {
        var editor = CurrentInputConnection;
        if (editor == null) return;
        var extracted = editor.GetExtractedText(new ExtractedTextRequest(), (GetTextFlags)0);
        if (extracted?.Text != null && extracted.SelectionStart >= 0 && extracted.SelectionEnd >= 0)
        {
            var target = CursorMovement.Target(extracted.Text.ToString(), extracted.SelectionStart, extracted.SelectionEnd, code == Keycode.DpadLeft) + extracted.StartOffset;
            if (editor.SetSelection(target, target)) return;
        }
        using var down = new global::Android.Views.KeyEvent(KeyEventActions.Down, code);
        using var up = new global::Android.Views.KeyEvent(KeyEventActions.Up, code);
        editor.SendKeyEvent(down); editor.SendKeyEvent(up);
    }
    private void Enter() { if (!SendDefaultEditorAction(true)) CurrentInputConnection?.CommitText("\n", 1); }
    private void HideKeyboard()
    {
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
        if (_restricted || _english)
        {
            _punctuationInput = ','; _punctuationAlternate = '.'; _punctuation.Text = ",";
            if (_punctuationHint != null) _punctuationHint.Text = ".";
            return;
        }
        var text = _shownView?.Text;
        if (string.IsNullOrEmpty(text)) text = CurrentInputConnection?.GetTextBeforeCursor(128, (GetTextFlags)0);
        _punctuationInput = AndroidInputSession.PredictPunctuation(text, false);
        var preferences = _preferences!.Current;
        _punctuation.Text = _punctuationInput == '.' ? preferences.Period : preferences.Comma;
        _punctuationAlternate = _punctuationInput == '.' ? ',' : '.';
        if (_punctuationHint != null) _punctuationHint.Text = _punctuationInput == '.' ? preferences.Comma : preferences.Period;
    }
    private void Status()
    {
        RenderCandidates();
        if (_oneHandButton != null) _oneHandButton.Visibility = _shownView is { Candidates.Count: > 0 } ? ViewStates.Gone : ViewStates.Visible;
        RefreshPunctuation();
        if (_clipboardButton != null)
        {
            _clipboardButton.Text = _clipboardOpen ? "×" : "▤";
            _clipboardButton.ContentDescription = _clipboardOpen ? "クリップボードを閉じる" : "クリップボードを開く";
            _clipboardButton.Enabled = _clipboardAllowed;
            _clipboardButton.Visibility = !_clipboardAllowed || _shownView is { Candidates.Count: > 0 } ? ViewStates.Gone : ViewStates.Visible;
        }
        if (_mode != null)
        {
            var pending = !_restricted && !_english && !_choosingBrackets && !string.IsNullOrEmpty(_shownView?.Text);
            _mode.Text = pending ? (_shownKatakana ? "自動" : "カナ") : _restricted || _english ? "英語" : "自動";
            _mode.ContentDescription = pending ? (_shownKatakana ? "日英自動判別に戻る" : "未確定文字をカタカナにする") : _english ? "日英自動判別に戻る" : "英語専用モードに切り替える";
            _mode.ContentDescription += "、上フリックで文字種を選ぶ";
            _mode.Enabled = _ready && !_restricted;
        }
        foreach (var (key, letter) in _letters) key.Text = (_shiftState.Active ? char.ToUpperInvariant(letter) : letter).ToString();
        if (_shift != null) { _shift.Text = _shiftState.Locked ? "⇪" : "⇧";
            _shift.ContentDescription = _shiftState.Locked ? "大文字固定を解除" : "Shift、2回押すと大文字固定";
            _shift.SetTypeface(null, _shiftState.Active ? TypefaceStyle.Bold : TypefaceStyle.Normal); }
    }
    public override void OnDestroy()
    {
        _worker?.Post(() => _session?.Reset());
        CancelTouches();
        Interlocked.Increment(ref _generation); _zenzEvaluator?.Dispose(); _zenzEvaluator = null; _thread?.QuitSafely(); base.OnDestroy();
    }

    private void CancelTouches() { foreach (var touch in _touches) touch.Cancel(); _predictionPrefix = ""; _pendingLetterTouch = null; }
    private PersonalRomajiTypos? LoadPersonalTypos() =>
        PersonalRomajiTypos.Load(Path.Combine(FilesDir!.AbsolutePath, "personal-romaji-typos.json"));
    private void CaptureLetterTouch(View source, MotionEvent e)
    {
        _pendingLetterTouch = null;
        if (e.ActionMasked != MotionEventActions.Down) return;
        if (!_adaptiveAllowed || _restricted || _symbols || _numberRows != null || _choosingBrackets ||
            _english || _shiftState.Active || _nextKeyPrediction == null || _preferences?.Current.LearningEnabled != true) return;
        var location = new int[2]; source.GetLocationOnScreen(location);
        var x = location[0] + e.GetX(); var y = location[1] + e.GetY();
        var keys = _letters.Select(item =>
        {
            var at = new int[2]; item.Key.GetLocationOnScreen(at);
            return new TouchKey(item.Letter, at[0], at[1], item.Key.Width, item.Key.Height);
        }).ToArray();
        _pendingLetterTouch = AdaptiveKeyTarget.Capture(keys, x, y, c => _nextKeyPrediction.Probability(_predictionPrefix, c));
    }

    private sealed class KeyTouch : Java.Lang.Object, View.IOnTouchListener
    {
        private readonly Handler _handler;
        private readonly Action _action;
        private readonly bool _repeats;
        private readonly Java.Lang.Runnable _repeat;
        private readonly SymbolPress? _symbolPress;
        private readonly Java.Lang.Runnable? _longPress;
        private readonly KeyPressOrder _order;
        private readonly int _slop;
        private View? _view;
        private bool _held;
        private readonly int _flickThreshold;
        private float _startX, _startY;
        private readonly Action<View, MotionEvent>? _capture;
        public KeyTouch(Handler handler, Action action, bool repeats, KeyPressOrder order, int slop, Action? longAction = null,
            Action<View, MotionEvent>? capture = null, int flickThreshold = 0, bool longPressEnabled = true)
        {
            _capture = capture;
            _flickThreshold = flickThreshold;
            _handler = handler; _action = action; _repeats = repeats; _order = order; _slop = slop;
            if(longAction != null) { _symbolPress = new SymbolPress(action,longAction,order,longPressEnabled); _longPress = new Java.Lang.Runnable(()=>_symbolPress.Hold()); }
            _repeat = new Java.Lang.Runnable(() => { if (!_held) return; _action(); _handler.PostDelayed(_repeat!, 60); });
        }
        public bool OnTouch(View? view, MotionEvent? e)
        {
            if (view == null || e == null) return false;
            if (e.ActionMasked == MotionEventActions.Down)
            {
                _order.BeforePress(); Cancel(); _capture?.Invoke(view, e);
            }
            var handled = HandleTouch(view, e);
            if (e.ActionMasked is MotionEventActions.Up or MotionEventActions.Cancel) _capture?.Invoke(view, e);
            return handled;
        }
        private bool HandleTouch(View view, MotionEvent e)
        {
            if (e.ActionMasked == MotionEventActions.Down)
            {
                Cancel(); _view = view; _held = true; view.Pressed = true;
                _startX = e.GetX(); _startY = e.GetY();
                if (_symbolPress != null) { _symbolPress.Down(); _handler.PostDelayed(_longPress!,450); }
                else { _order.BeforePress(); _action(); }
                if (_repeats) _handler.PostDelayed(_repeat, 400);
            }
            else if (e.ActionMasked is MotionEventActions.Move or MotionEventActions.Up)
            {
                var upward = _held && _symbolPress?.Move(e.GetX() - _startX, e.GetY() - _startY, _flickThreshold) == true;
                if (e.ActionMasked == MotionEventActions.Up) { _symbolPress?.Up(); Cancel(); }
                else if (!upward && !KeyTouchBounds.Contains(e.GetX(), e.GetY(), view.Width, view.Height, _slop)) Cancel();
            }
            else if (e.ActionMasked == MotionEventActions.Cancel) Cancel();
            return true;
        }
        public void Cancel() { _symbolPress?.Cancel(); if(_longPress != null) _handler.RemoveCallbacks(_longPress); _held = false; _handler.RemoveCallbacks(_repeat); if (_view != null) _view.Pressed = false; _view = null; }
    }

    private sealed class KeyboardInsets : Java.Lang.Object, View.IOnApplyWindowInsetsListener
    {
        private WindowInsets? _latest;
        private string? _lastMetrics;
        public WindowInsets OnApplyWindowInsets(View? view, WindowInsets? insets)
        {
            if (view == null || insets == null) throw new ArgumentNullException();
            _latest = insets;
            view.Post(() => Apply(view));
            return insets;
        }
        public void Apply(View view)
        {
            var decor = view.RootView;
            var insets = decor?.RootWindowInsets ?? _latest;
            if (decor == null || insets == null || view.Height == 0 || decor.Height == 0) return;
            var bottom = OperatingSystem.IsAndroidVersionAtLeast(30)
                ? insets.GetInsets(WindowInsets.Type.NavigationBars() | WindowInsets.Type.CaptionBar())!.Bottom
                : insets.SystemWindowInsetBottom;
            var windowAt = new int[2]; var inputAt = new int[2];
            decor.GetLocationOnScreen(windowAt); view.GetLocationOnScreen(inputAt);
            var padding = KeyboardInsetLayout.BottomPadding(bottom, windowAt[1] + decor.Height, inputAt[1] + view.Height);
            var metrics = $"inset={bottom} windowBottom={windowAt[1] + decor.Height} inputBottom={inputAt[1] + view.Height} padding={padding}";
            if (_lastMetrics != metrics) { _lastMetrics = metrics; global::Android.Util.Log.Info("MeltypeInsets", metrics); }
            if (view.PaddingBottom != padding)
                view.SetPadding(view.PaddingLeft, view.PaddingTop, view.PaddingRight, padding);
        }
    }
}

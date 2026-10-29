// SPDX-License-Identifier: GPL-3.0-or-later
using Meltype.Composition;
using Meltype.Input;
using Meltype.Config;

namespace Meltype.AndroidCore;

// Calls are serialized by the Android service's worker. Operations target the current editor only.
public sealed class AndroidInputSession : ICompositionHost
{
    private readonly CaptureGate _gate = new(() => { });
    private readonly CompositionController _controller;
    private readonly Action<int, string, CompositionView?> _output;
    private static readonly Lazy<CandidateDictionary> BuiltInCandidates = new(() => CandidateDictionary.Load(null));
    private readonly Func<InputPreferences> _preferences;
    private readonly Func<string?>? _clipboard;
    private bool _clipboardReadAllowed, _clipboardRead;
    private string? _clipboardValue;
    private readonly Func<(string? Before, string? After)>? _surrounding;
    private char? _character;
    private readonly TouchInputHistory _touchInput = new();
    private long _time;
    private bool _english;
    public bool English => _english;
    private readonly PersonalTypoLearning? _typoLearning;
    private readonly Func<bool> _canLearnTypos;
    private readonly Func<string, bool> _registeredReading;
    private CancellationTokenSource _typoLearningCancel = new();
    private string? _editedWrong, _commitRaw;
    public Task PendingTypoLearning { get; private set; } = Task.CompletedTask;

    private static readonly string[] SentenceEndings =
        ["です", "ます", "でした", "ました", "ません", "ない", "なかった", "だった", "である", "だ", "と思う", "と思います"];
    public CompositionView? View { get; private set; }
    public bool Katakana { get; private set; }
    public bool ApplyDakuten(bool handakuten) => _controller.ApplyDakuten(handakuten);
    public bool ToggleSmallLast() => _controller.ToggleSmallLast();
    public bool CycleKanaVariant() => _controller.CycleKanaVariant();

    public void ToggleKana()
    {
        if (_english || ChoosingBrackets || string.IsNullOrEmpty(View?.Text)) return;
        Katakana = !Katakana;
        if (Katakana) Key(0x76);
        else _controller.RestoreAutomaticMode();
    }
    private string[]? _brackets;
    private int _bracketIndex;
    public bool ChoosingBrackets => _brackets != null;
    public void BeginBracketConversion(bool quotes)
    {
        Commit();
        _brackets = quotes ? ["「」", "｢｣", "『』", "【】"] : ["()", "（）", "{}", "｛｝", "[]", "［］"];
        _bracketIndex = quotes ? 0 : _preferences().FullWidthParentheses ? 1 : 0;
        ShowBrackets();
    }
    private void ShowBrackets()
    {
        View = new CompositionView(_brackets![_bracketIndex], _brackets, _bracketIndex, true, "Space 切替　Enter 確定");
        _output(0, View.Text, View);
    }
    private void CommitBracket()
    {
        var pair = _brackets![_bracketIndex];
        _brackets = null; View = null;
        _output(_preferences().CursorIntoBrackets ? 7 : 1, pair, null);
    }

    public AndroidInputSession(IKanjiConverter converter, Func<string, IReadOnlyList<string>> candidates,
        Action<int, string, CompositionView?> output, Func<InputPreferences>? preferences = null, bool learningAllowed = true,
        AndroidUserDictionary? userDictionary = null, PersonalRomajiTypos? personalTypos = null,
        Func<string?>? clipboard = null, Func<(string? Before, string? After)>? surrounding = null, string? stateDirectory = null,
        IContextEvaluator? contextEvaluator = null, bool coherentRomanReview = false)
    {
        _output = output;
        preferences ??= () => new InputPreferences();
        _preferences = preferences; _clipboard = learningAllowed ? clipboard : null; _surrounding = surrounding;
        string? StateFile(string file) => learningAllowed && stateDirectory != null ? System.IO.Path.Combine(stateDirectory, file) : null;
        _registeredReading = raw => (userDictionary?.Lookup(new Meltype.Detection.RomajiDetector().ConvertLenient(raw, true)).Count ?? 0) > 0;
        _canLearnTypos = () => contextEvaluator is not null && learningAllowed && preferences().LearningEnabled && preferences().LocalZenz;
        if (learningAllowed && stateDirectory != null && contextEvaluator != null)
            _typoLearning = new PersonalTypoLearning(StateFile("zenz-personal-typos.json")!, contextEvaluator, _canLearnTypos);
        var languages = new LanguageMemory(StateFile("language-memory.json"));
        var detector = CompositionDetector.CreateDefault();
        if (learningAllowed && preferences().LearningEnabled) detector.Memory = languages;
        var predictor = new Predictor(new PhraseHistory(StateFile("phrase-history.tsv")), userDictionary?.CompositionDictionary, null);
        _controller = new CompositionController(_gate, detector, converter, this,
            new CompositionOptions { ContextEvaluator = contextEvaluator, ContextDebounceMs = 0, ContextOnClauseChange = true, ContextDisplayOnly = true, ContextKanaConversion = true, LearnedRomajiFix = raw => _typoLearning?.Find(raw), ShiftArrowsConvertAutoEnglish = true, FullWidthCommaPeriod = true, Comma = () => preferences().Comma[0],
                TouchRomajiAlternatives = raw => learningAllowed && preferences().LearningEnabled ? _touchInput.Alternatives(raw) : [],
                RuleBasedRomanReview = contextEvaluator is null, CoherentRomanReview = coherentRomanReview,
                Period = () => preferences().Period[0], LearningEnabled = () => learningAllowed && preferences().LearningEnabled, LiveConversion = () => true, LiveConversionMinLength = 2,
                Candidates = BuiltInCandidates.Value,
                Predictor = predictor, Languages = languages,
                Predictions = () => learningAllowed && preferences().LearningEnabled && preferences().Predictions,
                Learning = () => learningAllowed && preferences().LearningEnabled,
                CursorIntoBrackets = () => preferences().CursorIntoBrackets,
                CalculatorCandidates = () => preferences().CalculatorCandidates,
                ConsonantAbbreviations = () => preferences().ConsonantAbbreviations,
                ClipboardCandidates = () => preferences().ClipboardCandidates,
                EnglishNamePredictions = () => preferences().EnglishNamePredictions,
                FullWidthParentheses = () => preferences().FullWidthParentheses,
                SquareBrackets = () => preferences().SquareBrackets,
                FullWidthSlash = () => preferences().FullWidthSlash,
                LaughterAfterKana = () => preferences().LaughterAfterKana,
                EnterCommitsAsShown = () => preferences().EnterCommitsAsShown,
                FunctionKeysAffectSelectedClause = () => preferences().FunctionKeysAffectSelectedClause,
                AlphanumericKeyStartsAsTyped = () => preferences().AlphanumericKeyStartsAsTyped,
                EnglishFirstInput = () => preferences().EnglishFirstInput,
                ContextualLearning = () => preferences().ContextualLearning,
                ShowTypedKeys = () => preferences().ShowTypedKeys,
                UserDictionary = userDictionary?.CompositionDictionary,
                UserDictionaryOverrides = false, LowercaseFinalNCandidates = true,
                PersonalRomajiFix = personalTypos == null ? null : (letters, final) =>
                    learningAllowed && preferences().LearningEnabled &&
                    (userDictionary?.Lookup(new Meltype.Detection.RomajiDetector().ConvertLenient(letters, true)).Count ?? 0) == 0
                        ? personalTypos.Fix(letters, final) : null,
                CorrectTypos = () => learningAllowed && preferences().LearningEnabled,
                MoreCandidates = reading => (userDictionary?.Lookup(reading) ?? []).Concat(candidates(reading)).Distinct().ToArray(), AutoCorrect = () => false });
    }

    public void SetEnglish(bool english)
    {
        if (_english == english) return;
        _touchInput.Clear();
        _editedWrong = null;
        Commit();
        _english = english;
        if (!english) _controller.RestoreAutomaticMode();
    }

    public void Character(char character, LetterTouch? touch = null)
    {
        _touchInput.Append(_controller.RawInput, character, touch);
        if (ChoosingBrackets)
        {
            if (character == ' ') { Key(0x20); return; }
            CommitBracket();
        }
        if (_english) { CommitText(character.ToString()); return; }
        Key(character switch
    {
        ' ' => 0x20, ',' => 0xBC, '.' => 0xBE, '?' or '/' => 0xBF,
        '-' or '_' => 0xBD, '=' or '+' => 0xBB, '[' or '{' => 0xDB, ']' or '}' => 0xDD,
        '\\' or '|' => 0xDC, ';' or ':' => 0xBA, '\'' or '"' => 0xDE, '`' or '~' => 0xC0,
        '(' => 0x39, ')' => 0x30, '*' => 0x38, '!' => 0x31, '@' => 0x32, '#' => 0x33,
        '$' => 0x34, '%' => 0x35, '^' => 0x36, '&' => 0x37, '<' => 0xBC, '>' => 0xBE,
        '\n' => 0x0D, _ => char.ToUpperInvariant(character)
        }, character);
        if (character is ',' or '.' or '!' or '?') _controller.ReviewInputBoundary();
    }

    public void Space(bool fullWidth)
    {
        if (!fullWidth) { Character(' '); return; }
        Commit();
        CommitText("　");
    }

    // Conservative local heuristic, not a grammatical or semantic analysis.
    // Explicit comma/period keys and long-press period remain available.
    public void SentencePunctuation(string? beforeCursor = null) => Character(PunctuationCharacter(beforeCursor));
    public char PunctuationCharacter(string? beforeCursor = null) => PredictPunctuation(View?.Text ?? beforeCursor, _english);
    public static char PredictPunctuation(string? text, bool english)
    {
        var endingText = (text ?? "").TrimEnd('」', '』', '）', ')', ' ', '\t');
        return !english && SentenceEndings.Any(ending => endingText.EndsWith(ending, StringComparison.Ordinal)) ? '.' : ',';
    }

    public void Key(int code, char? character = null, bool shift = false)
    {
        if (code is not (0x0D or 0x20) && character is not (>= 'a' and <= 'z') && character is not (',' or '.' or '!' or '?')) _touchInput.Clear();
        _commitRaw = _controller.RawInput;
        if (code == 0x08 && !_english && View is { Converting: false } && _canLearnTypos())
        {
            if (_commitRaw.Length is >= 4 and <= 32 && _commitRaw.All(char.IsAsciiLetterLower)) _editedWrong ??= _commitRaw;
        }
        else if (code is not (0x0D or 0x20) && character is not (>= 'a' and <= 'z'))
            _editedWrong = null;

        if (!_english && !ChoosingBrackets && (code == 0x20 || code == 0x0D && !_preferences().EnterCommitsAsShown) &&
            _controller.TryCommitRomanTypo(code == 0x20 ? " " : "")) return;
        if (!_english && code == 0x20 && !_controller.HasDirectReadingCandidates && _controller.CommitVisibleEnglish(" ")) return;
        if (!_english && code == 0x0D && _preferences().EnterCommitsAsShown && _controller.CommitVisibleEnglish()) return;
        if (ChoosingBrackets)
        {
            if (code is 0x08 or 0x1B) { _brackets = null; Hide(); return; }
            if (code == 0x0D) { CommitBracket(); return; }
            if (code is 0x20 or 0x25 or 0x27)
            { _bracketIndex = (_bracketIndex + (code == 0x25 ? _brackets!.Length - 1 : 1)) % _brackets!.Length; ShowBrackets(); return; }
            CommitBracket();
        }
        if (code is 0x25 or 0x27 && string.IsNullOrEmpty(View?.Text))
        {
            _output(code == 0x25 ? 5 : 6, "", null); return;
        }
        if (!_english && code == 0x0D && View is { Converting: true })
        {
            _controller.CommitSelectedPrefix(); return;
        }
        // The live candidate strip already selects a clause. Preserve that position
        // when entering explicit conversion instead of restarting at an edge.
        if (!_english && code is 0x25 or 0x27 &&
            View is { Converting: false, Clauses: { Count: > 0 } clauses, SelectedClause: >= 0 } live)
        {
            var target = shift ? live.SelectedClause :
                Math.Clamp(live.SelectedClause + (code == 0x25 ? -1 : 1), 0, clauses.Count - 1);
            if (_controller.SelectClauseAt(clauses.Take(target).Sum(c => c.Length)) && !shift) return;
        }
        _character = character;
        if (_english) { Replay(new KeyEvent(code, 0, false, false, false, NextTime())); _character = null; return; }
        // Scope the modifier to this arrow so it cannot affect later characters.
        var resize = shift && code is 0x25 or 0x27;
        if (resize) PumpKey(0x10, false);
        _clipboardReadAllowed = code is 0x20 or 0x1C;
        _clipboardRead = false; _clipboardValue = null;
        try
        {
            PumpKey(code, false);
            PumpKey(code, true);
        }
        finally { if (resize) PumpKey(0x10, true); _clipboardReadAllowed = false; _clipboardValue = null; }
        _character = null;
    }

    private long NextTime() => _time = Math.Max(_time + 1, Environment.TickCount64);
    public void PollContextEvaluation(long now) => _controller.PollContextEvaluation(now);
    public bool ContextEvaluationPending => _controller.ContextEvaluationPending;

    private void PumpKey(int code, bool up)
    {
        _gate.OnKey(new KeyEvent(code, 0, false, up, false, NextTime()), e => e.IsDown);
        _controller.Pump();
    }

    public void SpaceKey(bool shift)
    {
        var p = _preferences();
        if (p.FullWidthSpaceByDefault ? !shift : shift && !p.ShiftSpaceHalfWidth) Space(true);
        else if (shift) { Commit(); CommitText(" "); }
        else Character(' ');
    }
    public void SelectPrediction(int index) { _touchInput.Clear(); _controller.CommitPredictionAt(index); }
    public void ForgetPrediction(int index) { _touchInput.Clear(); _controller.ForgetPredictionAt(index); }
    public string? ClipboardText
    {
        get
        {
            if (!_clipboardReadAllowed) return null;
            if (!_clipboardRead) { _clipboardValue = _clipboard?.Invoke(); _clipboardRead = true; }
            return _clipboardValue;
        }
    }

    public void SelectPosition(int offset)
    {
        _touchInput.Clear();
        if (!_english && !ChoosingBrackets) _controller.SelectClauseAt(offset);
    }

    public void Select(int index)
    {
        _touchInput.Clear();
        _commitRaw = _controller.RawInput;
        if (ChoosingBrackets)
        { if (index >= 0 && index < _brackets!.Length) { _bracketIndex = index; CommitBracket(); } return; }
        if (View is not { } view || index < 0 || index >= view.Candidates.Count) return;
        if (view.Converting) _controller.SelectCandidate(index);
        else if (!_controller.SelectLiveCandidate(view.SelectedClause, view.Candidates[index])) return;
        _controller.CommitSelectedPrefix();
    }
    public void Commit() { _commitRaw = _controller.RawInput; if (ChoosingBrackets) CommitBracket(); else if (!_controller.CommitVisibleEnglish()) _controller.CommitPending(); }
    public void Reset() { _touchInput.Clear(); _typoLearningCancel.Cancel(); _typoLearningCancel.Dispose(); _typoLearningCancel = new(); _editedWrong = _commitRaw = null; _controller.Reset(); _controller.ResetContext(); _gate.Abort(); _brackets = null; Katakana = false; View = null; }
    public void CommitText(string text)
    {
        _touchInput.Clear();
        if (_editedWrong is { } wrong && _commitRaw is { } raw && _canLearnTypos() && _typoLearning != null &&
            !_english && !_registeredReading(wrong) && !_registeredReading(raw) && text.Any(c => c is >= 'ぁ' and <= 'ゖ' or >= 'ァ' and <= 'ヺ' or >= '\u3400' and <= '\u9FFF'))
        {
            var context = _surrounding?.Invoke();
            var next = _typoLearning.ObserveAsync(wrong, raw, context?.Before ?? "", context?.After ?? "", _typoLearningCancel.Token);
            PendingTypoLearning = PendingTypoLearning.IsCompleted ? next : Task.WhenAll(PendingTypoLearning, next);
        }
        _editedWrong = null; _commitRaw = null;
        _output(1, text, null);
    }
    /// <summary>未確定の入力を確定してから、そのままの文字列を入れる (GODAN の「」など)。</summary>
    public void InsertLiteral(string text) { Commit(); CommitText(text); }
    public void Show(CompositionView view)
    {
        View = view.Converting || !_preferences().ShowCandidatesWhileTyping ? view : _controller.LiveCandidateView(view);
        _output(0, View.Text, View);
    }
    public void Hide() { Katakana = false; View = null; _output(2, "", null); }
    public void DeleteBackward(int count) => _output(3, count.ToString(), null);
    public void Replay(KeyEvent e)
    {
        if (e.IsUp) return;
        if (e.Vk == 0x08) DeleteBackward(1);
        else if (e.Vk == 0x0D) _output(4, "", null);
        else if (e.Vk is 0x25 or 0x27) _output(e.Vk == 0x25 ? 5 : 6, "", null);
        else if (_character is { } c) CommitText(c.ToString());
    }
    public void Replay(MouseButtonEvent e) { }
    public char? CharFromKey(KeyEvent e, bool shift) => _character;
    public void RequestSurroundingText(Action<string?, string?> callback)
    {
        var text = _surrounding?.Invoke(); callback(text?.Before, text?.After);
    }
}

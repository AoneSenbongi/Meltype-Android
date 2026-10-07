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
    private char? _character;
    private long _time;
    private bool _english;
    private static readonly string[] SentenceEndings =
        ["です", "ます", "でした", "ました", "ません", "ない", "なかった", "だった", "である", "だ", "と思う", "と思います"];
    public CompositionView? View { get; private set; }
    public bool Katakana { get; private set; }
    public void ToggleKana()
    {
        if (_english || string.IsNullOrEmpty(View?.Text)) return;
        Katakana = !Katakana;
        Key(Katakana ? 0x76 : 0x75);
    }
    public void InsertBracketPair(string pair)
    {
        _controller.CommitPending();
        _output(7, pair, null);
    }

    public AndroidInputSession(IKanjiConverter converter, Func<string, IReadOnlyList<string>> candidates,
        Action<int, string, CompositionView?> output, Func<InputPreferences>? preferences = null, bool learningAllowed = true,
        AndroidUserDictionary? userDictionary = null)
    {
        _output = output;
        preferences ??= () => new InputPreferences();
        _controller = new CompositionController(_gate, CompositionDetector.CreateDefault(), converter, this,
            new CompositionOptions { FullWidthCommaPeriod = true, Comma = () => preferences().Comma[0],
                Period = () => preferences().Period[0], LearningEnabled = () => learningAllowed && preferences().LearningEnabled, LiveConversion = () => true,
                UserDictionary = userDictionary?.CompositionDictionary,
                MoreCandidates = reading => (userDictionary?.Lookup(reading) ?? []).Concat(candidates(reading)).Distinct().ToArray(), AutoCorrect = () => false });
    }

    public void SetEnglish(bool english)
    {
        if (_english == english) return;
        _controller.CommitPending();
        _english = english;
    }

    public void Character(char character)
    {
        if (_english) { CommitText(character.ToString()); return; }
        Key(character switch
    {
        ' ' => 0x20, ',' => 0xBC, '.' => 0xBE, '?' or '/' => 0xBF,
        '-' => 0xBD, '\n' => 0x0D, _ => char.ToUpperInvariant(character)
        }, character);
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

    public void Key(int code, char? character = null)
    {
        if (code is 0x25 or 0x27 && string.IsNullOrEmpty(View?.Text))
        {
            _output(code == 0x25 ? 5 : 6, "", null); return;
        }
        if (!_english && code == 0x0D && View is { Converting: true })
        {
            _controller.CommitSelectedPrefix(); return;
        }
        _character = character;
        if (_english) { Replay(new KeyEvent(code, 0, false, false, false, ++_time)); _character = null; return; }
        foreach (var up in new[] { false, true })
        {
            _gate.OnKey(new KeyEvent(code, 0, false, up, false, ++_time), e => e.IsDown);
            _controller.Pump();
        }
        _character = null;
    }

    public void Select(int index)
    {
        if (View is not { } view || index < 0 || index >= view.Candidates.Count) return;
        if (view.Converting) _controller.SelectCandidate(index);
        else if (!_controller.SelectLiveCandidate(view.SelectedClause, view.Candidates[index])) return;
        _controller.CommitSelectedPrefix();
    }
    public void Commit() => _controller.CommitPending();
    public void Reset() { _controller.Reset(); _controller.ResetContext(); _gate.Abort(); Katakana = false; View = null; }
    public void CommitText(string text) => _output(1, text, null);
    public void Show(CompositionView view)
    {
        View = view.Converting ? view : _controller.LiveCandidateView(view);
        _output(0, View.Text, View);
    }
    public void Hide() { Katakana = false; View = null; _output(2, "", null); }
    public void DeleteBackward(int count) => _output(3, count.ToString(), null);
    public void Replay(KeyEvent e)
    {
        if (e.IsUp) return;
        if (e.Vk == 0x08) DeleteBackward(1);
        else if (e.Vk == 0x0D) _output(4, "", null);
        else if (_character is { } c) CommitText(c.ToString());
    }
    public void Replay(MouseButtonEvent e) { }
    public char? CharFromKey(KeyEvent e, bool shift) => _character;
    public void RequestSurroundingText(Action<string?, string?> callback) => callback(null, null);
}

// SPDX-License-Identifier: GPL-3.0-or-later
using Meltype.Input;

namespace Meltype.Composition;

public sealed partial class CompositionController
{
    private ContextEvaluation? _romanEvaluation;
    private RomajiTypoCorrector? _romanCorrector;
    private string? _romanKey, _romanRaw, _romanBaseline, _romanResult, _romanCommitSuffix, _romanSurrounding;
    private ContextRequest? _romanRequest;
    private IReadOnlyList<string> _romanCandidates = [];
    private long _romanDue;
    private string? _modelBoundaryPrefix, _modelBoundarySuffix;
    public bool ContextEvaluationPending => _romanEvaluation?.Pending == true || _romanRequest is not null;
    private bool RomanReviewCurrent => _romanResult is not null && _romanRaw == _text.Raw &&
        !_text.IsEmpty && !_converting && !_modeByKey && _predictionIndex < 0 && _text.Mode == DisplayMode.Auto &&
        _romanSurrounding == string.Join("\u0001", _precedingText, _followingText) &&
        _romanBaseline == _text.Display(false, _options.LiveConversion() ? LiveConvert : null);

    private void ResetRomanEvaluation()
    {
        _romanEvaluation?.Reset(); _romanRequest = null;
        _romanCandidates = [];
        _romanKey = _romanRaw = _romanBaseline = _romanResult = _romanCommitSuffix = _romanSurrounding = null;
        _modelBoundaryPrefix = _modelBoundarySuffix = null;
    }

    private bool RequestRomanEvaluation(string baseline)
    {
        var raw = _text.Raw;
        if (_romanEvaluation is null || _romanCorrector is null || !_options.LiveConversion() ||
            _modeByKey || _converting || _predictionIndex >= 0 || _text.Mode != DisplayMode.Auto ||
            _text.KanaInput || IsProtectedInput || raw.Length is < 4 or > 64 ||
            _options.KnownEnglishForTypoReview?.Invoke(raw) == true || _options.Languages?.IsExplicitEnglish(raw) == true ||
            (_options.UserDictionary?.Lookup(_detector.Romaji.ConvertLenient(raw, true)).Count ?? 0) > 0)
        { ResetRomanEvaluation(); return false; }
        var key = string.Join("\u0001", raw, baseline, _precedingText, _followingText);
        if (_romanKey == key) return true;
        ResetRomanEvaluation();
        if (ModelNumberBoundary.Find(_text, baseline, _precedingText ?? "", _followingText ?? "") is { } boundary)
        {
            _romanKey = key; _romanRaw = raw; _romanBaseline = baseline;
            _romanSurrounding = string.Join("\u0001", _precedingText, _followingText);
            _modelBoundaryPrefix = boundary.Prefix; _modelBoundarySuffix = boundary.Suffix;
            _romanRequest = boundary.Request;
            _romanDue = Environment.TickCount64 + 180;
            return true;
        }
        if (!raw.All(char.IsAsciiLetterLower) || !baseline.Any(char.IsAsciiLetterLower) || _detector.IsLexicalEnglishWord(raw)) return false;
        if (_romanCorrector.Fix(raw, final: true) is not { } fix || _detector.ContainsLexicalEnglishWord(raw)) return false;
        var request = new ContextRequest(fix.Kana, _precedingText ?? "", _followingText ?? "", [baseline, fix.Kana], RomanTypo: true);
        var candidates = request.Candidates.Where(c => ZenzCandidateRules.AllowsCandidate(request, c)).Distinct(StringComparer.Ordinal).ToArray();
        if (candidates.Length < 2) return false;
        _romanKey = key; _romanRaw = raw; _romanBaseline = baseline;
        _romanSurrounding = string.Join("\u0001", _precedingText, _followingText);
        _romanRequest = request with { Candidates = candidates };
        _romanDue = Environment.TickCount64 + 180;
        return true;
    }

    public void PollContextEvaluation(long nowMs)
    {
        if (_romanEvaluation is null || _romanRaw is null) return;
        if (_text.Raw != _romanRaw || _text.IsEmpty || _converting || _modeByKey || _predictionIndex >= 0 ||
            _romanSurrounding != string.Join("\u0001", _precedingText, _followingText) || _text.Mode != DisplayMode.Auto)
        { ResetRomanEvaluation(); return; }
        if (_romanRequest is { } request)
        {
            if (nowMs < _romanDue) return;
            _romanRequest = null;
            try
            {
                if (request.ModelBoundary is not null)
                {
                    _romanCandidates = request.Candidates.Select(c => _modelBoundaryPrefix + c + _modelBoundarySuffix).ToArray();
                    _romanEvaluation.Update(request, nowMs - 180);
                }
                else
                {
                    var clauses = _converter.ConvertClauses(request.Reading, request.Left);
                    var converted = clauses is { Count: > 0 } ? string.Concat(clauses.Select(c => c.Text)) : _converter.Convert(request.Reading);
                    var candidates = new[] { request.Candidates[0], converted, request.Reading }
                        .Where(c => c is not null && ZenzCandidateRules.AllowsCandidate(request, c)).Cast<string>()
                        .Distinct(StringComparer.Ordinal).Take(5).ToArray();
                    _romanCandidates = candidates;
                    _romanEvaluation.Update(request with { Candidates = candidates }, nowMs - 180);
                }
            }
            catch (Exception e) { Diagnostics.Log.Warn("誤字候補を取得できませんでした: " + e.GetType().Name); }
        }
        if (_romanEvaluation.Poll(nowMs) is { } result)
        {
            // Boundary inference only adds a suggestion; normal typing and Enter retain the baseline.
            _romanResult = _modelBoundaryPrefix is not null ?
                (_modelBoundaryPrefix + result + _modelBoundarySuffix == _romanBaseline ? null : _romanBaseline) : result;
            UpdateView();
        }
        if (_romanCommitSuffix is not null && !_romanEvaluation.Pending) FinishRomanCommit(useResult: true);
    }

    public bool TryCommitRomanTypo(string suffix)
    {
        if (!RequestRomanEvaluation(_text.Display(false, _options.LiveConversion() ? LiveConvert : null))) return false;
        if (_modelBoundaryPrefix is not null) return false;
        _romanCommitSuffix = suffix;
        PollContextEvaluation(Environment.TickCount64 + 180);
        return true;
    }

    private void FinishRomanCommit(bool useResult)
    {
        var suffix = _romanCommitSuffix ?? "";
        var text = useResult && RomanReviewCurrent ? _romanResult! : _romanBaseline ?? CurrentDisplay(false);
        ResetRomanEvaluation();
        // Automatic inference is not an explicit language choice or correction example.
        CommitText(text + suffix, english: !IsJapaneseText(text), chosen: true, preserveText: true);
        UpdateView();
    }

    private void CommitRomanShown(string suffix)
    {
        _romanCommitSuffix = suffix;
        FinishRomanCommit(useResult: true);
    }

    private bool SelectRomanCandidate(string text)
    {
        if (!RomanReviewCurrent || !_romanCandidates.Contains(text, StringComparer.Ordinal)) return false;
        ResetRomanEvaluation();
        CommitText(text, english: !IsJapaneseText(text), chosen: true, preserveText: true);
        UpdateView();
        return true;
    }

    private bool HandlePendingRomanCommit(KeyEvent e)
    {
        if (e.IsUp || _romanCommitSuffix is null || VirtualKeys.IsModifier(e.Vk)) return false;
        if (e.Vk is VirtualKeys.Back or VirtualKeys.Escape) _romanCommitSuffix = null;
        else if (e.Vk is VirtualKeys.Return or VirtualKeys.Space) return true;
        else FinishRomanCommit(useResult: false);
        return false;
    }
}

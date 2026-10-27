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
    private readonly Dictionary<string, string?> _romanReadings = new(StringComparer.Ordinal);
    private long _romanDue;
    private bool _romanUsesLearned, _romanNeedsConversion, _romanKanaPhase;
    private string? _romanCorrectedReading, _romanCorrectedRaw;
    private readonly Dictionary<string, string> _romanRepairs = new(StringComparer.Ordinal);
    private long _romanDeadline;
    private string? _modelBoundaryPrefix, _modelBoundarySuffix;
    public bool ContextEvaluationPending => _romanEvaluation?.Pending == true || _romanRequest is not null;
    private bool _romanPartial, _romanFinal;
    private string? _candidateCommitText;
    private string RomanTail => _romanPartial && _romanBaseline is { } prefix &&
        _text.Display(false, _options.LiveConversion() ? LiveConvert : null) is { } display && display.StartsWith(prefix, StringComparison.Ordinal)
            ? display[prefix.Length..] : "";
    private string RomanDisplay => _romanResult! + RomanTail;
    private IReadOnlyList<string> RomanCandidateTexts => _romanPartial ? _romanCandidates.Select(c=>c+RomanTail).ToArray() : _romanCandidates;
    private bool RomanAnchorCurrent => _romanRaw is not null && _romanBaseline is not null &&
        (_romanPartial ? _text.Raw.StartsWith(_romanRaw,StringComparison.Ordinal) &&
            _text.Display(false,_options.LiveConversion()?LiveConvert:null).StartsWith(_romanBaseline,StringComparison.Ordinal)
            : _romanRaw==_text.Raw && _romanBaseline==_text.Display(false,_options.LiveConversion()?LiveConvert:null));
    private bool RomanReviewCurrent => _romanResult is not null && RomanAnchorCurrent &&
        !_text.IsEmpty && !_converting && !_modeByKey && _predictionIndex < 0 && _text.Mode == DisplayMode.Auto &&
        _romanSurrounding == string.Join("\u0001", _precedingText, _followingText);

    private void ResetRomanEvaluation()
    {
        _romanEvaluation?.Reset(); _romanRequest = null; _romanUsesLearned = false;
        _romanNeedsConversion=false; _romanKanaPhase=false; _romanCorrectedReading=null; _romanCorrectedRaw=null; _romanRepairs.Clear(); _romanDeadline=0;
        _romanCandidates = [];
        _romanReadings.Clear();
        _romanKey = _romanRaw = _romanBaseline = _romanResult = _romanCommitSuffix = _romanSurrounding = null;
        _modelBoundaryPrefix = _modelBoundarySuffix = null; _romanPartial = false; _romanFinal = false;
    }

    private bool RequestRomanEvaluation(string baseline, bool final = false, bool keepTail = false, string? rawOverride = null)
    {
        var raw = rawOverride ?? _text.Raw;
        if ((_romanEvaluation is null && !_options.RuleBasedRomanReview) || _romanCorrector is null || !_options.LiveConversion() ||
            (_options.RuleBasedRomanReview && !_options.CorrectTypos()) ||
            _modeByKey || _converting || _predictionIndex >= 0 || _text.Mode != DisplayMode.Auto ||
            _text.KanaInput || IsProtectedInput || raw.Length < 4 ||
            _options.KnownEnglishForTypoReview?.Invoke(raw) == true || _options.Languages?.IsExplicitEnglish(raw) == true ||
            (_options.UserDictionary?.Lookup(_detector.Romaji.ConvertLenient(raw, true)).Count ?? 0) > 0)
        { ResetRomanEvaluation(); return false; }
        // A typo may itself split a word into bogus clauses. In coherent-review mode,
        // prefer a unique whole-reading repair before choosing a prefix from those clauses.
        RomajiFix? coherentFix = null;
        if (_options.CoherentRomanReview && !_options.RuleBasedRomanReview &&
            _text.Pending.Length == 0 && raw.Length <= 64 && raw.All(char.IsAsciiLetterLower))
        {
            if (baseline.Any(char.IsAsciiLetterLower) && !_detector.ContainsLexicalEnglishWord(raw))
                coherentFix = _romanCorrector.Fix(raw, true);
            var coherentTouch = _romanCorrector.TouchSuggestions(raw, _options.TouchRomajiAlternatives?.Invoke(raw) ?? []);
            if (coherentFix is null && coherentTouch.Count == 1) coherentFix = coherentTouch[0];
        }
        var wholeEligible=(final || coherentFix is not null) && raw!=_romanRaw && raw.Length<=64 && raw.All(char.IsAsciiLetterLower);
        var finalLearned=wholeEligible?_options.LearnedRomajiFix?.Invoke(raw):null;
        var finalTouch = wholeEligible && (_options.RuleBasedRomanReview || _options.CoherentRomanReview) ?
            _romanCorrector.TouchSuggestions(raw, _options.TouchRomajiAlternatives?.Invoke(raw) ?? []) : [];
        var recheckWhole=wholeEligible && (coherentFix is not null || finalLearned is not null ||
            finalTouch.Count == 1 ||
            baseline.Any(char.IsAsciiLetterLower) && !_detector.ContainsLexicalEnglishWord(raw) && _romanCorrector.Fix(raw,true) is not null);
        var fallback=recheckWhole && RomanReviewCurrent?RomanDisplay:null;
        if (!recheckWhole && (_romanPartial || _romanFinal) && RomanAnchorCurrent &&
            _romanSurrounding==string.Join("\u0001",_precedingText,_followingText) &&
            (_romanRequest is not null || _romanEvaluation?.Pending == true || _romanCommitSuffix is not null || _romanResult is not null && _romanResult!=_romanBaseline)) return true;
        var fullRaw = raw;
        var right = _followingText ?? "";
        if (_options.ContextOnClauseChange && !final && coherentFix is null && (_options.ContextDisplayOnly || ModelNumberBoundary.Find(_text,baseline,_precedingText??"",right) is null))
        {
            if(_text.Pending.Length>0){ResetRomanEvaluation();return false;}
            var clauses = BuildConversionClauses();
            if (clauses.Count < 2 || clauses[^1].Reading.Length < 2 ||
                (clauses[^1].IsEnglish ? !_detector.IsLexicalEnglishWord(clauses[^1].Reading) : !_romanCorrector.IsKnownReading(clauses[^1].Reading)))
            { ResetRomanEvaluation(); return false; }
            var closed = clauses.Take(Math.Max(0,clauses.Count-1)).ToArray();
            if (closed.Length == 0 || closed.Any(c=>c.Raw is null && !c.IsEnglish)) { ResetRomanEvaluation(); return false; }
            raw = string.Concat(closed.Select(c=>c.Raw ?? c.Reading));
            var prefix = string.Concat(closed.Select(c=>c.Text));
            if (raw.Length < 4 || !fullRaw.StartsWith(raw,StringComparison.Ordinal) || !baseline.StartsWith(prefix,StringComparison.Ordinal))
            { ResetRomanEvaluation(); return false; }
            right = baseline[prefix.Length..] + right;
            baseline = prefix;
        }
        if (raw.Length > 64 || _options.KnownEnglishForTypoReview?.Invoke(raw)==true ||
            _options.Languages?.IsExplicitEnglish(raw)==true ||
            (_options.UserDictionary?.Lookup(_detector.Romaji.ConvertLenient(raw,true)).Count??0)>0)
        { ResetRomanEvaluation(); return false; }
        var key = string.Join("\u0001", raw, baseline, _precedingText, _followingText);
        if (_romanKey == key && (!_romanUsesLearned || _options.LearnedRomajiFix?.Invoke(raw) is not null)) return true;
        ResetRomanEvaluation();
        if (!_options.RuleBasedRomanReview && !_options.ContextDisplayOnly && raw == fullRaw && ModelNumberBoundary.Find(_text, baseline, _precedingText ?? "", _followingText ?? "") is { } boundary)
        {
            _romanKey = key; _romanRaw = raw; _romanBaseline = baseline;
            _romanSurrounding = string.Join("\u0001", _precedingText, _followingText);
            _modelBoundaryPrefix = boundary.Prefix; _modelBoundarySuffix = boundary.Suffix;
            _romanRequest = boundary.Request;
            _romanDue = Environment.TickCount64 + _options.ContextDebounceMs;
            return true;
        }
        var learned = _options.LearnedRomajiFix?.Invoke(raw);
        if (!raw.All(char.IsAsciiLetterLower) || _detector.IsLexicalEnglishWord(raw)) return false;
        var fix = learned ?? coherentFix ?? _romanCorrector.Fix(raw,final:true);
        var touches = _options.TouchRomajiAlternatives?.Invoke(raw) ?? [];
        var touchFixes = learned is null ? _romanCorrector.TouchSuggestions(raw, touches) : [];
        var fixes = new List<RomajiFix>();
        if (learned is { } personal) fixes.Add(personal);
        fixes.AddRange(touchFixes);
        // The Android integration opts into multiple proposals. Other hosts keep their existing rule.
        if (learned is null && baseline.Any(char.IsAsciiLetterLower) && !_detector.ContainsLexicalEnglishWord(raw))
            fixes.AddRange(!_options.RuleBasedRomanReview && _options.TouchRomajiAlternatives is not null ? _romanCorrector.Suggestions(raw, true) : fix is { } legacy ? [legacy] : []);
        fixes = fixes.DistinctBy(f => f.Kana).Take(3).ToList();
        if (_options.RuleBasedRomanReview)
        {
            // No model: a reading must be unique and supported by the original conservative rule or touch evidence.
            if (fixes.Count != 1 || learned is null && fix?.Kana != fixes[0].Kana && !touchFixes.Any(f => f.Kana == fixes[0].Kana)) return false;
            string? converted;
            try
            {
                var clauses = _converter.ConvertClauses(fixes[0].Kana, _precedingText);
                converted = clauses is { Count: > 0 } ? string.Concat(clauses.Select(c => c.Text)) : _converter.Convert(fixes[0].Kana);
            }
            catch (Exception e) { Diagnostics.Log.Warn("誤字の変換を採用できませんでした: " + e.GetType().Name); return false; }
            if (converted is not { Length: > 0 and <= 64 } || converted == baseline ||
                !ZenzCandidateRules.PreservesLiteralText(fixes[0].Kana, converted)) return false;
            _romanKey = key; _romanRaw = raw; _romanBaseline = baseline; _romanResult = converted;
            _romanPartial = keepTail || raw != fullRaw; _romanFinal = final;
            _romanSurrounding = string.Join("\u0001", _precedingText, _followingText);
            return true;
        }
        ContextRequest request;
        if(fixes.Count == 0)
        {
            var reading=_detector.Romaji.ConvertLenient(raw,true);
            if(!_options.ContextKanaConversion || baseline.Any(char.IsAsciiLetterLower) ||
                !reading.All(c=>c is >= 'ぁ' and <= 'ゖ' or 'ー') || !_romanCorrector.IsKnownReading(reading))return false;
            request=new(reading,_precedingText??"",right,[baseline],KanaConversion:true);
        }
        else
        {
            _romanUsesLearned=learned is not null;
            request=new(fixes[0].Kana,_precedingText??"",right,
                new[] { baseline }.Concat(fixes.Select(f => f.Kana)).Distinct(StringComparer.Ordinal).ToArray(),RomanTypo:baseline.Any(char.IsAsciiLetterLower));
            _romanCorrectedReading=fixes[0].Kana;
            foreach (var proposal in fixes) { _romanReadings[proposal.Kana] = proposal.Kana; _romanRepairs[proposal.Kana] = proposal.Right; }
            _romanNeedsConversion=_options.ContextKanaConversion;
        }
        var candidates=request.Candidates.Where(c=>ZenzCandidateRules.AllowsCandidate(request,c)).Distinct(StringComparer.Ordinal).ToArray();
        if(candidates.Length<(request.KanaConversion?1:2))return false;
        _romanKey = key; _romanRaw = raw; _romanBaseline = baseline;
        _romanPartial = keepTail || raw != fullRaw || coherentFix is not null; _romanFinal = final; _romanResult=fallback;
        _romanSurrounding = string.Join("\u0001", _precedingText, _followingText);
        _romanRequest = request with { Candidates = candidates };
        _romanDue = Environment.TickCount64 + _options.ContextDebounceMs;
        return true;
    }

    public void PollContextEvaluation(long nowMs)
    {
        if (_romanEvaluation is null || _romanRaw is null) return;
        if(_romanDeadline!=0 && nowMs>=_romanDeadline)
        {
            _romanEvaluation.Reset();_romanRequest=null;
            if(_romanCommitSuffix is not null)FinishRomanCommit(useResult:true);
            return;
        }
        if (_romanUsesLearned && _options.LearnedRomajiFix?.Invoke(_romanRaw) is null)
        { ResetRomanEvaluation(); UpdateView(); return; }
        if (!RomanAnchorCurrent || _text.IsEmpty || _converting || _modeByKey || _predictionIndex >= 0 ||
            _romanSurrounding != string.Join("\u0001", _precedingText, _followingText) || _text.Mode != DisplayMode.Auto)
        { ResetRomanEvaluation(); return; }
        if (_romanRequest is { } request)
        {
            if (nowMs < _romanDue) return;
            _romanRequest = null;
            try
            {
                if(_romanDeadline==0)_romanDeadline=nowMs+1500;
                if(request.KanaConversion)
                {
                    _romanKanaPhase=true;
                    _romanEvaluation.Update(request,nowMs-_options.ContextDebounceMs);
                }
                else if (request.ModelBoundary is not null)
                {
                    _romanCandidates = request.Candidates.Select(c => _modelBoundaryPrefix + c + _modelBoundarySuffix).ToArray();
                    _romanEvaluation.Update(request, nowMs - _options.ContextDebounceMs);
                }
                else
                {
                    var proposals = new List<string> { request.Candidates[0] };
                    foreach (var reading in request.Candidates.Skip(1))
                    {
                        var clauses = _converter.ConvertClauses(reading, request.Left);
                        var converted = clauses is { Count: > 0 } ? string.Concat(clauses.Select(c => c.Text)) : _converter.Convert(reading);
                        foreach (var candidate in new[] { converted, reading })
                        {
                            if (candidate is null || candidate == request.Candidates[0] || !ZenzCandidateRules.AllowsCandidate(request, candidate)) continue;
                            if (_romanReadings.TryGetValue(candidate, out var existing) && existing != reading) _romanReadings[candidate] = null;
                            else _romanReadings[candidate] = reading;
                            if (!proposals.Contains(candidate, StringComparer.Ordinal) && proposals.Count < 5) proposals.Add(candidate);
                        }
                    }
                    var candidates = proposals.ToArray();
                    _romanCandidates = candidates;
                    _romanEvaluation.Update(request with { Candidates = candidates }, nowMs - _options.ContextDebounceMs);
                }
            }
            catch (Exception e) { Diagnostics.Log.Warn("誤字候補を取得できませんでした: " + e.GetType().Name); }
        }
        if (_romanEvaluation.Poll(nowMs) is { } result)
        {
            if(_romanKanaPhase)
            {
                var reading=_romanCorrectedReading ?? _detector.Romaji.ConvertLenient(_romanRaw,true);
                if(CanAlignNeuralResult(reading,result))_romanResult=result;
            }
            else
            {
                if (_romanReadings.TryGetValue(result, out var selectedReading)) _romanCorrectedReading = selectedReading;
                if (_romanCorrectedReading is { } accepted && _romanRepairs.TryGetValue(accepted, out var repairedRaw)) _romanCorrectedRaw = repairedRaw;
                _romanResult=_modelBoundaryPrefix is not null ?
                    (_modelBoundaryPrefix+result+_modelBoundarySuffix==_romanBaseline?null:_romanBaseline):
                    result==_romanBaseline?_romanResult??result:result;
                if(_romanNeedsConversion && result!=_romanBaseline && _romanCorrectedReading is { } reading)
                {
                    _romanNeedsConversion=false;
                    _romanRequest=new(reading,_precedingText??"",RomanTail+(_followingText??""),[result],KanaConversion:true);
                    _romanDue=nowMs;_romanKanaPhase=true;
                    UpdateView();PollContextEvaluation(nowMs);return;
                }
            }
            UpdateView();
        }
        if (_romanCommitSuffix is not null && _romanRequest is null && !_romanEvaluation.Pending) FinishRomanCommit(useResult: true);
    }

    private bool CanAlignNeuralResult(string reading,string result)
    {
        var clauses=_converter.ConvertClauses(reading,_precedingText);
        if(clauses is not {Count:>0})return false;
        var lists=clauses.Select(c=>JapaneseCandidates(c.Reading,c.Text)
            .Concat(_options.MoreCandidates?.Invoke(c.Reading)??[]).Distinct(StringComparer.Ordinal).Take(256).ToArray()).ToArray();
        var failed=new HashSet<(int,int)>();
        bool Match(int clause,int at)
        {
            if(clause==lists.Length)return at==result.Length;
            if(!failed.Add((clause,at)))return false;
            return lists[clause].Any(c=>c.Length>0 && result.AsSpan(at).StartsWith(c,StringComparison.Ordinal) && Match(clause+1,at+c.Length));
        }
        return Match(0,0);
    }

    // Match corrected reading boundaries to original keystrokes without altering the input units.
    private string? CorrectedClauseRaw(int start, int length)
    {
        if (_romanRaw is not { } original || _romanCorrectedRaw is not { } repaired ||
            _detector.Romaji.AnalyzeFragment(repaired) is not { IsValid: true, Partial: "" } analysis ||
            analysis.Kana != _romanCorrectedReading) return null;
        int kana = 0, raw = 0, from = -1, to = -1;
        foreach (var token in analysis.Tokens)
        {
            if (kana == start) from = raw;
            kana += token.Kana.Length; raw += token.Romaji.Length;
            if (kana == start + length) to = raw;
        }
        if (from < 0 || to < from) return null;
        var prefix = 0;
        while (prefix < Math.Min(original.Length, repaired.Length) && original[prefix] == repaired[prefix]) prefix++;
        var suffix = 0;
        while (suffix < Math.Min(original.Length, repaired.Length) - prefix && original[^(suffix + 1)] == repaired[^(suffix + 1)]) suffix++;
        int Map(int at) => at <= prefix ? at : at >= repaired.Length - suffix ? at + original.Length - repaired.Length : -1;
        var begin = Map(from); var end = Map(to);
        return begin >= 0 && end > begin && end <= original.Length ? original[begin..end] : null;
    }

    public void ReviewInputBoundary()
    {
        if (!_options.ContextOnClauseChange || _text.Raw.Length<=1) return;
        var display=_text.Display(false,_options.LiveConversion()?LiveConvert:null);
        if(display.Length==0)return;
        if (RequestRomanEvaluation(display[..^1],final:true,keepTail:true,rawOverride:_text.Raw[..^1]))
        {
            if (_options.RuleBasedRomanReview) UpdateView();
            else PollContextEvaluation(Environment.TickCount64);
        }
    }

    public bool TryCommitRomanTypo(string suffix)
    {
        if (!RequestRomanEvaluation(_text.Display(false, _options.LiveConversion() ? LiveConvert : null), final: true)) return false;
        if (_modelBoundaryPrefix is not null) return false;
        _romanCommitSuffix = suffix;
        if (_options.RuleBasedRomanReview) FinishRomanCommit(useResult: true);
        else PollContextEvaluation(Environment.TickCount64 + _options.ContextDebounceMs);
        return true;
    }

    private void FinishRomanCommit(bool useResult)
    {
        var suffix = _romanCommitSuffix ?? "";
        var text = useResult && RomanReviewCurrent ? RomanDisplay : RomanAnchorCurrent ? _romanBaseline! + RomanTail : CurrentDisplay(false);
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
        if (!RomanReviewCurrent || !RomanCandidateTexts.Contains(text, StringComparer.Ordinal)) return false;
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

// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text.Json;

namespace Meltype.Composition;

/// <summary>A conversion target and its context. Candidates preserve English and protected spans.</summary>
public sealed record ContextRequest(string Reading, string Left, string Right, IReadOnlyList<string> Candidates,
    bool RomanTypo = false, ModelNumberBoundary? ModelBoundary = null, bool KanaConversion = false);

public interface IContextEvaluator
{
    Task<string?> EvaluateAsync(ContextRequest request, CancellationToken cancellation);
}

/// <summary>Owner-thread state machine. Workers never touch the controller or the application's document.</summary>
public sealed class ContextEvaluation(IContextEvaluator evaluator, int debounceMs = 180, int timeoutMs = 1500) : IDisposable
{
    private string? _key;
    private ContextRequest? _request;
    private Task<string?>? _task;
    private CancellationTokenSource? _cancel;
    private long _due, _deadline;
    private bool _started;
    public bool Pending => _request is not null && (!_started || _task is not null);

    public void Update(ContextRequest request, long now)
    {
        var key = JsonSerializer.Serialize(request);
        if (_key == key) return;
        Reset(); _key = key;
        if (request.Candidates.Count < (request.KanaConversion ? 1 : 2) || request.Reading.Length > 100) return;
        _request = request with { Candidates = request.Candidates.ToArray() };
        _due = now + debounceMs;
    }

    public string? Poll(long now)
    {
        if (_request is null) return null;
        if (!_started && now >= _due)
        {
            _started = true; _deadline = now + timeoutMs;
            _cancel = new CancellationTokenSource(timeoutMs);
            // Evaluators return promptly; model work is asynchronous and never touches owner state.
            var request = _request; var token = _cancel.Token;
            try { _task = evaluator.EvaluateAsync(request, token); }
            catch (Exception e) { _task = Task.FromException<string?>(e); }
        }
        if (_task is null) return null;
        if (now >= _deadline) { _cancel?.Cancel(); Observe(_task); _task = null; return null; }
        if (!_task.IsCompleted) return null;
        var task = _task; _task = null;
        if (!task.IsCompletedSuccessfully)
        {
            if (task.Exception is { } error) Diagnostics.Log.Warn("文脈補正を採用できませんでした: " + error.GetBaseException().GetType().Name);
            Observe(task); return null;
        }
        var result = task.Result;
        return result is not null && (_request.KanaConversion ? ZenzCandidateRules.AllowsCandidate(_request,result) : _request.Candidates.Contains(result,StringComparer.Ordinal)) ? result : null;
    }

    private static void Observe(Task task) => _ = task.ContinueWith(t => _ = t.Exception,
        CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

    public void Reset()
    {
        _cancel?.Cancel(); _cancel?.Dispose(); _cancel = null;
        if (_task is not null) Observe(_task);
        _task = null; _request = null; _key = null; _started = false;
    }

    public void Dispose() => Reset();
}

public static class ZenzCandidateRules
{
    public static bool AllowsCandidate(ContextRequest request, string candidate)
    {
        if (request.ModelBoundary is { } boundary)
        {
            if (request.KanaConversion || request.RomanTypo || request.Reading.Length != 0 || !boundary.IsValid()) return false;
            var baseline = boundary.Model + boundary.KanaSuffix;
            var literal = boundary.Model + boundary.RawSuffix;
            return request.Candidates.SequenceEqual(new[] { baseline, literal }) &&
                (candidate == baseline || candidate == literal);
        }
        if (request.KanaConversion)
            return !request.RomanTypo && request.Candidates.Count==1 && request.Reading.Length is >0 and <=64 &&
                request.Reading.All(c=>c is >= 'ぁ' and <= 'ゖ' or 'ー') && candidate.Length is >0 and <=64 &&
                candidate.All(c=>!char.IsControl(c) && !char.IsWhiteSpace(c)) &&
                PreservesLiteralText(request.Candidates[0],candidate);
        if (!request.RomanTypo) return PreservesLiteralText(request.Candidates[0], candidate);
        static bool Japanese(char c) => c is >= 'ぁ' and <= 'ゖ' or >= 'ァ' and <= 'ヺ' or 'ー'
            or '々' or 'ゝ' or 'ゞ' or 'ヽ' or 'ヾ' or >= '\u3400' and <= '\u4DBF' or >= '\u4E00' and <= '\u9FFF';
        var original = request.Candidates[0];
        return original.Length is >= 4 and <= 64 && original.Any(char.IsAsciiLetterLower) &&
            original.All(c => char.IsAsciiLetterLower(c) || Japanese(c)) &&
            request.Reading.Length is > 0 and <= 64 && request.Reading.All(c => c is >= 'ぁ' and <= 'ゖ' or 'ー') &&
            (candidate == original || candidate.Length is > 0 and <= 64 && candidate.All(Japanese));
    }
    /// <summary>Spelling changes may affect Japanese letters, never literal tokens, numbers or punctuation.</summary>
    public static bool PreservesLiteralText(string baseline, string candidate)
    {
        static string Literal(string value) => string.Concat(value.Where(c =>
            !(c is >= 'ぁ' and <= 'ゖ' or >= 'ァ' and <= 'ヺ' or '々' or 'ゝ' or 'ゞ' or 'ヽ' or 'ヾ' or >= '\u3400' and <= '\u4DBF' or >= '\u4E00' and <= '\u9FFF')));
        return Literal(baseline) == Literal(candidate);
    }
    public static string LiteralPrefix(ContextRequest request)
    {
        if (request.KanaConversion || request.Candidates.Count == 0) return "";
        var first = request.Candidates[0];
        var length = 0;
        while (length < first.Length && first[length] < 128 &&
            request.Candidates.All(c => c.Length > length && c[length] == first[length])) length++;
        return first[..length];
    }

    public static string Prompt(ContextRequest request) =>
        (request.Left.Length == 0 ? (request.KanaConversion ? "\uEE02。" : "") : "\uEE02" + request.Left[^Math.Min(256, request.Left.Length)..]) +
        (request.Right.Length == 0 ? "" : "\uEE07" + request.Right[..Math.Min(128, request.Right.Length)]) +
        "\uEE00" + CompositionText.ToKatakana(request.Reading) + "\uEE01" + LiteralPrefix(request);

    public static string Grammar(ContextRequest request) => "root ::= " + string.Join(" | ", request.Candidates.Distinct(StringComparer.Ordinal)
        .Select(s => JsonSerializer.Serialize(s[LiteralPrefix(request).Length..], new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping })));

}

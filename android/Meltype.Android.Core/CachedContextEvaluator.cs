// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text.Json;
using Meltype.Composition;
namespace Meltype.AndroidCore;

public sealed class CachedContextEvaluator(IContextEvaluator inner) : IContextEvaluator, IDisposable
{
    private readonly object gate = new();
    private readonly Dictionary<string, string> results = [];
    private readonly Dictionary<(string Reading, string Left, string Right), string> accepted = [];
    private int generation;
    private bool disposed;
    public async Task<string?> EvaluateAsync(ContextRequest request, CancellationToken cancellation)
    {
        var key = JsonSerializer.Serialize(request);
        int version;
        lock (gate)
        {
            if (disposed || cancellation.IsCancellationRequested) return null;
            version = generation;
            if (results.TryGetValue(key, out var cached)) return cached;
            if (request.KanaConversion && request.Candidates.Count == 1 &&
                TryAccepted(request, out var selected) && request.Candidates[0] == selected &&
                ZenzCandidateRules.AllowsCandidate(request, selected)) return selected;
        }
        var result = await inner.EvaluateAsync(request, cancellation).ConfigureAwait(false);
        lock (gate)
        {
            if (disposed || cancellation.IsCancellationRequested || version != generation) return null;
            if (result is not null && (request.KanaConversion ? ZenzCandidateRules.AllowsCandidate(request, result) :
                request.Candidates.Contains(result, StringComparer.Ordinal)))
            {
                if (results.Count >= 64) results.Clear();
                results[key] = result;
                if (!request.KanaConversion && request.ModelBoundary is null && result != request.Candidates[0] &&
                    (result != request.Reading || !request.Candidates.Skip(1).Any(c => c != result)))
                {
                    if (accepted.Count >= 64) accepted.Clear();
                    accepted[(request.Reading, request.Left, request.Right)] = result;
                }
            }
            return result;
        }
    }
    private bool TryAccepted(ContextRequest request, out string selected)
    {
        if (accepted.TryGetValue((request.Reading, request.Left, request.Right), out selected!)) return true;
        return request.Right.Length > 0 && request.Right[0] is '，' or '、' or ',' or '。' or '.' &&
            accepted.TryGetValue((request.Reading, request.Left, request.Right[1..]), out selected!);
    }
    public void ClearCaches() { lock (gate) { generation++; results.Clear(); accepted.Clear(); } }
    public void Dispose()
    {
        lock (gate) { if (disposed) return; disposed = true; generation++; results.Clear(); accepted.Clear(); }
        if (inner is IDisposable disposable) disposable.Dispose();
    }
}

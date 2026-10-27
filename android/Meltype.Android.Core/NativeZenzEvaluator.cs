// SPDX-License-Identifier: GPL-3.0-or-later
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Meltype.Composition;

namespace Meltype.AndroidCore;

/// <summary>Serialized CPU inference. No networking, text logging, or document access.</summary>
public sealed class NativeZenzEvaluator(string modelPath, Action? prepareModel = null) : IContextEvaluator, IDisposable
{
    public const string ModelSha256 = "29C223D4C23327B80FD13EBB5AB2555057A46317997D5DA391584FFBEF0DB673";
    private readonly object _gate = new();
    private readonly CancellationTokenSource _lifetime = new();
    private IntPtr _handle;
    private volatile bool _disposed;
    private bool _unavailable;

    public Task<bool> WarmUpAsync() => Task.Run(() => { lock (_gate) return EnsureLoaded(); });

    private bool EnsureLoaded()
    {
        if (_disposed || _unavailable) return false;
        if (_handle != IntPtr.Zero) return true;
        try
        {
            prepareModel?.Invoke();
            using var model = File.OpenRead(modelPath);
            if (Convert.ToHexString(SHA256.HashData(model)) != ModelSha256) { _unavailable = true; return false; }
            if (_disposed) return false;
            _handle = Create(modelPath, Math.Clamp(Environment.ProcessorCount / 2, 1, 4));
            _unavailable = _handle == IntPtr.Zero;
            return !_unavailable;
        }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException or
            IOException or UnauthorizedAccessException)
        {
            _unavailable = true;
            Diagnostics.Log.Warn("端末内AIを準備できません: " + error.GetType().Name);
            return false;
        }
    }

    public Task<string?> EvaluateAsync(ContextRequest request, CancellationToken cancellation)
    {
        if (_disposed || request.Candidates.Count < (request.KanaConversion ? 1 : 2) || request.Candidates.Count > 5 || request.Candidates.Any(c => c.Length is 0 or > 64 ||
            c.Any(char.IsControl) || !ZenzCandidateRules.AllowsCandidate(request, c))) return Task.FromResult<string?>(null);
        var copy = request with { Left = request.Left[^Math.Min(64, request.Left.Length)..],
            Right = request.Right[..Math.Min(32, request.Right.Length)], Candidates = request.Candidates.ToArray() };
        return Task.Run<string?>(() =>
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation, _lifetime.Token);
            lock (_gate)
            {
                if (_disposed || _unavailable || linked.IsCancellationRequested) return null;
                try
                {
                    if (!EnsureLoaded()) return null;
                    if (linked.IsCancellationRequested) return null;
                    using var registration = linked.Token.Register(() => Cancel(_handle));
                    var output = new byte[2048];
                    var count = Generate(_handle, ZenzCandidateRules.Prompt(copy), copy.KanaConversion ? null : ZenzCandidateRules.Grammar(copy), output, output.Length, 1400);
                    if (count <= 0 || count >= output.Length || linked.IsCancellationRequested) return null;
                    var text = ZenzCandidateRules.LiteralPrefix(copy) + new UTF8Encoding(false, true).GetString(output, 0, count);
                    if(copy.KanaConversion)text=text.Trim();
                    return (copy.KanaConversion ? ZenzCandidateRules.AllowsCandidate(copy,text) : copy.Candidates.Contains(text,StringComparer.Ordinal)) ? text : null;
                }
                catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException or
                    IOException or UnauthorizedAccessException or DecoderFallbackException)
                {
                    _unavailable = true;
                    Diagnostics.Log.Warn("端末内AIを使用できません: " + error.GetType().Name);
                    return null;
                }
            }
        });
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _lifetime.Cancel();
        // Joining native inference never blocks the UI thread.
        _ = Task.Run(() => { lock (_gate) { if (_handle != IntPtr.Zero) Destroy(_handle); _handle = IntPtr.Zero; } });
    }

    [DllImport("meltype_zenz", EntryPoint = "meltype_zenz_create", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr Create([MarshalAs(UnmanagedType.LPUTF8Str)] string model, int threads);
    [DllImport("meltype_zenz", EntryPoint = "meltype_zenz_generate", CallingConvention = CallingConvention.Cdecl)]
    private static extern int Generate(IntPtr handle, [MarshalAs(UnmanagedType.LPUTF8Str)] string prompt,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string? grammar, [Out] byte[] output, int capacity, int timeoutMs);
    [DllImport("meltype_zenz", EntryPoint = "meltype_zenz_cancel", CallingConvention = CallingConvention.Cdecl)]
    private static extern void Cancel(IntPtr handle);
    [DllImport("meltype_zenz", EntryPoint = "meltype_zenz_destroy", CallingConvention = CallingConvention.Cdecl)]
    private static extern void Destroy(IntPtr handle);
}

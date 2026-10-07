// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text.Json;

namespace Meltype.AndroidCore;

public static class TypoTrainingPolicy
{
    public static bool Allows(int inputType, int imeOptions)
    {
        if ((imeOptions & 0x1000000) != 0 || (inputType & 15) != 1) return false;
        return (inputType & 0xff0) is not (0x10 or 0x20 or 0x80 or 0x90 or 0xd0 or 0xe0);
    }
}

// Main-thread editing snapshots. Focus/selection changes must call Cancel.
public sealed class TypoEditCapture(Action<string, string> save)
{
    private string? _before;
    private bool _typed;
    public bool Pending => _before != null;
    public void BeforeDelete(string? text) { if (_before == null && !string.IsNullOrEmpty(text)) _before = Bound(text); }
    public void Typed() { if (_before != null) _typed = true; }
    public void Finish(string? text)
    {
        var before = _before; var typed = _typed; Cancel();
        var after = Bound(text ?? "");
        if (typed && before != null && after.Length > 0 && before != after) save(before, after);
    }
    public void Cancel() { _before = null; _typed = false; }
    public static string Bound(string text)
    {
        var start = Math.Max(0, text.Length - 64);
        if (start > 0 && char.IsLowSurrogate(text[start])) start++;
        return text[start..];
    }
}

public sealed class TypoTrainingStore
{
    private static readonly object Gate = new();
    private readonly string _folder;
    private readonly Func<DateTimeOffset> _clock;
    private string StateFile => Path.Combine(_folder, "until.txt");
    private string EventsFile => Path.Combine(_folder, "events.jsonl");
    public TypoTrainingStore(string folder, Func<DateTimeOffset>? clock = null)
    { _folder = folder; _clock = clock ?? (() => DateTimeOffset.UtcNow); }
    public DateTimeOffset? Until
    {
        get { lock (Gate) { return File.Exists(StateFile) && DateTimeOffset.TryParse(File.ReadAllText(StateFile), out var value) ? value : null; } }
    }
    public bool Active => Until is { } until && _clock() < until;
    public int Count { get { lock (Gate) return File.Exists(EventsFile) ? File.ReadLines(EventsFile).Count() : 0; } }
    public void Start() { lock (Gate) { Directory.CreateDirectory(_folder); File.WriteAllText(StateFile, _clock().AddDays(7).ToString("O")); } }
    public void Stop() { lock (Gate) { if (File.Exists(StateFile)) File.Delete(StateFile); } }
    public void Clear() { lock (Gate) { Stop(); if (File.Exists(EventsFile)) File.Delete(EventsFile); } }
    public void Append(string before, string after)
    {
        lock (Gate)
        {
            if (!Active || Count >= 5000) return;
            var record = new { id = Guid.NewGuid().ToString(), day = _clock().ToString("yyyy-MM-dd"),
                before = TypoEditCapture.Bound(before), after = TypoEditCapture.Bound(after) };
            File.AppendAllText(EventsFile, JsonSerializer.Serialize(record) + "\n");
        }
    }
}

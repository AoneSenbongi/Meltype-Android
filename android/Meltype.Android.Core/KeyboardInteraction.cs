// SPDX-License-Identifier: GPL-3.0-or-later
namespace Meltype.AndroidCore;
public sealed class KeyPressOrder
{
    private SymbolPress? _pending;
    public void BeforePress() { var pending = _pending; _pending = null; pending?.Up(); }
    internal void Track(SymbolPress press) => _pending = press;
    internal void Release(SymbolPress press) { if (ReferenceEquals(_pending, press)) _pending = null; }
}
public static class KeyTouchBounds
{
    public static bool Contains(float x, float y, int width, int height, int slop) =>
        x >= -slop && x < width + slop && y >= -slop && y < height + slop;
}
public sealed class SymbolPress(Action tap, Action hold, KeyPressOrder? order = null, bool longPressEnabled = true)
{
    private bool _down, _fired;
    public void Down() { order?.BeforePress(); _down = true; _fired = false; order?.Track(this); }
    private void Activate() { if (_down && !_fired) { _fired = true; hold(); } }
    public void Hold() { if (longPressEnabled) Activate(); }
    public bool Move(float dx, float dy, float threshold)
    {
        if (!_down || threshold <= 0 || dy >= 0 || -dy <= Math.Abs(dx)) return false;
        if (-dy >= threshold) Activate();
        return true;
    }
    public void Up() { var fire = _down && !_fired; Cancel(); if (fire) tap(); }
    public void Cancel() { _down = false; _fired = false; order?.Release(this); }
}
public static class CursorMovement
{
    public static int Target(string text, int start, int end, bool left)
    {
        start = Math.Clamp(start, 0, text.Length); end = Math.Clamp(end, 0, text.Length);
        if (start != end) return left ? Math.Min(start,end) : Math.Max(start,end);
        if (left) return start == 0 ? 0 : start - (start > 1 && char.IsLowSurrogate(text[start-1]) && char.IsHighSurrogate(text[start-2]) ? 2 : 1);
        return end == text.Length ? end : end + (end+1 < text.Length && char.IsHighSurrogate(text[end]) && char.IsLowSurrogate(text[end+1]) ? 2 : 1);
    }
}
public static class KeyboardSymbols
{
    public static string? For(char c) => c switch
    {
        >= 'a' and <= 'z' when "qwertyuiop".Contains(c) => "1234567890"["qwertyuiop".IndexOf(c)].ToString(),
        >= 'a' and <= 'z' when "asdfghjkl".Contains(c) => new[]{"@","#","¥","%","&","-","+","()","/"}["asdfghjkl".IndexOf(c)],
        >= 'a' and <= 'z' when "zxcvbnm".Contains(c) => new[]{"_","'",":",";","\"","!","?"}["zxcvbnm".IndexOf(c)],
        '-' => "「」", _ => null
    };
}
public sealed class ClipboardHistory(Func<DateTimeOffset>? clock = null)
{
    private readonly Func<DateTimeOffset> _clock = clock ?? (() => DateTimeOffset.UtcNow);
    private readonly List<(string Text, DateTimeOffset Time)> _items = [];
    public IReadOnlyList<string> Items { get { _items.RemoveAll(x => _clock()-x.Time >= TimeSpan.FromHours(1)); return _items.Select(x=>x.Text).ToArray(); } }
    public void Add(string? text, bool sensitive = false)
    {
        if(sensitive || string.IsNullOrWhiteSpace(text) || text.Length > 10000) return;
        _ = Items; _items.RemoveAll(x=>x.Text==text); _items.Insert(0,(text,_clock()));
        if(_items.Count>10) _items.RemoveRange(10,_items.Count-10);
    }
    public void Clear() => _items.Clear();
}

public sealed class ClipboardSuggestion(long lastTimestamp = 0)
{
    public long LastTimestamp { get; private set; } = lastTimestamp;
    public string? Text { get; private set; }
    public void Open(long timestamp, string? text)
    {
        if (timestamp <= 0 || string.IsNullOrWhiteSpace(text)) { Text = null; return; }
        if (timestamp == LastTimestamp) return;
        Text = text;
        LastTimestamp = timestamp;
    }
    public void Dismiss() => Text = null;
}

public static class ClipboardInputPolicy
{
    public static bool Allows(int inputType) => (inputType & 15) switch
    {
        1 => (inputType & 0xff0) is not (0x80 or 0x90 or 0xe0),
        2 => (inputType & 0xff0) != 0x10,
        3 or 4 => true,
        _ => false
    };
}

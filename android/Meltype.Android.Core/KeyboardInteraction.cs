// SPDX-License-Identifier: GPL-3.0-or-later
namespace Meltype.AndroidCore;
public sealed class SymbolPress(Action tap, Action hold)
{
    private bool _down, _fired;
    public void Down() { _down = true; _fired = false; }
    public void Hold() { if (_down && !_fired) { _fired = true; hold(); } }
    public void Up() { var fire = _down && !_fired; Cancel(); if (fire) tap(); }
    public void Cancel() { _down = false; _fired = false; }
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
        >= 'a' and <= 'z' when "asdfghjkl".Contains(c) => "@#¥%&-+()"["asdfghjkl".IndexOf(c)].ToString(),
        >= 'a' and <= 'z' when "zxcvbnm".Contains(c) => new[]{"!","?","「」","/",":",";","\""}["zxcvbnm".IndexOf(c)],
        '-' => "_", _ => null
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

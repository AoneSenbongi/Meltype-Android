// SPDX-License-Identifier: GPL-3.0-or-later
using Meltype.Detection;
namespace Meltype.AndroidCore;

public sealed class NextKeyPrediction
{
    private readonly Dictionary<string, Dictionary<char, int>> _next = new(StringComparer.Ordinal);
    public NextKeyPrediction(IEnumerable<string> words)
    {
        foreach (var word in words.Distinct(StringComparer.Ordinal))
            for (var i = 2; i < Math.Min(word.Length, 33); i++)
            {
                if (!_next.TryGetValue(word[..i], out var counts)) _next[word[..i]] = counts = [];
                counts[word[i]] = counts.GetValueOrDefault(word[i]) + 1;
            }
    }
    public static NextKeyPrediction BuiltIn() => new(new[] {"english.txt", "japanese.txt"}
        .SelectMany(name => WordList.ParseWords(DictionarySource.ReadEmbedded(name))));
    public double Probability(string prefix, char letter)
    {
        // Japanese romaji can contain several joined dictionary words.
        for (var start = Math.Max(0, prefix.Length - 32); start <= prefix.Length - 2; start++)
            if (_next.TryGetValue(prefix[start..], out var counts))
                return (double)counts.GetValueOrDefault(letter) / counts.Values.Sum();
        return 0;
    }
}

public readonly record struct TouchKey(char Letter, float X, float Y, float Width, float Height)
{
    public bool Contains(float x, float y) => x >= X && x < X + Width && y >= Y && y < Y + Height;
}
public static class AdaptiveKeyTarget
{
    public static LetterTouch? Capture(IReadOnlyList<TouchKey> keys, float x, float y, Func<char, double> probability)
    {
        var original = keys.FirstOrDefault(k => k.Contains(x, y));
        if (original.Width <= 0) return null;
        var alternatives = new List<TouchAlternative>();
        foreach (var key in keys)
        {
            if (key.Letter == original.Letter || key.Width <= 0 || key.Height <= 0) continue;
            // Evidence only: never redirect a key or include centers, gaps or other rows.
            if (Math.Abs(key.Y - original.Y) > 1 || y < key.Y || y >= key.Y + key.Height) continue;
            var distance = x < key.X ? key.X - x : x >= key.X + key.Width ? x - key.X - key.Width : 0;
            if (distance > Math.Min(key.Width, original.Width) * .30f) continue;
            var dx = (x - key.X - key.Width / 2) / key.Width;
            var dy = (y - key.Y - key.Height / 2) / key.Height;
            alternatives.Add(new(key.Letter, -(dx * dx + dy * dy)));
        }
        return new(original, x, y, alternatives.OrderByDescending(a => a.Score)
            .ThenByDescending(a => probability(a.Letter)).Take(2).ToArray());
    }
}

public sealed record TouchAlternative(char Letter, double Score);
public sealed record LetterTouch(TouchKey Original, float X, float Y, IReadOnlyList<TouchAlternative> Alternatives);

// Scoped to the exact original composition. Coordinates are never persisted or learned.
public sealed class TouchInputHistory
{
    private string _raw = "";
    private readonly Dictionary<int, LetterTouch> _touches = [];
    public void Clear() { _raw = ""; _touches.Clear(); }
    public void Append(string before, char letter, LetterTouch? touch)
    {
        if (_raw != before) Clear();
        if (letter is ' ' or '\n') return;
        if (letter is ',' or '.' or '!' or '?' && _raw == before)
        { _raw = before + letter; return; }
        if (before.Length >= 64 || !before.All(char.IsAsciiLetterLower) || !char.IsAsciiLetterLower(letter))
        { Clear(); return; }
        if (touch?.Original.Letter == letter && touch.Original.Contains(touch.X, touch.Y) && touch.Alternatives.Count > 0)
            _touches[before.Length] = touch;
        _raw = before + letter;
    }
    public IReadOnlyList<string> Alternatives(string raw)
    {
        if (raw.Length < 4 || !_raw.StartsWith(raw, StringComparison.Ordinal)) return [];
        return _touches.Where(p => p.Key < raw.Length && raw[p.Key] == p.Value.Original.Letter)
            .SelectMany(p => p.Value.Alternatives.Where(a => char.IsAsciiLetterLower(a.Letter))
                .Select(a => (Raw: raw[..p.Key] + a.Letter + raw[(p.Key + 1)..], a.Score)))
            .OrderByDescending(a => a.Score).Select(a => a.Raw).Distinct(StringComparer.Ordinal).Take(3).ToArray();
    }
}


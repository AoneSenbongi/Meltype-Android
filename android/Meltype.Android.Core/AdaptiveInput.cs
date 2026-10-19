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
    public static char? Choose(IReadOnlyList<TouchKey> keys, float x, float y, Func<char, double> probability)
    {
        var original = keys.FirstOrDefault(k => k.Contains(x, y));
        if (original.Width <= 0) return null;
        var result = original.Letter;
        foreach (var key in keys)
        {
            var p = probability(key.Letter);
            if (key.Letter == original.Letter || p < .55 || p <= probability(original.Letter)) continue;
            // Only share a vertical boundary between letter cells in the same row.
            if (Math.Abs(key.Y - original.Y) > 1 || y < key.Y || y >= key.Y + key.Height) continue;
            var distance = x < key.X ? key.X - x : x >= key.X + key.Width ? x - key.X - key.Width : 0;
            if (distance <= Math.Min(key.Width, original.Width) * .30f) result = key.Letter;
        }
        return result;
    }
}


// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text.Json;
using Meltype.Composition;
using Meltype.Detection;

namespace Meltype.AndroidCore;

public sealed record PersonalRomajiContext(string Left, string Following, bool AtStart, bool AtEnd);
public sealed record PersonalRomajiRule(string Wrong, string Right, int Support, PersonalRomajiContext[]? Contexts = null);

// The private model contains short recurring edits, never sentence replacements.
public sealed class PersonalRomajiTypos
{
    private readonly PersonalRomajiRule[] _rules;
    private readonly HashSet<string> _protected;
    private readonly RomajiDetector _roman = new();
    private readonly RomajiTypoCorrector _japanese;
    private static readonly Lazy<HashSet<string>> English = new(() =>
        new(WordList.ParseWords(DictionarySource.ReadEmbedded("english.txt")), StringComparer.Ordinal));

    public PersonalRomajiTypos(IEnumerable<PersonalRomajiRule> rules, IEnumerable<string>? protectedReadings = null)
    {
        _rules = rules.Where(r => r.Support >= 3 && r.Wrong != r.Right &&
            r.Wrong.Length <= 2 && r.Right.Length <= 2 &&
            (Math.Max(r.Wrong.Length, r.Right.Length) <= 1 ||
             r.Wrong.Length == 2 && r.Right == new string(r.Wrong.Reverse().ToArray())) &&
            (r.Wrong + r.Right).All(c => c is >= 'a' and <= 'z')).Distinct().ToArray();
        _protected = new(protectedReadings ?? [], StringComparer.Ordinal);
        _japanese = RomajiTypoCorrector.Load(_roman);
    }

    private sealed record Model(int Version, int MinSupport, PersonalRomajiRule[] Rules, string[]? Protected);
    public static PersonalRomajiTypos Load(string path)
    {
        try
        {
            var model = JsonSerializer.Deserialize<Model>(File.ReadAllText(path));
            return model is { Version: 1, MinSupport: >= 3, Rules: not null } &&
                model.Rules.All(r => r != null && r.Wrong != null && r.Right != null &&
                    r.Contexts is { Length: > 0 } && r.Contexts.All(c => c != null &&
                        c.Left is { Length: <= 3 } && c.Following is { Length: <= 3 }))
                ? new(model.Rules.Where(r => r.Support >= model.MinSupport), model.Protected) : new([]);
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return new([]); }
    }

    public RomajiFix? Fix(string letters, bool final)
    {
        if (_rules.Length == 0 || letters.Length is < 4 or > 64 ||
            letters.Any(c => c is < 'a' or > 'z') || English.Value.Contains(letters)) return null;
        var canonical = _roman.Canonicalize(letters, final);
        if (_protected.Contains(canonical) || _japanese.IsCommonJapanese(letters) ||
            _japanese.IsKnownReading(_roman.ConvertLenient(letters, true))) return null;
        var candidates = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var rule in _rules)
        for (var at = 0; at <= canonical.Length - rule.Wrong.Length; at++)
        {
            if (!canonical.AsSpan(at).StartsWith(rule.Wrong, StringComparison.Ordinal)) continue;
            var end = at + rule.Wrong.Length;
            if (rule.Contexts != null && !rule.Contexts.Any(c => at >= c.Left.Length &&
                canonical.AsSpan(0, at).EndsWith(c.Left, StringComparison.Ordinal) &&
                canonical.AsSpan(end).StartsWith(c.Following, StringComparison.Ordinal) &&
                (!c.AtStart || at == c.Left.Length) && (!c.AtEnd || end + c.Following.Length == canonical.Length))) continue;
            // A following syllable must have arrived before changing a live prefix.
            if (!final && canonical.Length - at - rule.Wrong.Length < 2) continue;
            var right = canonical[..at] + rule.Right + canonical[(at + rule.Wrong.Length)..];
            var kana = _roman.ConvertLenient(right, true);
            if (kana.Any(c => c is < 'ぁ' or > 'ゖ') || !_japanese.IsKnownReading(kana)) continue;
            candidates.TryAdd(kana, right);
        }
        return candidates.Count == 1
            ? new RomajiFix(letters, candidates.First().Value, candidates.First().Key) : null;
    }
}

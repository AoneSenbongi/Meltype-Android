// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text;
using System.Text.Json;
using Meltype.Composition;

namespace Meltype.AndroidCore;

public sealed class AndroidUserDictionary
{
    public sealed record ImportResult(int Added, int Skipped);
    private readonly string _path;
    private List<UserWord> _words = [];
    public int Count => _words.Count;
    public UserDictionary CompositionDictionary { get; private set; } = new(null, builtIn: false);
    public AndroidUserDictionary(string path)
    {
        _path = path;
        if (File.Exists(path)) _words = Parse(File.ReadAllBytes(path)).Words;
        Rebuild();
    }
    public IReadOnlyList<string> Lookup(string reading) => _words.Where(w => w.Reading == reading).Select(w => w.Word).Reverse().ToArray();
    public int Add(string reading, string word) => Merge([Validate(reading, word)]);
    public ImportResult Import(byte[] bytes)
    {
        var result = Parse(bytes);
        return new(Merge(result.Words), result.Skipped);
    }
    private int Merge(IEnumerable<UserWord> input)
    {
        var next = _words.Concat(input).Distinct().ToList();
        if (next.Count > 5000) throw new InvalidDataException("登録できる単語は5000件までです。");
        var added = next.Count - _words.Count;
        if (added == 0) return 0;
        var json = JsonSerializer.Serialize(next);
        if (Encoding.UTF8.GetByteCount(json) > 1_000_000) throw new InvalidDataException("辞書の合計が1MBを超えます。単語を減らしてください。");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
        var temp = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, json, new UTF8Encoding(false));
            File.Move(temp, _path, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
        _words = next; Rebuild(); return added;
    }
    private void Rebuild()
    {
        var dictionary = new UserDictionary(null, builtIn: false);
        dictionary.AddRange(_words.Where(w => w.Reading.Length >= UserDictionary.MinReadingLength));
        CompositionDictionary = dictionary;
    }
    private static UserWord Validate(string reading, string word)
    {
        reading = new string(reading.Trim().Select(c => c is >= 'ァ' and <= 'ヶ' ? (char)(c - 0x60) : c).ToArray());
        word = word.Trim();
        if (reading.Length is < 1 or > 100 || word.Length is < 1 or > 256 ||
            reading.Any(c => !(c is >= 'ぁ' and <= 'ゖ' or 'ー' or '・' or >= '0' and <= '9' or >= 'a' and <= 'z' or >= 'A' and <= 'Z')) || word.Any(c => char.IsControl(c) && c is not ('\n' or '\r')))
            throw new InvalidDataException("読みはかな・英数字で1〜100文字、単語は1〜256文字で入力してください。");
        return new(reading, word);
    }
    private static (List<UserWord> Words, int Skipped) Parse(byte[] bytes)
    {
        if (bytes.Length > 1_000_000) throw new InvalidDataException("辞書ファイルは1MBまでです。");
        string text;
        using (var reader = new StreamReader(new MemoryStream(bytes), new UTF8Encoding(false, true), true)) text = reader.ReadToEnd();
        var pairs = new List<(string Reading, string Word)>();
        if (text.TrimStart().StartsWith('['))
        {
            try { pairs.AddRange((JsonSerializer.Deserialize<List<UserWord>>(text) ?? []).Select(w => (w?.Reading ?? "", w?.Word ?? ""))); }
            catch (JsonException ex) { throw new InvalidDataException("ユーザー辞書の形式を読めません。", ex); }
        }
        else if (text.TrimStart().StartsWith('{'))
        {
            try
            {
                using var json = JsonDocument.Parse(text);
                foreach (var prefix in new[] { "JAJP", "EN" })
                {
                    if (!json.RootElement.TryGetProperty(prefix + "_KEY", out var keys)) continue;
                    if (keys.ValueKind != JsonValueKind.Array || !json.RootElement.TryGetProperty(prefix + "_VALUE", out var values) ||
                        values.ValueKind != JsonValueKind.Array || keys.GetArrayLength() != values.GetArrayLength())
                        throw new InvalidDataException("Simeji辞書の読みと単語の件数が一致しません。");
                    for (var i = 0; i < keys.GetArrayLength(); i++)
                        pairs.Add((keys[i].GetString() ?? "", values[i].GetString() ?? ""));
                }
                if (pairs.Count == 0) throw new InvalidDataException("Simejiの辞書形式ではありません。");
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException)
            { throw new InvalidDataException("Simeji辞書を読めません。ファイルの形式を確認してください。", ex); }
        }
        else foreach (var line in text.Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#') || line.StartsWith('!')) continue;
            var fields = line.TrimEnd('\r').Split('\t');
            pairs.Add(fields.Length >= 2 ? (fields[0], fields[1]) : ("", ""));
        }
        var words = new List<UserWord>(); var skipped = 0;
        foreach (var pair in pairs.Distinct())
        {
            try { words.Add(Validate(pair.Reading, pair.Word)); }
            catch (InvalidDataException) { skipped++; }
        }
        return (words.Distinct().ToList(), skipped);
    }
}

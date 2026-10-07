// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Collections.Concurrent;
using System.Text;

namespace Meltype.Detection;

public readonly record struct RomajiToken(string Romaji, string Kana);

/// <summary>ローマ字としての解析結果。入力途中 (prefix) を前提にしている。</summary>
public sealed record RomajiAnalysis(
    bool IsValid,
    IReadOnlyList<RomajiToken> Tokens,
    string Partial,
    string? InvalidReason,
    int StrongYouon,
    int Tsu,
    int Sokuon,
    int LongVowels)
{
    public string Kana => string.Concat(Tokens.Select(t => t.Kana));
}

/// <summary>
/// ローマ字 → かな変換の可能性を評価する (設計書 §13)。
/// ここで分かるのは「ローマ字として成立するか」と日本語らしい特徴だけで、
/// 成立しても英語の可能性は残る (kana, sushi, radio)。最終判断は ScoreEngine が行う。
/// </summary>
public sealed class RomajiDetector
{
    // かな → 綴り (先頭が標準の綴り)。l / x / v 系は英語と区別できないので意図的に含めない。
    private static readonly (string Kana, string[] Spellings)[] Table =
    [
        ("あ", ["a"]), ("い", ["i", "yi"]), ("う", ["u", "whu", "wu"]), ("え", ["e"]), ("お", ["o"]),
        ("か", ["ka"]), ("き", ["ki"]), ("く", ["ku"]), ("け", ["ke"]), ("こ", ["ko"]),
        ("きゃ", ["kya"]), ("きゅ", ["kyu"]), ("きょ", ["kyo"]),
        ("さ", ["sa"]), ("し", ["shi", "si"]), ("す", ["su"]), ("せ", ["se", "ce"]), ("そ", ["so"]),
        ("しゃ", ["sha", "sya"]), ("しゅ", ["shu", "syu"]), ("しょ", ["sho", "syo"]), ("しぇ", ["she", "sye"]),
        ("た", ["ta"]), ("ち", ["chi", "ti"]), ("つ", ["tsu", "tu"]), ("て", ["te"]), ("と", ["to"]),
        ("ちゃ", ["cha", "tya", "cya"]), ("ちゅ", ["chu", "tyu", "cyu"]), ("ちょ", ["cho", "tyo", "cyo"]), ("ちぇ", ["che", "tye"]),
        ("な", ["na"]), ("に", ["ni"]), ("ぬ", ["nu"]), ("ね", ["ne"]), ("の", ["no"]),
        ("にゃ", ["nya"]), ("にゅ", ["nyu"]), ("にょ", ["nyo"]),
        ("は", ["ha"]), ("ひ", ["hi"]), ("ふ", ["fu", "hu"]), ("へ", ["he"]), ("ほ", ["ho"]),
        ("ひゃ", ["hya"]), ("ひゅ", ["hyu"]), ("ひょ", ["hyo"]),
        ("ふぁ", ["fa"]), ("ふぃ", ["fi"]), ("ふぇ", ["fe"]), ("ふぉ", ["fo"]),
        ("ま", ["ma"]), ("み", ["mi"]), ("む", ["mu"]), ("め", ["me"]), ("も", ["mo"]),
        ("みゃ", ["mya"]), ("みゅ", ["myu"]), ("みょ", ["myo"]),
        ("や", ["ya"]), ("ゆ", ["yu"]), ("よ", ["yo"]),
        ("ら", ["ra"]), ("り", ["ri"]), ("る", ["ru"]), ("れ", ["re"]), ("ろ", ["ro"]),
        ("りゃ", ["rya"]), ("りゅ", ["ryu"]), ("りょ", ["ryo"]),
        ("わ", ["wa"]), ("を", ["wo"]), ("うぉ", ["who"]),
        // Microsoft IME の wh の打ち方 (whu = う、wha = うぁ)
        ("うぁ", ["wha"]),
        ("が", ["ga"]), ("ぎ", ["gi"]), ("ぐ", ["gu"]), ("げ", ["ge"]), ("ご", ["go"]),
        ("ぎゃ", ["gya"]), ("ぎゅ", ["gyu"]), ("ぎょ", ["gyo"]),
        ("ざ", ["za"]), ("じ", ["ji", "zi"]), ("ず", ["zu"]), ("ぜ", ["ze"]), ("ぞ", ["zo"]),
        ("じゃ", ["ja", "jya", "zya"]), ("じゅ", ["ju", "jyu", "zyu"]), ("じょ", ["jo", "jyo", "zyo"]), ("じぇ", ["je", "jye", "zye"]),
        ("だ", ["da"]), ("ぢ", ["di"]), ("づ", ["du"]), ("で", ["de"]), ("ど", ["do"]),
        ("ぢゃ", ["dya"]), ("ぢゅ", ["dyu"]), ("ぢょ", ["dyo"]),
        ("ば", ["ba"]), ("び", ["bi"]), ("ぶ", ["bu"]), ("べ", ["be"]), ("ぼ", ["bo"]),
        ("びゃ", ["bya"]), ("びゅ", ["byu"]), ("びょ", ["byo"]),
        ("ぱ", ["pa"]), ("ぴ", ["pi"]), ("ぷ", ["pu"]), ("ぺ", ["pe"]), ("ぽ", ["po"]),
        ("ぴゃ", ["pya"]), ("ぴゅ", ["pyu"]), ("ぴょ", ["pyo"]),
    ];

    // 英語の綴りにほぼ現れない拗音 (sha/cha/ja は shut, chat, jam などで普通に出るので除外)。
    private static readonly HashSet<string> StrongYouonKana =
    [
        "きゃ", "きゅ", "きょ", "にゃ", "にゅ", "にょ", "ひゃ", "ひゅ", "ひょ", "みゃ", "みゅ", "みょ",
        "りゃ", "りゅ", "りょ", "ぎゃ", "ぎゅ", "ぎょ", "びゃ", "びゅ", "びょ", "ぴゃ", "ぴゅ", "ぴょ",
    ];

    // 変換ボックスでだけ使う綴り (小書き文字・外来音)。英語かどうかの判定には使わない
    // (l / x / v を読めるようにすると hello や live までローマ字として成立してしまう)。
    private static readonly (string Kana, string[] Spellings)[] CompositionTable =
    [
        ("ぁ", ["xa", "la"]), ("ぃ", ["xi", "li", "xyi", "lyi"]), ("ぅ", ["xu", "lu"]), ("ぇ", ["xe", "le", "xye", "lye"]), ("ぉ", ["xo", "lo"]),
        ("ゃ", ["xya", "lya"]), ("ゅ", ["xyu", "lyu"]), ("ょ", ["xyo", "lyo"]), ("っ", ["xtu", "ltu", "xtsu", "ltsu"]), ("ゎ", ["xwa", "lwa"]),
        ("ゕ", ["xka", "lka"]), ("ゖ", ["xke", "lke"]), ("ん", ["xn"]),
        ("ゔぁ", ["va"]), ("ゔぃ", ["vi"]), ("ゔ", ["vu"]), ("ゔぇ", ["ve"]), ("ゔぉ", ["vo"]),
        ("いぇ", ["ye"]), ("うぃ", ["wi", "whi"]), ("うぇ", ["we", "whe"]),
        // 歴史的仮名 (Microsoft IME と同じ綴り。wi / we は ウィンドウ・ウェブ の うぃ / うぇ)
        ("ゐ", ["wyi"]), ("ゑ", ["wye"]),
        ("てぃ", ["thi"]), ("でぃ", ["dhi"]), ("てゅ", ["thu"]), ("でゅ", ["dhu"]), ("とぅ", ["twu"]), ("どぅ", ["dwu"]),
        // c 行 (Microsoft IME と同じ。cake や code まで日本語として読めてしまうので判定には使わない)
        ("か", ["ca"]), ("し", ["ci"]), ("く", ["cu"]), ("こ", ["co"]),
        ("ちぃ", ["cyi", "tyi"]),
        ("くぁ", ["kwa"]), ("ぐぁ", ["gwa"]), ("つぁ", ["tsa"]), ("つぃ", ["tsi"]), ("つぇ", ["tse"]), ("つぉ", ["tso"]),
    ];

    private static readonly Dictionary<string, string> SpellingToKana = BuildSpellingMap(Table);
    private static readonly Dictionary<string, string> CompositionSpellingToKana = BuildSpellingMap([.. Table, .. CompositionTable]);
    private static readonly HashSet<string> CompositionPartials = BuildPartials(CompositionSpellingToKana);
    private static readonly Dictionary<string, string[]> KanaToSpellings = Table.ToDictionary(e => e.Kana, e => e.Spellings);
    private static readonly HashSet<string> PartialSpellings = BuildPartials(SpellingToKana);

    private static Dictionary<string, string> BuildSpellingMap((string Kana, string[] Spellings)[] table)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (kana, spellings) in table)
        {
            foreach (var spelling in spellings) map[spelling] = kana;
        }
        return map;
    }

    private static HashSet<string> BuildPartials(Dictionary<string, string> spellings)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var spelling in spellings.Keys)
        {
            for (var i = 1; i < spelling.Length; i++) set.Add(spelling[..i]);
        }
        return set;
    }

    private static bool IsVowel(char c) => c is 'a' or 'i' or 'u' or 'e' or 'o';
    private static bool IsConsonant(char c) => c is >= 'a' and <= 'z' && !IsVowel(c);

    public RomajiAnalysis Analyze(string letters) => Analyze(letters, strictStart: true, composition: false);

    /// <summary>c 行 (ca / cu / co) を k 行に読み替える (ch は そのまま)。英数状態の判定で、fucarete を fukarete として調べるのに使う。</summary>
    public static string ReadCRow(string letters)
    {
        if (!letters.Contains('c')) return letters;
        var chars = letters.ToCharArray();
        for (var i = 0; i + 1 < chars.Length; i++)
        {
            if (chars[i] == 'c' && chars[i + 1] is 'a' or 'u' or 'o') chars[i] = 'k';
        }
        return new string(chars);
    }

    /// <summary>
    /// 変換ボックス用。語の途中から解析し (語頭の「ん」「っ」も許す)、"nn" は Microsoft IME と同じく常に「ん」と読む
    /// (tanni → たんい。こんにちは は konnnichiha)。
    /// </summary>
    public RomajiAnalysis AnalyzeFragment(string letters) => Analyze(letters, strictStart: false, composition: true);

    /// <param name="strictStart">語頭の「ん」「っ」を不正とみなすか (語の途中から解析するときは false)。</param>
    /// <param name="composition">
    /// 変換ボックス用なら true: "nn" を常に「ん」にし、小書き文字 (xa, ltu, xn …) や外来音 (vu, thi …) も読む。
    /// false ならヘボン式で "nni" を ん + に と読む (判定・辞書の見出し語用)。
    /// </param>
    private RomajiAnalysis Analyze(string letters, bool strictStart, bool composition)
    {
        // 判定は 1 キーごとに、打った文字のあらゆる区間を何度も解析し直す (長い文では 1 文字で数万回)。
        // 結果は文字列と引数だけで決まるので、覚えておいて使い回す。
        var cache = composition ? _compositionCache : strictStart ? _strictCache : _looseCache;
        if (cache.TryGetValue(letters, out var cached)) return cached;
        var result = AnalyzeCore(letters, strictStart, composition);
        if (cache.Count >= CacheLimit) cache.Clear();
        cache[letters] = result;
        return result;
    }

    /// <summary>覚えておく解析結果の数 (超えたら捨てて覚え直す)。</summary>
    private const int CacheLimit = 50000;
    private readonly ConcurrentDictionary<string, RomajiAnalysis> _strictCache = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, RomajiAnalysis> _looseCache = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, RomajiAnalysis> _compositionCache = new(StringComparer.Ordinal);

    private static RomajiAnalysis AnalyzeCore(string letters, bool strictStart, bool composition)
    {
        var tokens = new List<RomajiToken>();
        int strongYouon = 0, tsu = 0, sokuon = 0, longVowels = 0;
        var i = 0;
        var s = letters;

        RomajiAnalysis Invalid(string reason) =>
            new(false, tokens.ToArray(), "", reason, strongYouon, tsu, sokuon, longVowels);

        while (i < s.Length)
        {
            var c = s[i];
            if (c is < 'a' or > 'z') return Invalid($"'{c}' は英字ではない");

            // ん: "nn" / 子音の前の "n"。"nni" のように母音が続く場合は ん + に (ヘボン式 konnichiwa)。
            if (c == 'n' && i + 1 < s.Length && (s[i + 1] == 'n' || (IsConsonant(s[i + 1]) && s[i + 1] != 'y')))
            {
                if (i == 0 && strictStart) return Invalid("語頭の「ん」");
                var consumed = 1;
                if (s[i + 1] == 'n' && (composition || i + 2 >= s.Length || !(IsVowel(s[i + 2]) || s[i + 2] == 'y'))) consumed = 2;
                tokens.Add(new RomajiToken(s.Substring(i, consumed), "ん"));
                i += consumed;
                continue;
            }

            // っ: 同じ子音の連続 (kk, tt, ss …) と tch。
            if (i + 1 < s.Length && IsConsonant(c) && c != 'n' &&
                (s[i + 1] == c || (c == 't' && s[i + 1] == 'c' && i + 2 < s.Length && s[i + 2] == 'h')))
            {
                if (i == 0 && strictStart) return Invalid("語頭の「っ」");
                tokens.Add(new RomajiToken(c.ToString(), "っ"));
                sokuon++;
                i += 1;
                continue;
            }

            var matched = false;
            var spellings = composition ? CompositionSpellingToKana : SpellingToKana;
            for (var length = Math.Min(4, s.Length - i); length >= 1; length--)
            {
                var piece = s.Substring(i, length);
                if (!spellings.TryGetValue(piece, out var kana)) continue;
                if (StrongYouonKana.Contains(kana)) strongYouon++;
                if (piece == "tsu") tsu++;
                if (kana == "う" && tokens.Count > 0 && EndsWithVowel(tokens[^1].Romaji, 'o', 'u')) longVowels++;
                tokens.Add(new RomajiToken(piece, kana));
                i += length;
                matched = true;
                break;
            }
            if (matched) continue;

            var rest = s[i..];
            if ((composition ? CompositionPartials : PartialSpellings).Contains(rest) || rest is "n" or "tc")
            {
                return new RomajiAnalysis(true, tokens.ToArray(), rest, null, strongYouon, tsu, sokuon, longVowels);
            }
            return Invalid($"「{rest}」はローマ字として成立しない");
        }

        return new RomajiAnalysis(true, tokens.ToArray(), "", null, strongYouon, tsu, sokuon, longVowels);
    }

    private static bool EndsWithVowel(string romaji, char a, char b) =>
        romaji.Length > 0 && (romaji[^1] == a || romaji[^1] == b);

    /// <summary>
    /// 変換ボックスの表示用。ローマ字として読めない文字はその文字だけ英字のまま残し、続きを変換する
    /// (MS-IME の「ごおｇ」と同じ振る舞い)。<paramref name="final"/> なら語末の n を ん にする。
    /// </summary>
    public string ConvertLenient(string letters, bool final)
    {
        var builder = new StringBuilder();
        var rest = letters;
        while (rest.Length > 0)
        {
            var analysis = Analyze(rest, strictStart: false, composition: true);
            foreach (var token in analysis.Tokens) builder.Append(token.Kana);
            if (analysis.IsValid)
            {
                builder.Append(final && analysis.Partial == "n" ? "ん" : analysis.Partial);
                break;
            }
            var consumed = analysis.Tokens.Sum(t => t.Romaji.Length);
            builder.Append(rest[consumed]);
            rest = rest[(consumed + 1)..];
        }
        return builder.ToString();
    }

    /// <summary>ログ表示用。確定した部分だけをかなにし、途中の子音は英字のまま残す。</summary>
    public string ToKana(string letters)
    {
        var analysis = Analyze(letters);
        return analysis.IsValid ? analysis.Kana + analysis.Partial : letters;
    }

    /// <summary>
    /// 辞書の見出し語 (ヘボン式) から、実際に打たれうる綴りの揺れ (si/shi, tu/tsu, nn/n …) を列挙する。
    /// 組み合わせ爆発を防ぐため最大 <paramref name="limit"/> 件。
    /// </summary>
    /// <summary>入力途中ではなく完結した語として解析する (語末の n を ん とみなす)。</summary>
    public RomajiAnalysis AnalyzeWord(string word)
    {
        var analysis = Analyze(word);
        return analysis.IsValid && analysis.Partial == "n" ? Analyze(word + "n") : analysis;
    }

    public IReadOnlyList<string> SpellingVariants(string canonical, int limit = 64)
    {
        var analysis = AnalyzeWord(canonical);
        if (!analysis.IsValid || analysis.Partial.Length > 0) return [canonical];

        var tokens = analysis.Tokens;
        var results = new List<string>();
        var builder = new StringBuilder();

        void Walk(int index)
        {
            if (results.Count >= limit) return;
            if (index == tokens.Count)
            {
                results.Add(builder.ToString());
                return;
            }
            var mark = builder.Length;
            foreach (var option in OptionsFor(tokens, index))
            {
                builder.Append(option);
                Walk(index + 1);
                builder.Length = mark;
            }
        }

        Walk(0);
        if (!results.Contains(canonical)) results.Insert(0, canonical);
        return results;
    }

    private static IEnumerable<string> OptionsFor(IReadOnlyList<RomajiToken> tokens, int index)
    {
        var token = tokens[index];
        var next = index + 1 < tokens.Count ? tokens[index + 1] : (RomajiToken?)null;
        switch (token.Kana)
        {
            case "ん":
                yield return "nn";
                // 語末の ん は n 一つで書かれることが多い (gohan, nihon)。
                if (next is null) yield return "n";
                // 子音 (な行・や行以外) の前なら単独の n でも ん になる。
                if (next is { } n && n.Kana != "っ" && n.Romaji.Length > 0 && IsConsonant(n.Romaji[0]) && n.Romaji[0] is not ('n' or 'y'))
                    yield return "n";
                // ヘボン式の「ん + な行」は n 一つで書かれる (konnichiwa)。
                if (next is { } nn && nn.Romaji.Length > 0 && nn.Romaji[0] == 'n')
                    yield return "n";
                yield break;
            case "っ":
                if (next is { } following && KanaToSpellings.TryGetValue(following.Kana, out var spellings))
                {
                    foreach (var first in spellings.Select(s => s[0]).Distinct())
                    {
                        if (IsConsonant(first) && first != 'n') yield return first.ToString();
                    }
                }
                else yield return token.Romaji;
                yield break;
            default:
                if (KanaToSpellings.TryGetValue(token.Kana, out var options))
                {
                    foreach (var option in options) yield return option;
                }
                else yield return token.Romaji;
                yield break;
        }
    }
}

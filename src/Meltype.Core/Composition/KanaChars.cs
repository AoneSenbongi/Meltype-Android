// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

namespace Meltype.Composition;

/// <summary>かな・カタカナ・漢字の文字種の判定と、ひらがな ⇔ カタカナの変換。複数の場所に同じ式を書かないためにまとめた。</summary>
internal static class KanaChars
{
    private const int KatakanaOffset = 0x60;

    public static bool IsHiragana(char c) => c is >= 'ぁ' and <= 'ゖ';

    /// <summary>ひらがな、または長音符 (ー)。読みとして使える文字。</summary>
    public static bool IsHiraganaOrLongMark(char c) => c is >= 'ぁ' and <= 'ゖ' or 'ー';

    public static bool IsKatakana(char c) => c is >= 'ァ' and <= 'ヶ';

    public static bool IsKanji(char c) => c is >= '㐀' and <= '䶿' or >= '一' and <= '鿿' or '々';

    /// <summary>漢字かカタカナ。かな (助詞・送り仮名) だけでない語かを見るのに使う。</summary>
    public static bool IsKanjiOrKatakana(char c) => IsKanji(c) || IsKatakana(c);

    public static string ToHiragana(string text) => new(text.Select(c => IsKatakana(c) ? (char)(c - KatakanaOffset) : c).ToArray());

    public static string ToKatakana(string text) => new(text.Select(c => IsHiragana(c) ? (char)(c + KatakanaOffset) : c).ToArray());

    // 大⇔小キーで使う、通常文字と小書き文字の対応。
    private static readonly Dictionary<char, char> SmallToLarge = new()
    {
        ['ぁ'] = 'あ', ['ぃ'] = 'い', ['ぅ'] = 'う', ['ぇ'] = 'え', ['ぉ'] = 'お',
        ['っ'] = 'つ', ['ゃ'] = 'や', ['ゅ'] = 'ゆ', ['ょ'] = 'よ', ['ゎ'] = 'わ',
        ['ゕ'] = 'か', ['ゖ'] = 'け',
        ['ァ'] = 'ア', ['ィ'] = 'イ', ['ゥ'] = 'ウ', ['ェ'] = 'エ', ['ォ'] = 'オ',
        ['ッ'] = 'ツ', ['ャ'] = 'ヤ', ['ュ'] = 'ユ', ['ョ'] = 'ヨ', ['ヮ'] = 'ワ',
        ['ヵ'] = 'カ', ['ヶ'] = 'ケ',
    };

    private static readonly Dictionary<char, char> LargeToSmall = SmallToLarge.ToDictionary(pair => pair.Value, pair => pair.Key);

    public static char? ToLarge(char c) => SmallToLarge.TryGetValue(c, out var large) ? large : null;

    public static char? ToSmall(char c) => LargeToSmall.TryGetValue(c, out var small) ? small : null;

    // 大⇔小キーのタップ循環で使う、濁音・半濁音・清音の対応。_text の単位はひらがな。
    private const string Voiced = "がぎぐげござじずぜぞだぢづでどばびぶべぼゔ";
    private const string VoicedBase = "かきくけこさしすせそたちつてとはひふへほう";
    private const string SemiVoiced = "ぱぴぷぺぽ";
    private const string SemiVoicedBase = "はひふへほ";

    /// <summary>濁音 (ば) を半濁音 (ぱ) にする。は行以外は null。</summary>
    public static char? ToHandakuten(char c)
    {
        var index = Voiced.IndexOf(c);
        if (index < 0) return null;
        var baseIndex = SemiVoicedBase.IndexOf(VoicedBase[index]);
        return baseIndex < 0 ? null : SemiVoiced[baseIndex];
    }

    /// <summary>半濁音 (ぱ) を清音 (は) にする。</summary>
    public static char? ToBaseFromHandakuten(char c)
    {
        var index = SemiVoiced.IndexOf(c);
        return index < 0 ? null : SemiVoicedBase[index];
    }

    /// <summary>濁音 (が) を清音 (か) にする。</summary>
    public static char? ToBaseFromVoiced(char c)
    {
        var index = Voiced.IndexOf(c);
        return index < 0 ? null : VoicedBase[index];
    }
}

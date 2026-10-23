// SPDX-License-Identifier: GPL-3.0-or-later
namespace Meltype.AndroidCore;

/// <summary>
/// GODAN の「かな/A」が、未確定の文字を かな と アルファベット のどちらへ向けるかの判定。
/// ライブ変換では未確定の表示がかなではなく漢字だけになる (nihongo → 日本語) ので、
/// かなだけでなく漢字も日本語として数える。日本語の文字が無い (英字だけの) ときだけ かな へ向ける。
/// </summary>
public static class KanaAlphabetSwitch
{
    /// <summary>未確定の文字をアルファベットへ向けるか。日本語の文字を 1 つでも含めば true。</summary>
    public static bool ToAlphabet(string? text) => text is not null && text.Any(IsJapanese);

    /// <summary>ひらがな・カタカナ・半角カタカナ・長音符・繰り返し記号・漢字。</summary>
    public static bool IsJapanese(char c) =>
        c is >= 'ぁ' and <= 'ゖ'   // ひらがな
        or 'ー'                     // 長音符
        or >= 'ァ' and <= 'ヶ'      // カタカナ
        or >= 'ヽ' and <= 'ヾ'      // 繰り返し記号
        or '々' or '〆'
        or >= '㐀' and <= '䶿'       // 漢字 (拡張A)
        or >= '一' and <= '鿿'      // 漢字
        or >= 'ｦ' and <= 'ﾟ';       // 半角カタカナ
}

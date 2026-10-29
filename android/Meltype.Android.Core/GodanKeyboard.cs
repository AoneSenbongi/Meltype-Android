// SPDX-License-Identifier: GPL-3.0-or-later
namespace Meltype.AndroidCore;

/// <summary>GODAN キーボードのフリック方向。</summary>
public enum GodanDirection { Tap, Left, Up, Right, Down }

/// <summary>大⇔小キーの操作。Input 以外はローマ字ではなくエンジンへ直接働きかける。</summary>
public enum GodanActionKind { Input, SmallLarge, Dakuten, Handakuten, CycleKana }

/// <summary>
/// GODAN の 1 つのキー操作。Input は Meltype のローマ字入力へ渡す文字列、
/// Label はキー上やフリック中に表示する文字。
/// Literal が true の操作はローマ字として読まず、そのまま確定する (「」)。
/// </summary>
public sealed record GodanAction(string Input, string Label, bool Literal = false, bool Hint = true, GodanActionKind Kind = GodanActionKind.Input);

/// <summary>GODAN の 1 キー。Label は通常表示、Tap はタップしたときの操作。</summary>
public sealed record GodanKey(
    string Label,
    GodanAction Tap,
    GodanAction? Left = null,
    GodanAction? Up = null,
    GodanAction? Right = null,
    GodanAction? Down = null)
{
    public GodanAction? Action(GodanDirection direction) => direction switch
    {
        GodanDirection.Left => Left,
        GodanDirection.Up => Up,
        GodanDirection.Right => Right,
        GodanDirection.Down => Down,
        _ => Tap,
    };
}

/// <summary>
/// Gboard の GODAN 配列 (5 段 3 列)。左列が母音、中央と右の 2 列が子音。
/// タップした文字をローマ字として入力し、フリックで濁音・半濁音・数字・記号を入力する。
/// 母音の上フリックは「ん」、左フリックは「っ」、右フリックは小書きの「ゃぃゅぇょ」を続ける。
/// 中央列の K/S/T は両手で持ったときに左の母音キーと指がぶつかるため、左フリックを持たない。
/// </summary>
public static class GodanKeyboard
{
    public const int ColumnCount = 3;
    public const int RowCount = 5;

    // 下段の中央は Gboard では言語切り替えキー。Meltype では QWERTY へ戻すキーに使う。
    public static readonly GodanKey?[][] Rows =
    [
        [new("A", new("a", "A"), Up: new("ann", "ん", Hint: false), Left: new("axtu", "っ", Hint: false), Right: new("ya", "ゃ", Hint: false), Down: new("1", "1")),
         new("K", new("k", "K"), Up: new("q", "Q"), Right: new("g", "G"), Down: new("2", "2")),
         new("H", new("h", "H"), Left: new("p", "P"), Up: new("f", "F"), Right: new("b", "B"), Down: new("3", "3"))],

        [new("I", new("i", "I"), Up: new("inn", "ん", Hint: false), Left: new("ixtu", "っ", Hint: false), Right: new("yi", "ぃ", Hint: false), Down: new("4", "4")),
         new("S", new("s", "S"), Up: new("j", "J"), Right: new("z", "Z"), Down: new("5", "5")),
         new("M", new("m", "M"), Left: new("/", "/"), Up: new("l", "L"), Right: new("-", "-"), Down: new("6", "6"))],

        [new("U", new("u", "U"), Up: new("unn", "ん", Hint: false), Left: new("uxtu", "っ", Hint: false), Right: new("yu", "ゅ", Hint: false), Down: new("7", "7")),
         new("T", new("t", "T"), Up: new("c", "C"), Right: new("d", "D"), Down: new("8", "8")),
         new("Y", new("y", "Y"), Left: new("(", "("), Up: new("x", "X"), Right: new(")", ")"), Down: new("9", "9"))],

        [new("E", new("e", "E"), Up: new("enn", "ん", Hint: false), Left: new("extu", "っ", Hint: false), Right: new("ye", "ぇ", Hint: false)),
         new("N", new("n", "N"), Left: new(":", ":"), Up: new("nn", "ん", Hint: false), Right: new(".", "."), Down: new("0", "0")),
         new("R", new("r", "R"), Left: new("。", "。", Literal: true), Up: new("？", "?", Literal: true), Right: new("！", "!", Literal: true), Down: new("、", "、", Literal: true))],

        [new("O", new("o", "O"), Up: new("onn", "ん", Hint: false), Left: new("oxtu", "っ", Hint: false), Right: new("yo", "ょ", Hint: false)),
         null,
         new("W", new("w", "W"), Left: new("「", "「", Literal: true), Up: new("v", "V"), Right: new("」", "」", Literal: true))],
    ];

    /// <summary>キーの読み上げ用の説明。タップと、ある方向のフリックをすべて含める。</summary>
    public static string Describe(GodanKey key)
    {
        var parts = new List<string> { key.Label, "タップで" + key.Tap.Label };
        if (key.Left is { } left) parts.Add("左フリック " + left.Label);
        if (key.Up is { } up) parts.Add("上フリック " + up.Label);
        if (key.Right is { } right) parts.Add("右フリック " + right.Label);
        if (key.Down is { } down) parts.Add("下フリック " + down.Label);
        return string.Join("、", parts);
    }
}

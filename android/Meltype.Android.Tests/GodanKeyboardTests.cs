// SPDX-License-Identifier: GPL-3.0-or-later
using Meltype.AndroidCore;

internal static class GodanKeyboardTests
{
    public static void Run()
    {
        if (GodanKeyboard.Rows.Length != GodanKeyboard.RowCount || GodanKeyboard.Rows.Any(row => row.Length != GodanKeyboard.ColumnCount))
            throw new Exception("GODAN must be a 3x5 grid.");
        var keys = GodanKeyboard.Rows.SelectMany(row => row).Where(key => key != null).Select(key => key!).ToArray();
        if (keys.Length != 14) throw new Exception($"GODAN must have 14 letter keys, got {keys.Length}.");
        if (GodanKeyboard.Rows[4][1] != null) throw new Exception("The bottom middle cell must stay free for the layout switch.");

        string[] labels = ["A", "K", "H", "I", "S", "M", "U", "T", "Y", "E", "N", "R", "O", "W"];
        if (!GodanKeyboard.Rows.SelectMany(row => row).Where(key => key != null).Select(key => key!.Label).SequenceEqual(labels))
            throw new Exception("GODAN key positions changed.");

        // Gboard の GODAN と同じ並びとフリック。Input は Meltype のローマ字入力へ渡す綴り。
        var expected = new Dictionary<string, (string Tap, string? Left, string? Up, string? Right, string? Down)>
        {
            ["A"] = ("a", "axtu", "ann", "ya", "1"),
            ["K"] = ("k", null, "q", "g", "2"),
            ["H"] = ("h", "p", "f", "b", "3"),
            ["I"] = ("i", "ixtu", "inn", "yi", "4"),
            ["S"] = ("s", null, "j", "z", "5"),
            ["M"] = ("m", "/", "l", "-", "6"),
            ["U"] = ("u", "uxtu", "unn", "yu", "7"),
            ["T"] = ("t", null, "c", "d", "8"),
            ["Y"] = ("y", "(", "x", ")", "9"),
            ["E"] = ("e", "extu", "enn", "ye", null),
            ["N"] = ("n", ":", "nn", ".", "0"),
            ["R"] = ("r", "。", "？", "！", "、"),
            ["O"] = ("o", "oxtu", "onn", "yo", null),
            ["W"] = ("w", "「", "v", "」", null),
        };
        foreach (var key in keys)
        {
            var want = expected[key.Label];
            string?[] actual = [key.Tap.Input, key.Left?.Input, key.Up?.Input, key.Right?.Input, key.Down?.Input];
            string?[] wanted = [want.Tap, want.Left, want.Up, want.Right, want.Down];
            if (!actual.SequenceEqual(wanted))
                throw new Exception($"GODAN flicks differ at {key.Label}: expected {string.Join('/', wanted)}, got {string.Join('/', actual)}");
            if (GodanKeyboard.Describe(key).Length == 0) throw new Exception("GODAN key description is empty.");
            foreach (var action in new[] { key.Tap, key.Left, key.Up, key.Right, key.Down }.Where(action => action != null))
                if (action!.Input.Length == 0 || action.Label.Length == 0)
                    throw new Exception($"GODAN key {key.Label} has an empty action.");
        }

        // 中央列の K/S/T は左フリックを持たない (両手持ちで母音キーと指がぶつかるため)。
        foreach (var label in new[] { "K", "S", "T" })
            if (keys.Single(key => key.Label == label).Left != null)
                throw new Exception($"GODAN {label} must not have a left flick.");

        // 下フリックは 1〜9 と 0 を 1 回ずつ。E/O は数字を持たない。
        var numbers = keys.SelectMany(key => new[] { key.Left, key.Up, key.Right, key.Down })
            .Where(action => action is { Input.Length: 1 } && char.IsAsciiDigit(action.Input[0]))
            .Select(action => action!.Input).OrderBy(value => value).ToArray();
        if (!numbers.SequenceEqual(["0", "1", "2", "3", "4", "5", "6", "7", "8", "9"]))
            throw new Exception("GODAN number flicks must cover 0-9 once: " + string.Concat(numbers));
        if (keys.Single(key => key.Label == "E").Down != null || keys.Single(key => key.Label == "O").Down != null)
            throw new Exception("GODAN E/O must not have number flicks.");

        // 母音のフリックは「ん」「っ」と小書き母音。右フリックは子音キーの後ろに続ける綴り。
        if (keys.Single(key => key.Label == "A").Up!.Input != "ann" ||
            keys.Single(key => key.Label == "A").Left!.Input != "axtu" ||
            keys.Single(key => key.Label == "A").Right!.Input != "ya" ||
            keys.Single(key => key.Label == "I").Right!.Input != "yi" ||
            keys.Single(key => key.Label == "U").Right!.Input != "yu" ||
            keys.Single(key => key.Label == "E").Right!.Input != "ye" ||
            keys.Single(key => key.Label == "O").Right!.Input != "yo")
            throw new Exception("GODAN vowel flicks differ from Gboard.");
        if (!keys.Single(key => key.Label == "W").Left!.Literal || !keys.Single(key => key.Label == "W").Right!.Literal)
            throw new Exception("GODAN corner brackets must be inserted literally.");

        // Gboard は母音キーの上/左/右と N の上をキーに表示しない (動作は残す)。
        foreach (var label in new[] { "A", "I", "U", "E", "O" })
        {
            var vowel = keys.Single(key => key.Label == label);
            if (new[] { vowel.Up, vowel.Left, vowel.Right }.Any(action => action is not { Hint: false }))
                throw new Exception($"GODAN {label} must not display the vowel flick hints.");
        }
        if (keys.Single(key => key.Label == "N").Up is not { Hint: false })
            throw new Exception("GODAN N must not display the ん flick hint.");
        if (keys.Single(key => key.Label == "K").Up is not { Hint: true } ||
            keys.Single(key => key.Label == "M").Right is not { Hint: true, Label: "-" })
            throw new Exception("GODAN documented flick hints must stay visible.");
        var rKey = keys.Single(key => key.Label == "R");
        if (new[] { rKey.Left, rKey.Up, rKey.Right, rKey.Down }.Any(action => action is not { Literal: true }))
            throw new Exception("GODAN R flicks must insert the Japanese punctuation literally.");

        var description = GodanKeyboard.Describe(keys.Single(key => key.Label == "K"));
        foreach (var part in new[] { "K", "タップで", "Q", "G", "2" })
            if (!description.Contains(part, StringComparison.Ordinal))
                throw new Exception($"GODAN description is missing {part}: {description}");

        Console.WriteLine("PASS: GODAN 3x5 layout, tap/flick table, vowel flicks, number row, punctuation and descriptions");

        // 「かな/A」: ライブ変換の未確定は漢字だけになる (nihongo → 日本語) ので、漢字も日本語として英字へ向ける。
        foreach (var japanese in new[] { "にほんご", "日本語", "カタカナ", "ｶﾀｶﾅ", "今日は日本語", "ー", "々" })
            if (!KanaAlphabetSwitch.ToAlphabet(japanese))
                throw new Exception($"かな/A must send Japanese preedit to the alphabet: {japanese}");
        foreach (var latin in new[] { "nihongo", "abc", "123", "", "日本語abc" })
        {
            var wantJapanese = latin == "日本語abc";
            if (KanaAlphabetSwitch.ToAlphabet(latin) != wantJapanese)
                throw new Exception($"かな/A misclassified the preedit: {latin}");
        }
        if (KanaAlphabetSwitch.ToAlphabet(null)) throw new Exception("かな/A must ignore the empty preedit.");

        // 1 回のタップで英字になること (F10 を 2 回押させない)。
        var kanaSession = new AndroidInputSession(new FakeConverter(), _ => [], (_, _, _) => { });
        foreach (var c in "nihongo") kanaSession.Character(c);
        if (kanaSession.View?.Text != "日本語") throw new Exception("かな/A test precondition is broken: " + kanaSession.View?.Text);
        kanaSession.Key(0x79); // F10 = 英字
        if (kanaSession.View?.Text != "nihongo")
            throw new Exception("かな/A must switch a kanji preedit to the alphabet in one tap: " + kanaSession.View?.Text);
        Console.WriteLine("PASS: GODAN かな/A switches 日本語 to nihongo in one tap");
    }
}

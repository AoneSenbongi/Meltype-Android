// SPDX-License-Identifier: GPL-3.0-or-later
using Meltype.AndroidCore;
internal static class NumericKeyboardTests
{
    public static void Run()
    {
        void Expect(int type, string? bottom)
        {
            var rows = NumericKeyboard.Rows(type);
            if (bottom == null) { if (rows != null) throw new Exception("Text field got a number pad"); return; }
            if (rows == null || !rows.SequenceEqual(new[] { "123", "456", "789", bottom }))
                throw new Exception("Wrong numeric layout: " + type);
        }
        Expect(1, null); Expect(0x81, null); Expect(4, null); Expect(0, null);
        Expect(2, " 0 "); Expect(0x12, " 0 ");
        Expect(0x1002, "-0 "); Expect(0x2002, " 0."); Expect(0x3012, "-0.");
        Expect(3, "*0#"); Expect(0x3001, null); Expect(0x3003, "*0#");
        foreach (var type in new[] { 1, 2, 0x1002, 0x2002, 3, 4 })
            if (!ClipboardInputPolicy.Allows(type)) throw new Exception("Clipboard unavailable in ordinary field: " + type);
        foreach (var type in new[] { 0x12, 0x81, 0x91, 0xe1 })
            if (ClipboardInputPolicy.Allows(type)) throw new Exception("Clipboard offered in password field: " + type);
        // Transition back to ordinary text must not keep the numeric layout.
        Expect(2, " 0 "); Expect(1, null);
        Console.WriteLine("PASS: number, signed, decimal, numeric password, phone and return to text layouts");
    }
}

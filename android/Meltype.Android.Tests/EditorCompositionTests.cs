// SPDX-License-Identifier: GPL-3.0-or-later
using Meltype.AndroidCore;

internal static class EditorCompositionTests
{
    public static void Run()
    {
        foreach (var action in new[] { "typing", "confirm" })
        {
            var edits = new List<(int Op, string Text)>();
            var session = new AndroidInputSession(new FakeConverter(), _ => [], (op, text, _) => edits.Add((op, text)));
            foreach (var c in "nihongo") session.Character(c);
            if (session.View is null) throw new Exception("Expected pending conversion before external send");
            // The app sends the visible text and clears its editor without ending the IME session.
            if (EditorComposition.ShouldReset(true, 0, 0, -1, -1)) session.Reset();
            edits.Clear();
            if (action == "typing") session.Character('a'); else session.Key(0x0D);
            var inserted = action == "typing" ? session.View?.Text ?? "" : string.Concat(edits.Where(e => e.Op == 1).Select(e => e.Text));
            var expected = action == "typing" ? "あ" : "";
            if (inserted != expected) throw new Exception($"External send + {action}: expected '{expected}', got '{inserted}'");
        }
        if (!EditorComposition.ShouldReset(true, 7, 7, -1, -1)) throw new Exception("External confirmation did not discard pending input");
        if (EditorComposition.ShouldReset(true, 7, 7, 0, 7)) throw new Exception("Normal composing update discarded input");
        if (EditorComposition.ShouldReset(false, 0, 0, -1, -1)) throw new Exception("Ordinary committed update reset the session");
        if (EditorComposition.ShouldReset(true, 2, 2, 0, 7)) throw new Exception("Cursor inside composition committed later clauses");
        if (EditorComposition.ShouldReset(true, 2, 5, 0, 7)) throw new Exception("Selection inside composition committed later clauses");
        if (!EditorComposition.ShouldReset(true, 8, 8, 0, 7)) throw new Exception("Cursor outside composition did not reset");
        Console.WriteLine("PASS: external send then typing/Enter, external confirmation, normal composition and cursor selection");
    }
}

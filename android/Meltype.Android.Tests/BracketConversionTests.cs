using Meltype.AndroidCore;
using Meltype.Composition;
internal static class BracketConversionTests
{
    public static void Run()
    {
        foreach (var english in new[] { false, true })
        foreach (var quotes in new[] { false, true })
        {
            var ops = new List<(int Op, string Text)>();
            var session = new AndroidInputSession(new FakeConverter(), _ => [], (op,text,_) => ops.Add((op,text)),()=>new Meltype.Config.InputPreferences{FullWidthParentheses=false});
            session.SetEnglish(english);
            session.BeginBracketConversion(quotes);
            var candidates = quotes ? new[] { "「」", "｢｣", "『』", "【】" } : new[] { "()", "（）", "{}", "｛｝", "[]", "［］" };
            if (!session.ChoosingBrackets || session.View?.Text != candidates[0] || session.View?.Candidates.SequenceEqual(candidates) != true || ops.Any(o=>o.Op==7)) throw new Exception("Bracket preview committed early");
            session.Character(' ');
            if (session.View?.SelectedIndex != 1 || session.View.Text != candidates[1]) throw new Exception("Space did not cycle bracket preview");
            session.Select(candidates.Length - 1);
            if (session.View != null || session.ChoosingBrackets || ops.Last() != (7,candidates[^1])) throw new Exception("Candidate did not commit pair with inside cursor");
            session.BeginBracketConversion(quotes); session.Key(0x0D);
            if (ops.Last() != (7,candidates[0])) throw new Exception("Enter did not confirm pair");
            session.BeginBracketConversion(quotes); ops.Clear(); session.Key(0x08);
            if (session.View != null || ops.Any(o=>o.Op==7)) throw new Exception("Backspace did not cancel bracket");
            session.BeginBracketConversion(quotes); ops.Clear(); session.Character('a');
            if (ops[0] != (7,candidates[0]) || session.ChoosingBrackets) throw new Exception("Typing did not commit selected bracket before inner text");
            session.BeginBracketConversion(quotes); session.Key(0x25);
            if (session.View?.Text != candidates[^1]) throw new Exception("Left did not wrap bracket candidates");
            session.Key(0x27);
            if (session.View?.Text != candidates[0]) throw new Exception("Right did not wrap bracket candidates");
            session.BeginBracketConversion(quotes); session.Reset();
            if (session.ChoosingBrackets || session.View != null) throw new Exception("Reset retained bracket candidates");
        }
        Console.WriteLine("PASS: bracket groups, preview, Space, candidate, Enter, cancel, typing, reset, English mode");
    }
}

// SPDX-License-Identifier: GPL-3.0-or-later
using Meltype.AndroidCore;
using Meltype.Composition;
internal static class ConversionSelectionTests
{
    public static void Run()
    {
        var english = new AndroidInputSession(new Plain(), _ => [], (_, _, _) => { });
        foreach (var c in "open") english.Character(c);
        if (english.View?.Candidates.Contains("オペン") != true) throw new Exception("English input has no Japanese live candidates: " + english.View?.Text + " / " + string.Join("|",english.View?.Candidates ?? []));
        english.Reset();
        foreach (var c in "kakao") english.Character(c);
        if (english.View?.Candidates.Contains("カカオ") != true) throw new Exception("kakao has no katakana candidate");
        var chosen = "";
        english = new AndroidInputSession(new Plain(), _ => [], (op, text, _) => { if (op == 1) chosen += text; });
        foreach (var c in "kakao") english.Character(c);
        english.Select(english.View!.Candidates.ToList().IndexOf("カカオ"));
        if (chosen != "カカオ" || english.View != null) throw new Exception("kakao candidate selection did not commit katakana");
        foreach(var word in new[]{"open","data","close","camera","code","type","line","user","test","save","file"})
        {
            foreach(var action in new[]{"enter","space","blur"})
            {
                var output="";
                var session=new AndroidInputSession(new Plain(),_=>[],(op,text,_)=>{if(op==1)output+=text;});
                foreach(var c in word)session.Character(c);
                if(session.View?.Text != word) throw new Exception("Expected English live preview: "+word+"="+session.View?.Text);
                if(action=="enter")session.Key(0x0D);
                else if(action=="space")session.Character(' ');
                else session.Commit();
                var expected=word+(action=="space"?" ":"");
                if(output!=expected)throw new Exception("Visible English changed on "+action+": "+word+" -> "+output);
            }
        }
        foreach(var action in new[]{"enter","space","blur"})
        {
            var output="";
            var session=new AndroidInputSession(new Plain(),_=>[],(op,text,_)=>{if(op==1)output+=text;});
            foreach(var c in "kyouhaopen")session.Character(c);
            var visible=session.View!.Text;
            if(!visible.EndsWith("open"))throw new Exception("Mixed preview missing English: "+visible);
            if(action=="enter")session.Key(0x0D);else if(action=="space")session.Space(false);else session.Commit();
            if(output!=visible+(action=="space"?" ":""))throw new Exception("Mixed English preview changed on "+action+": "+visible+" -> "+output);
        }
        Console.WriteLine("PASS: visible English preserved on Enter, Space and commit for eleven words");
    }
    public static void SpaceTests()
    {
        foreach(var raw in new[]{"", "open", "nihongo"})
        {
            var output="";
            var session=new AndroidInputSession(new Plain(),_=>[],(op,text,_)=>{if(op==1)output+=text;});
            foreach(var c in raw)session.Character(c);
            var expected=(raw=="nihongo"?"にほんご":raw)+"　";
            session.Space(fullWidth:true);
            if(output!=expected || session.View!=null)throw new Exception("Full-width space lost pending text: "+output);
        }
        var selected="";
        var input=new AndroidInputSession(new Plain(),_=>[],(op,text,_)=>{if(op==1)selected+=text;});
        foreach(var c in "nihongo")input.Character(c);
        input.Space(false); input.Space(false); // Select katakana.
        var visible=input.View!.Text;
        input.Space(true);
        if(selected!=visible+"　")throw new Exception("Full-width space lost selected candidate");
        selected="";input.Reset();input.SetEnglish(true);input.Space(true);
        if(selected!="　")throw new Exception("Full-width space unavailable in English mode");
        var shift=new KeyboardShift();shift.Toggle();shift.Consume();
        if(shift.Active)throw new Exception("One-shot Shift survived Space");
        shift.Toggle();shift.Toggle();shift.Consume();
        if(!shift.Locked)throw new Exception("Space cleared Caps Lock");
        Console.WriteLine("PASS: full-width space with empty, English, Japanese and selected conversion; Shift and Caps Lock");
    }
    public static void CandidateTests()
    {
        foreach(var raw in new[]{"open","data","same","time","name","note","take","home","more","site","game","make","close","camera","code","type","line","user","test","save","file"})
        {
            var output="";
            var session=new AndroidInputSession(new Plain(),_=>[],(op,text,_)=>{if(op==1)output+=text;});
            foreach(var c in raw)session.Character(c);
            var candidates=session.View?.Candidates.ToList();
            if(candidates?.Contains(raw)==true)session.Select(candidates.IndexOf(raw));
            else session.Key(0x0D);
            if(output!=raw)throw new Exception("Explicit English candidate changed: "+raw+"="+output);
            output="";session.Reset();foreach(var c in raw)session.Character(c);session.Space(false);
            if(session.View is {Converting:true} view)
            {
                session.Space(false);
                var chosen=session.View!.Text;
                session.Key(0x0D);
                if(output!=chosen)throw new Exception("Selected conversion changed on Enter: "+raw);
            }
        }
        Console.WriteLine("PASS: explicit English candidates and selected conversion survive confirmation for 21 words");
    }
    public static void ResizeTests()
    {
        var operations = new List<int>();
        var input = new AndroidInputSession(new Plain(), _ => [], (op, _, _) => operations.Add(op));
        foreach (var c in "nihongo") input.Character(c);
        input.Key(0x25, shift: true);
        if (input.View?.Clauses is not { Count: 2 } clauses || clauses[0] != "にほん" || clauses[1] != "ご")
            throw new Exception("Shift+Left did not shrink live conversion: " + input.View?.Text);
        input.Key(0x27, shift: true);
        if (input.View?.Clauses is not { Count: 1 } || input.View.Text != "にほんご")
            throw new Exception("Shift+Right did not restore conversion boundary");
        input.Key(0x25, shift: true);
        var before = input.View!.Clauses!.ToArray();
        input.Key(0x27);
        if (!input.View!.Clauses!.SequenceEqual(before) || input.View.SelectedClause != 1)
            throw new Exception("Unmodified arrow resized instead of selecting a clause");
        input.Key(0x25); input.Key(0x25, shift: true); input.Key(0x25, shift: true);
        input.Key(0x25, shift: true); // Cannot shrink the clause below one character.
        if (input.View?.Clauses?[0] != "に" || operations.Any(op => op is 1 or 5 or 6))
            throw new Exception("Repeated resize confirmed input, escaped editor, or shrank below one character");
        input.Commit(); operations.Clear(); input.Key(0x25, shift: true);
        if (!operations.SequenceEqual(new[] { 5 })) throw new Exception("Shift with empty composition lost cursor movement");
        input.Character('a');
        if (input.View?.Text != "あ") throw new Exception("Resize leaked Shift to following input");
        Console.WriteLine("PASS: live/converting clause shrink/expand, repeated resize, limits, unmodified arrows and scoped Shift");
    }
    sealed class Plain : IKanjiConverter
    {
        public string? Convert(string text)=>text;
        public IReadOnlyList<ConversionClause>? ConvertClauses(string text,string? context=null)=>[new(text,text)];
    }
}

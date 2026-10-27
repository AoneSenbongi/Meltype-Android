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
                var session=new AndroidInputSession(new Plain(),_=>[],(op,text,_)=>{if(op==1)output+=text;},()=>new Meltype.Config.InputPreferences{EnterCommitsAsShown=true});
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
            var session=new AndroidInputSession(new Plain(),_=>[],(op,text,_)=>{if(op==1)output+=text;},()=>new Meltype.Config.InputPreferences{EnterCommitsAsShown=true});
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
    public static void ArrowTests()
    {
        foreach (var begin in new[] { "live", "selected", "resized" })
        {
            var operations = new List<int>();
            var input = new AndroidInputSession(new Split(), _ => [], (op, _, _) => operations.Add(op));
            foreach (var c in "nihongowobenkyousuru") input.Character(c);
            if (input.View?.Clauses is not { Count: 4 }) throw new Exception("Expected four conversion clauses");
            if (begin != "live") input.SelectPosition(input.View.Clauses.Take(3).Sum(c => c.Length));
            if (begin == "resized") input.Key(0x25, shift: true);
            var clauses = input.View!.Clauses!.ToArray();
            var text = input.View.Text;
            operations.Clear();
            for (var expected = 2; expected >= 0; expected--)
            {
                input.Key(0x25);
                if (input.View?.SelectedClause != expected)
                    throw new Exception($"{begin}: Left expected clause {expected}, got {input.View?.SelectedClause}");
            }
            input.Key(0x25);
            if (input.View?.SelectedClause != 0) throw new Exception("Left wrapped at first clause");
            for (var expected = 1; expected < clauses.Length; expected++)
            {
                input.Key(0x27);
                if (input.View?.SelectedClause != expected) throw new Exception("Right skipped a clause");
            }
            input.Key(0x27);
            if (input.View?.SelectedClause != clauses.Length - 1 || input.View.Text != text ||
                !input.View.Clauses!.SequenceEqual(clauses) || operations.Any(op => op is 1 or 5 or 6))
                throw new Exception("Arrow movement changed text, boundaries, committed, or escaped editor");
        }
        Console.WriteLine("PASS: live, selected and resized clauses move one at a time with arrows and stop at edges");
    }
    sealed class Split : IKanjiConverter
    {
        public string? Convert(string text) => text;
        public IReadOnlyList<ConversionClause>? ConvertClauses(string text, string? context = null) =>
            text == "にほんごをべんきょうする" ? [new("にほんご", "日本語"), new("を", "を"), new("べんきょう", "勉強"), new("する", "する")] : [new(text, text)];
    }
    public static void InitialResizeTests()
    {
        foreach(var arrow in new[]{0x27,0x25})
        {
            var operations=new List<int>();
            var input=new AndroidInputSession(new Split(),_=>[],(op,_,_)=>operations.Add(op));
            foreach(var c in "nihongowobenkyousuru")input.Character(c);
            var before=input.View!.Clauses!.ToArray();
            if(input.View.SelectedClause!=3)throw new Exception("Live conversion must initially select the last clause");
            operations.Clear();input.Key(arrow,shift:true);
            if(input.View?.SelectedClause!=3 || !input.View.Clauses!.Take(3).SequenceEqual(before.Take(3)))
                throw new Exception("Initial Shift+arrow resized or selected an earlier clause: "+arrow+" selected="+input.View?.SelectedClause);
            if(arrow==0x27 && !input.View.Clauses!.SequenceEqual(before))
                throw new Exception("Initial Shift+Right changed the last clause with no following clause");
            if(arrow==0x25)
            {
                if(!input.View.Clauses!.Skip(3).SequenceEqual(new[]{"す","る"}))throw new Exception("Initial Shift+Left did not shrink the last clause");
                input.Key(0x27,shift:true);
                if(input.View.SelectedClause!=3 || !input.View.Clauses!.SequenceEqual(before))throw new Exception("Shift+Right did not restore the same last clause");
            }
            if(operations.Any(op=>op is 1 or 5 or 6))throw new Exception("Initial resize committed or moved the editor cursor");
        }
        Console.WriteLine("PASS: first Shift+Right/Left retains the rightmost live clause; resize limits and restoration preserve preceding clauses");
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
    public static void MisclassifiedEnglishTests()
    {
        foreach(var word in new[]{"camera","data","open"})
        {
            var committed="";
            var mistaken=new AndroidInputSession(new Plain(),_=>[],(op,text,_)=>{if(op==1)committed+=text;});
            foreach(var c in word)mistaken.Character(c);
            if(mistaken.View?.Text!=word)throw new Exception("English detection fixture changed: "+word);
            mistaken.Key(0x25,shift:true);mistaken.Key(0x27,shift:true);
            if(mistaken.View is not {Converting:true} corrected || corrected.Text==word || committed!="")
                throw new Exception("Misclassified English cannot be resized as Japanese: "+word+" text="+mistaken.View?.Text+" converting="+mistaken.View?.Converting+" committed="+committed);
            if(word=="camera")
            {
                var index=mistaken.View.Candidates.ToList().IndexOf("カメラ");
                if(index<0)throw new Exception("Japanese candidate missing after English resize");
                mistaken.Select(index);
                if(committed!="カメラ")throw new Exception("Japanese selection after English resize failed");
            }
        }
        var literal=new AndroidInputSession(new Plain(),_=>[],(_,_,_)=>{});literal.SetEnglish(true);
        foreach(var c in "camera")literal.Character(c);
        literal.Key(0x25,shift:true);literal.Key(0x27,shift:true);
        if(literal.View!=null)throw new Exception("Explicit English mode was converted by resize");
        Console.WriteLine("PASS: automatic English can resize into Japanese without committing; explicit English remains direct input");
    }
    sealed class Plain : IKanjiConverter
    {
        public string? Convert(string text)=>text;
        public IReadOnlyList<ConversionClause>? ConvertClauses(string text,string? context=null)=>[new(text,text)];
    }
}

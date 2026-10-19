using Meltype.AndroidCore;
using Meltype.Composition;
using System.Text;

static class UserDictionaryTests
{
    public static void Run()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".txt");
        try
        {
            var store = new AndroidUserDictionary(path);
            var json = "{\"JAJP_KEY\":[\"てすと\",\"あ\",\"\"],\"JAJP_VALUE\":[\"独自単語\",\"短い語\",\"\"],\"EN_KEY\":[\"てすと\"],\"EN_VALUE\":[\"独自単語\"]}";
            var result = store.Import(Encoding.UTF8.GetBytes(json));
            if (result.Added != 2 || store.Count != 2 || !store.Lookup("あ").Contains("短い語")) throw new Exception("Simeji import lost short or duplicate entries");
            if (store.Import(Encoding.UTF8.GetBytes(json)).Added != 0) throw new Exception("Repeated import duplicated entries");
            store.Add("カタカナ", "登録語");
            store.Add("あいさつ", "こんにちは\nよろしく");
            if (new AndroidUserDictionary(path).Lookup("あいさつ")[0] != "こんにちは\nよろしく") throw new Exception("Multiline phrase was lost");
            if (!new AndroidUserDictionary(path).Lookup("かたかな").Contains("登録語")) throw new Exception("Dictionary did not persist");
            var shown = "";
            var session = new AndroidInputSession(new Plain(), _ => [], (op,text,_) => { if(op == 0) shown=text; }, userDictionary: store);
            foreach(var c in "tesutoyou") session.Character(c);
            if (shown != "てすとよう") throw new Exception("Registered prefix overrode engine context: " + shown);
            session.Reset(); foreach(var c in "tesuto") session.Character(c);
            if (session.View?.Candidates.Contains("独自単語") != true) throw new Exception("Registered word is absent from candidates");
            var committed = "";
            session = new AndroidInputSession(new Plain(), _ => [], (op,text,_) => { if(op == 1) committed += text; }, userDictionary: store);
            foreach(var c in "tesuto") session.Character(c);
            session.Select(session.View!.Candidates.ToList().IndexOf("独自単語"));
            if(committed != "独自単語") throw new Exception("Selected registered word was not committed");
            store.Add("はし", "橋"); store.Add("かわ", "革"); store.Add("てすとよう", "独自の長い語");
            session = new AndroidInputSession(new Contextual(), _ => [], (_,_,_) => { }, userDictionary: store);
            foreach(var c in "hasiru") session.Character(c); session.Space(false);
            if(session.View?.Text != "走る") throw new Exception("Registered substring split a contextual word: " + session.View?.Text);
            session.Reset(); foreach(var c in "kawadeoyogu") session.Character(c);
            if(session.View?.Text != "川で泳ぐ") throw new Exception("Registered word beat sentence context: " + session.View?.Text);
            session.Reset(); foreach(var c in "kawa") session.Character(c); session.Space(false);
            if(session.View?.Text != "川" || session.View.Candidates.Contains("革") != true) throw new Exception("Engine result or alternate registered candidate missing");
            session.Reset(); foreach(var c in "tesutoyou") session.Character(c);
            if(session.View?.Text != "テスト用" || session.View.Candidates.Contains("独自の長い語") != true) throw new Exception("Exact registered phrase or contextual default missing");
            session.Reset(); foreach(var c in "a") session.Character(c);
            if (session.View?.Candidates.Contains("短い語") != true) throw new Exception("Short reading is absent from candidates");
            if(store.CompositionDictionary.Split("あいう") != null) throw new Exception("Short reading replaced a word prefix");
            var before = store.Count;
            try { store.Import(Encoding.UTF8.GetBytes("{\"JAJP_KEY\":[\"てすと\"],\"JAJP_VALUE\":[]} ")); throw new Exception("Malformed arrays accepted"); }
            catch (InvalidDataException) { }
            if(store.Count != before) throw new Exception("Failed import changed dictionary");
            var utf16 = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes("!Header\r\nゆにこーど\t文字コード\t名詞\r\n")).ToArray();
            if (store.Import(utf16).Added != 1) throw new Exception("UTF-16 dictionary import failed");
            var boundedCount = store.Count;
            var large = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(Enumerable.Range(0, 800)
                .Select(i => new UserWord("てすと" + i, new string('字', 250) + i)),
                new System.Text.Json.JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
            try { store.Import(large); throw new Exception("Oversized saved dictionary accepted"); }
            catch (InvalidDataException) { }
            if (store.Count != boundedCount || new AndroidUserDictionary(path).Count != boundedCount) throw new Exception("Failed save damaged existing words");
            Console.WriteLine("PASS: Simeji/short readings, deduplication, persistence, live conversion, candidates, invalid import");
        }
        finally { if(File.Exists(path)) File.Delete(path); }
    }
    sealed class Contextual : IKanjiConverter
    {
        public string? Convert(string text)=>string.Concat(ConvertClauses(text)!.Select(c=>c.Text));
        public IReadOnlyList<ConversionClause>? ConvertClauses(string text,string? context=null)=>text switch
        {
            "はしる"=>[new(text,"走る")],
            "かわでおよぐ"=>[new("かわ","川"),new("でおよぐ","で泳ぐ")],
            "かわ"=>[new(text,"川")],
            "てすとよう"=>[new("てすと","テスト"),new("よう","用")],
            _=>[new(text,text)]
        };
    }
    sealed class Plain : IKanjiConverter
    {
        public string? Convert(string text)=>text;
        public IReadOnlyList<ConversionClause>? ConvertClauses(string text,string? context=null)=>[new(text,text)];
    }
}

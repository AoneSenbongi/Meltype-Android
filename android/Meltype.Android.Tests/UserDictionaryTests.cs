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
            if (shown != "独自単語よう") throw new Exception("User dictionary is absent from live conversion: " + shown);
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
    sealed class Plain : IKanjiConverter
    {
        public string? Convert(string text)=>text;
        public IReadOnlyList<ConversionClause>? ConvertClauses(string text,string? context=null)=>[new(text,text)];
    }
}

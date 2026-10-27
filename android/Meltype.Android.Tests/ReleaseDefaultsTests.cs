using Meltype.Config;
using System.Text.Json;
using System.Text.Json.Nodes;
internal static class ReleaseDefaultsTests
{
    public static void Run()
    {
        var expected=JsonNode.Parse("""
{
  "Comma": "，",
  "Period": "．",
  "LearningEnabled": true,
  "CalculatorCandidates": true,
  "ConsonantAbbreviations": false,
  "ClipboardCandidates": false,
  "Predictions": true,
  "EnglishNamePredictions": true,
  "ShowCandidatesWhileTyping": true,
  "FullWidthParentheses": true,
  "CursorIntoBrackets": true,
  "SquareBrackets": false,
  "FullWidthSlash": false,
  "LaughterAfterKana": true,
  "FullWidthSpaceByDefault": false,
  "ShiftSpaceHalfWidth": false,
  "EnterCommitsAsShown": false,
  "FunctionKeysAffectSelectedClause": false,
  "AlphanumericKeyStartsAsTyped": false,
  "EnglishFirstInput": false,
  "ContextualLearning": false,
  "ShowTypedKeys": false,
  "LocalZenz": true
}
""");
        var actual=JsonSerializer.SerializeToNode(new InputPreferences());
        if(!JsonNode.DeepEquals(expected,actual))throw new Exception("Fresh defaults differ from the user-selected device settings");
        var folder=Path.Combine(Path.GetTempPath(),"meltype-defaults-"+Guid.NewGuid());Directory.CreateDirectory(folder);
        try
        {
            var file=Path.Combine(folder,"preferences.json");var store=new InputPreferenceStore(file);
            if(!JsonNode.DeepEquals(expected,JsonSerializer.SerializeToNode(store.Current)))throw new Exception("No-file startup ignored release defaults");
            File.WriteAllText(file,"{\"LocalZenz\":false,\"FullWidthParentheses\":false,\"LearningEnabled\":false,\"ShowTypedKeys\":true}");
            var saved=store.Current;
            if(saved.LocalZenz||saved.FullWidthParentheses||saved.LearningEnabled||!saved.ShowTypedKeys)throw new Exception("Existing explicit preferences were overwritten");
            File.WriteAllText(file,"{\"ShowTypedKeys\":true}");
            if(store.Current.LocalZenz||!store.Current.ShowTypedKeys)throw new Exception("Legacy file implicitly enabled the model");
            foreach(var invalid in new[]{"not-json","null","[]",new string('x',4097)})
            {
                File.WriteAllText(file,invalid);
                if(store.Current.LocalZenz||store.Current.LearningEnabled)throw new Exception("Invalid preferences enabled AI or learning");
            }
        }
        finally{File.Delete(Path.Combine(folder,"preferences.json"));Directory.Delete(folder);}
        Console.WriteLine("PASS: selected release defaults, fresh startup, existing/legacy preferences and fail-closed invalid settings");
    }
}

// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Config;
using Meltype.Detection;
using static Meltype.Tests.TestSupport;

namespace Meltype.Tests;

internal static class DetectionTests
{
    private static readonly ScoreEngine Engine = CreateEngine();

    private static void ExpectJapanese(params string[] words)
    {
        foreach (var word in words)
        {
            var result = Classify(Engine, word);
            Assert.True(result.Verdict == Verdict.Japanese, $"「{word}」は日本語と判定されるべき: {result.Describe()}");
        }
    }

    private static void ExpectNotJapanese(params string[] words)
    {
        foreach (var word in words)
        {
            var result = Classify(Engine, word);
            Assert.True(result.Verdict != Verdict.Japanese, $"「{word}」を日本語と誤判定した: {result.Describe()}");
        }
    }

    // ---- 設計書 §30 テスト方針 ----

    [Test] public static void Design_Japanese() => ExpectJapanese("konnichiwa", "arigatou", "ohayou", "watashi", "ashita");

    [Test] public static void Design_Typo() => ExpectJapanese("konnitiwa", "konnichia", "arigatouu");

    // 報告: 英数状態で OK の後に notasuku (のタスク) と打っても日本語に戻らない。fucarete (c 行) も。
    [Test] public static void ParticleThenWord_AndCRow() => ExpectJapanese("notasuku", "gamenwo", "fucarete");

    [Test] public static void Design_English() => ExpectNotJapanese("hello", "github", "typescript", "javascript", "server", "terminal");

    [Test]
    public static void Design_TechnicalInput()
    {
        // Space でセッションが切れるので単語ごとに判定される。
        foreach (var line in new[] { "npm install", "git commit", "git push", "localhost", "http://" })
        {
            foreach (var word in line.Split(' ')) ExpectNotJapanese(word);
        }
    }

    [Test] public static void Design_UnknownStaysUnchanged() => ExpectNotJapanese("test");

    [Test] public static void Design_RomajiReadableEnglishIsNotEnough() => ExpectNotJapanese("kana", "sushi", "radio");

    // ---- 追加の確認 ----

    [Test]
    public static void MoreJapanese() => ExpectJapanese(
        "kyou", "desu", "masu", "sugoi", "tabemasu", "shigoto", "tomodachi", "daijoubu", "yoroshiku", "otsukare",
        "sumimasen", "onegaishimasu", "hontou", "mainichi", "nihongo", "kaigi", "shiryou", "kakunin", "ryoukai",
        "wakarimashita", "itadakimasu", "gomennasai", "zenzen", "chotto", "tanoshii", "kanashii", "benri");

    // 実機のログで見逃していた語 (him → 保留切れ, onakag → 辞書になし)。
    [Test]
    public static void ReportedMisses() => ExpectJapanese("himadana", "onakagasuita", "nemui", "tsukareta", "oishii", "ganbatte", "yukkuri");

    [Test]
    public static void NewDictionaryWordsDoNotCatchEnglish() => ExpectNotJapanese(
        "haha", "hair", "suitable", "semantic", "madonna", "naked", "nerd", "hotel", "music", "seminar", "motive", "kidney");

    [Test]
    public static void KunreiSpellingsAreJapanese() => ExpectJapanese("sigoto", "tomodati", "otukare", "arigatou", "siryou", "konniti");

    [Test]
    public static void CommonEnglishIsNotJapanese() => ExpectNotJapanese(
        "the", "and", "you", "make", "take", "same", "home", "time", "name", "made", "note", "open", "done",
        "tomorrow", "anatomy", "monday", "should", "shirt", "kitten", "matter", "item", "setting", "remote",
        "minute", "banana", "hamburger", "karate", "karaoke", "tsunami", "anime", "manga", "ninja", "samurai",
        "tofu", "kimono", "sake", "sumo", "tokyo", "kyoto", "honda", "toyota", "nintendo", "pokemon", "kawaii",
        "sensei", "senpai", "matcha", "wasabi", "tempura", "emoji", "sudoku", "demo", "node", "none", "kite");

    [Test]
    public static void EnglishSentencesNeverSwitch()
    {
        const string text =
            "The quick brown fox jumps over the lazy dog. Please review my pull request before the meeting tomorrow. " +
            "I think we need to update the documentation and fix the remaining bugs. Can you send me the latest " +
            "build? The server returned an error when I tried to deploy the new version. Let me know if you have " +
            "any questions about the design. We should make a decision on the database migration by Monday. " +
            "Our team will take care of the performance issue this week. Thanks for your help, see you soon. " +
            "Remote work is going well and the new monitor is great. Open the terminal and run the tests again. " +
            "Maybe we can meet at noon near the station. Take a look at the image I attached to the ticket.";
        var words = text.Split([' ', '.', ',', '?', '!'], StringSplitOptions.RemoveEmptyEntries);
        ExpectNotJapanese(words);
    }

    [Test]
    public static void ObviousEnglishIsDecidedOnFirstKey()
    {
        foreach (var letter in "lqvx")
        {
            var result = Engine.Evaluate(new DetectionInput(letter.ToString(), [char.ToUpperInvariant(letter)], false));
            Assert.Equal(Verdict.English, result.Verdict, $"'{letter}'");
        }
    }

    [Test]
    public static void EnglishIsDecidedEarly()
    {
        // 待ち時間を減らすため、ローマ字として成立しなくなった時点で英語と確定する。
        Assert.Equal("hel", Classify(Engine, "hello").Text);
        Assert.Equal("th", Classify(Engine, "the").Text);
        Assert.Equal("np", Classify(Engine, "npm").Text);
    }

    [Test]
    public static void JapaneseIsDecidedWithinFourLetters()
    {
        Assert.Equal("konn", Classify(Engine, "konnichiwa").Text);
        Assert.Equal("wata", Classify(Engine, "watashi").Text);
        Assert.Equal("kyou", Classify(Engine, "kyouha").Text);
    }

    [Test]
    public static void HigherThresholdIsMoreConservative()
    {
        var strict = DefaultSettings();
        strict.JapaneseThreshold = 12;
        var engine = CreateEngine(strict);
        Assert.True(Classify(engine, "konnichiwa").Verdict != Verdict.Japanese, "閾値を上げたら切り替えない");
    }

    [Test]
    public static void KanaInputStyle()
    {
        var settings = DefaultSettings();
        settings.InputStyle = InputStyle.Kana;
        var engine = CreateEngine(settings);
        // こんにちは (JIS かな配列: こ=B ん=Y に=I ち=A は=F)
        int[] keys = [0x42, 0x59, 0x49, 0x41, 0x46];
        DetectionResult? result = null;
        for (var i = 1; i <= keys.Length; i++)
        {
            var letters = new string(keys[..i].Select(k => (char)('a' + k - 0x41)).ToArray());
            result = engine.Evaluate(new DetectionInput(letters, keys[..i], i == keys.Length));
            if (result.Verdict != Verdict.Undecided) break;
        }
        Assert.Equal(Verdict.Japanese, result!.Verdict, result.Describe());
        Assert.Equal("こんにちは", KanaDetector.ToKana(keys));
        // 濁点キー (@) は直前の文字に合成される: か + ゛ = が
        Assert.Equal("が", KanaDetector.ToKana([0x54, 0xC0]));
    }

    [Test]
    public static void Romaji_Analyze()
    {
        var romaji = new RomajiDetector();
        Assert.Equal("こんにちわ", romaji.Analyze("konnichiwa").Kana);
        Assert.Equal("こんにちは", romaji.Analyze("konnnichiha").Kana);
        Assert.Equal("しんぶん", romaji.AnalyzeWord("shinbun").Kana);
        Assert.Equal("きって", romaji.Analyze("kitte").Kana);
        Assert.Equal("まっちゃ", romaji.Analyze("matcha").Kana);
        Assert.Equal("しゅ", romaji.Analyze("syu").Kana);
        Assert.Equal("し", romaji.AnalyzeFragment("ci").Kana);
        Assert.True(!romaji.Analyze("ci").IsValid, "ci は変換ボックス専用");
        Assert.True(romaji.Analyze("ky").IsValid, "入力途中の子音は有効");
        Assert.True(!romaji.Analyze("th").IsValid, "th は不正");
        Assert.True(!romaji.Analyze("np").IsValid, "語頭の ん は不正");
        Assert.True(!romaji.Analyze("kkk").IsValid, "語頭の っ は不正");
        Assert.True(romaji.Analyze("kyou").StrongYouon == 1, "拗音");
    }

    [Test]
    public static void Romaji_SpellingVariants()
    {
        var variants = new RomajiDetector().SpellingVariants("shigoto");
        Assert.True(variants.Contains("sigoto"), string.Join(",", variants));
        variants = new RomajiDetector().SpellingVariants("konnichiwa");
        Assert.True(variants.Contains("konnitiwa") && variants.Contains("konnnichiwa"), string.Join(",", variants));
        variants = new RomajiDetector().SpellingVariants("shinbun");
        Assert.True(variants.Contains("shinnbunn") && variants.Contains("sinbun"), string.Join(",", variants));
    }

    [Test]
    public static void Typo_Levenshtein()
    {
        Assert.Equal(0, TypoDetector.Levenshtein("abc", "abc"));
        Assert.Equal(1, TypoDetector.Levenshtein("konnichia", "konnichiwa"));
        Assert.Equal(2, TypoDetector.Levenshtein("kitten", "sitting", 1), "上限を超えたら max+1");
    }

    [Test]
    public static void Typo_AloneDoesNotSwitch()
    {
        var withoutTypo = DefaultSettings();
        withoutTypo.TypoEnabled = true;
        var engine = CreateEngine(withoutTypo);
        var result = engine.Evaluate(new DetectionInput("konic", [], false));
        var typo = result.Contributions.Where(c => c.Source == "Typo").Sum(c => c.Japanese);
        Assert.True(typo > 0 && typo < withoutTypo.JapaneseThreshold, $"Typo の加点は閾値未満であるべき: {result.Describe()}");
    }

    [Test]
    public static void Describe_HidesTypedText_UnlessRecordTextIsOn()
    {
        // ログに出す判定の説明に、打った文字 (「kyouha」や、理由の中の「kyou」と一致 など) を出さない (入力した文字をログに出す設定が OFF のとき)
        var result = Engine.Evaluate(new DetectionInput("kyouha", "KYOUHA".Select(c => (int)c).ToArray(), true));
        var before = Diagnostics.Log.RecordText;
        try
        {
            Diagnostics.Log.RecordText = false;
            var hidden = result.Describe();
            Assert.True(!hidden.Contains("kyou") && hidden.Contains("(6 文字)"), $"打った文字を出さない: {hidden}");
            Diagnostics.Log.RecordText = true;
            Assert.True(result.Describe().Contains("kyouha"), "ON なら出す");
        }
        finally
        {
            Diagnostics.Log.RecordText = before;
        }
    }
}

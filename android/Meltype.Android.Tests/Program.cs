// SPDX-License-Identifier: GPL-3.0-or-later
using Meltype.AndroidCore;
using Meltype.Composition;

var converter = new FakeConverter();
var document = "";
var preedit = "";
var session = new AndroidInputSession(converter, _ => [], (operation, text, _) =>
{
    if (operation == 0) preedit = text;
    if (operation == 1) { document += text; preedit = ""; }
    if (operation == 2) preedit = "";
});
void Type(string raw) { foreach (var c in raw) session.Character(c); }
void Equal(string expected, string actual)
{
    if (expected != actual) throw new Exception($"Expected {expected}, got {actual}");
}
Type("kyouha"); Equal("今日は", preedit);
session.SetEnglish(true); Equal("今日は", document);
Type("nihongo,."); Equal("今日はnihongo,.", document);
Equal("", preedit);
session.SetEnglish(false);
Type("nihongo"); Equal("日本語", preedit);
Type("?"); Equal("日本語？", preedit);
session.Commit(); Equal("今日はnihongo,.日本語？", document);
session.Reset();
Type("kyouhagoogledekensaku"); Equal("今日はgoogleで検索", preedit);
Type("."); Equal("今日はgoogleで検索．", preedit);
session.Key(0x1B); Equal("", preedit);
Console.WriteLine("PASS: automatic detection, punctuation, mode toggle, preserving pending input, cancellation");

var liveDocument = "";
var live = new AndroidInputSession(new FakeConverter(), reading => reading switch
{
    "にほんご" => ["日本語", "日本語版"],
    "でけんさく" => ["で検索", "で探索"],
    _ => [],
}, (operation, text, _) => { if (operation == 1) liveDocument += text; });
foreach (var c in "nihongo") live.Character(c);
if (live.View is not { Converting: false } || !live.View.Candidates.Contains("日本語版")) throw new Exception("Live candidates are not visible before Space.");
Equal("", liveDocument);
live.Select(live.View.Candidates.ToList().IndexOf("日本語版"));
Equal("日本語版", liveDocument);
liveDocument = ""; live.Reset();
foreach (var c in "kyouhagoogledekensaku") live.Character(c);
if (live.View is not { Converting: false } || !live.View.Candidates.Contains("で探索")) throw new Exception("Mixed live candidates are missing.");
live.Select(live.View.Candidates.ToList().IndexOf("で探索"));
Equal("今日はgoogleで探索", liveDocument);
live.SetEnglish(true); live.Character('a');
if (live.View != null) throw new Exception("English mode retained Japanese candidates.");
Console.WriteLine("PASS: live candidates before Space, no early commit, candidate selection preserves mixed text, English mode clears candidates");

var partialDocument = "";
var partialPreedit = "";
var partial = new AndroidInputSession(new ClauseConverter(), reading => reading switch
{
    "われわれは" => ["我々は", "われわれは"],
    "うちゅうじんだ" => ["宇宙人だ", "宇宙人だよ"],
    _ => [],
}, (operation, text, _) => { if (operation == 0) partialPreedit = text; if (operation == 1) partialDocument += text; if (operation is 1 or 2) partialPreedit = ""; });
foreach (var c in "warewarehautyuujinda") partial.Character(c);
partial.Character(' ');
partial.Select(partial.View!.Candidates.ToList().IndexOf("われわれは"));
Equal("われわれは", partialDocument);
Equal("宇宙人だ", partialPreedit);
if (partial.View is not { Converting: true } || !partial.View.Candidates.Contains("宇宙人だよ")) throw new Exception("Later clause lost its editable candidates.");
partial.Select(partial.View.Candidates.ToList().IndexOf("宇宙人だよ"));
Equal("われわれは宇宙人だよ", partialDocument);
Equal("", partialPreedit);
partial.Reset(); partialDocument = "";
foreach (var c in "warewarehautyuujinda") partial.Character(c);
partial.Character(' '); partial.Key(0x0D);
Equal("我々は", partialDocument); Equal("宇宙人だ", partialPreedit);
partial.Key(0x0D); Equal("我々は宇宙人だ", partialDocument);
partial.Reset(); partialDocument = "";
foreach (var c in "warewarehautyuujinda") partial.Character(c);
partial.Character(' '); partial.Key(0x0D); partial.Key(0x1B); partial.Key(0x1B);
Equal("我々は", partialDocument); Equal("", partialPreedit);
partial.Character('「'); partial.Character('」');
Equal("我々は「」", partialDocument);
Console.WriteLine("PASS: candidate tap and Enter confirm only the selected prefix; later clause remains editable");

partial.Reset(); partialDocument = "";
foreach (var c in "warewarehautyuujinda") partial.Character(c);
partial.SentencePunctuation(); partial.Commit();
Equal("我々は宇宙人だ．", partialDocument);
session.Reset(); document = "";
Type("kyouha"); session.SentencePunctuation(); session.Commit();
Equal("今日は，", document);
session.SetEnglish(true); session.SentencePunctuation(); Equal("今日は，,", document);
Console.WriteLine("PASS: sentence-ending comma key becomes full-width period; unfinished sentence and English remain commas");

session.SetEnglish(false); session.Reset(); document = "";
session.SentencePunctuation("我々は宇宙人だ"); session.Commit(); Equal("．", document);
session.Reset(); document = "";
session.SentencePunctuation("我々は"); session.Commit(); Equal("，", document);
session.Reset(); document = "";
session.SentencePunctuation(null); session.Commit(); Equal("，", document);
session.Reset(); document = "";
Type("kyouha"); session.SentencePunctuation("前の文です"); session.Commit(); Equal("今日は，", document);
session.Reset(); document = "";
session.SentencePunctuation("我々は宇宙人だ．"); session.Commit(); Equal("，", document);
Console.WriteLine("PASS: confirmed cursor context, unfinished prefix, unavailable context, current preedit priority, no unconditional post-confirm period");

sealed class ClauseConverter : IKanjiConverter
{
    public string? Convert(string reading) => string.Concat(ConvertClauses(reading)!.Select(c => c.Text));
    public IReadOnlyList<ConversionClause>? ConvertClauses(string reading, string? context = null) => reading switch
    {
        "われわれはうちゅうじんだ" => [new("われわれは", "我々は"), new("うちゅうじんだ", "宇宙人だ")],
        "われわれは" => [new(reading,"我々は")],
        "うちゅうじんだ" => [new(reading,"宇宙人だ")],
        _ => [new(reading, reading.Replace("われわれは", "我々は").Replace("うちゅうじんだ", "宇宙人だ"))]
    };
}

sealed class FakeConverter : IKanjiConverter
{
    public string? Convert(string reading) => string.Concat(ConvertClauses(reading)!.Select(c => c.Text));
    public IReadOnlyList<ConversionClause>? ConvertClauses(string reading, string? context = null) =>
        new[] { new ConversionClause(reading, reading.Replace("きょうは", "今日は").Replace("にほんご", "日本語").Replace("けんさく", "検索")) };
}

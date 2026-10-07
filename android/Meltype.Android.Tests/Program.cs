// SPDX-License-Identifier: GPL-3.0-or-later
using Meltype.AndroidCore;
using Meltype.Composition;

var cursorOperations = new List<int>();
var cursorSession = new AndroidInputSession(new FakeConverter(), _ => [], (operation, _, _) => cursorOperations.Add(operation));
cursorSession.Key(0x25); cursorSession.Key(0x27);
if (!cursorOperations.SequenceEqual(new[] { 5, 6 })) throw new Exception("Empty input arrows did not move the editor cursor.");
cursorOperations.Clear();
foreach (var c in "nihongo") cursorSession.Character(c);
cursorOperations.Clear(); cursorSession.Key(0x25); cursorSession.Key(0x27);
if (cursorOperations.Any(operation => operation is 5 or 6)) throw new Exception("Pending composition arrows moved the editor cursor.");
cursorSession.Commit(); cursorOperations.Clear(); cursorSession.Key(0x25);
if (!cursorOperations.SequenceEqual(new[] { 5 })) throw new Exception("Confirmed input arrow did not move the editor cursor.");
cursorSession.SetEnglish(true); cursorOperations.Clear(); cursorSession.Key(0x27);
if (!cursorOperations.SequenceEqual(new[] { 6 })) throw new Exception("English arrow did not move the editor cursor.");
Console.WriteLine("PASS: editor arrows before input, after confirmation and in English; composition arrows stay internal");

var ciSession = new AndroidInputSession(new FakeConverter(), _ => [], (_, _, _) => { });
ciSession.Character('c'); ciSession.Character('i');
if (ciSession.View?.Text != "し") throw new Exception("Upstream ci conversion is missing.");
Console.WriteLine("PASS: upstream ci spelling converts to し");

var punctuationSession = new AndroidInputSession(new FakeConverter(), _ => [], (_, _, _) => { });
if (punctuationSession.PunctuationCharacter("我々は宇宙人だ") != '.' || punctuationSession.PunctuationCharacter("我々は") != ',') throw new Exception("Punctuation key prediction is wrong.");
punctuationSession.SetEnglish(true);
if (punctuationSession.PunctuationCharacter("文です") != ',') throw new Exception("English punctuation key became a period.");
punctuationSession.SetEnglish(false);
foreach (var c in "kyouha") punctuationSession.Character(c);
if (punctuationSession.PunctuationCharacter("前の文です") != ',') throw new Exception("Punctuation label ignored pending text.");
var shift = new KeyboardShift(); shift.Toggle();
if (shift.Apply('a') != 'A' || shift.Apply('b') != 'b' || shift.Active) throw new Exception("Shift did not release after one letter.");
shift.Toggle(); shift.Reset();
if (shift.Active || shift.Apply('a') != 'a') throw new Exception("Other key did not release Shift.");
Console.WriteLine("PASS: punctuation prediction follows output; Shift releases after one character or other action");

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

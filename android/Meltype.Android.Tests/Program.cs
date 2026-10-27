// SPDX-License-Identifier: GPL-3.0-or-later
using Meltype.AndroidCore;
using Meltype.Composition;

if (args is ["--zenz-boundary-local", var endpoint])
{
    ModelBoundaryTests.RunLocalModel(endpoint);
    return;
}

if (args is ["--zenz-model", var zenzModel, var zenzRuntime])
{
    AndroidZenzTests.RunNative(zenzModel, zenzRuntime);
    return;
}
if (args is ["--import-dictionary", var source, var target])
{
    var store = new AndroidUserDictionary(target);
    var result = store.Import(File.ReadAllBytes(source));
    Console.WriteLine($"Imported: {result.Added}, skipped: {result.Skipped}, total: {store.Count}");
    return;
}

if (args is ["--audit-personal-typos", var model, var analysis, var report])
{
    PersonalRomajiTests.Audit(model, analysis, report);
    return;
}

if (typeof(AndroidInputSession).Assembly.GetType("Meltype.AndroidCore.TypoTrainingStore") != null ||
    typeof(AndroidInputSession).Assembly.GetType("Meltype.AndroidCore.TypoEditCapture") != null)
    throw new Exception("Release contains typo-acquisition capability");
if (typeof(AndroidInputSession).Assembly.GetType("Meltype.AndroidCore.HardwareKeyboard") is not null)
    throw new Exception("Android still includes external-keyboard-only commands");
ReleaseDefaultsTests.Run();
MozcReuseTests.Run();
KeyEventInputTests.Run();
RuleBasedCorrectionTests.WholeWordAfterPrefix();
RuleBasedCorrectionTests.Run();
TouchCorrectionTests.Run();
NeuralConversionTests.Run();
CandidateDisplayTests.Run();
ClauseReviewTests.Run();
AndroidUpdateTests.Run();
PersonalTypoLearningTests.Run();
LiveConversionTests.Run();
ModelNumberTests.Run();
ModelBoundaryTests.Run();
MozcCandidateTests.Run();
AndroidZenzTests.Run();
GodanKeyboardTests.Run();
Feature130Tests.Run();
AdaptiveInputTests.Run();
PersonalRomajiTests.Run();
ConversionSelectionTests.Run();
ConversionSelectionTests.ArrowTests();
ConversionSelectionTests.InitialResizeTests();
ConversionSelectionTests.ResizeTests();
ConversionSelectionTests.MisclassifiedEnglishTests();
ConversionSelectionTests.SpaceTests();
ConversionSelectionTests.CandidateTests();
NumericKeyboardTests.Run();
EditorCompositionTests.Run();
KeyboardInteractionTests.Run();
InputPersonalizationTests.Run();
InputSettingTests.Run();
UserDictionaryTests.Run();
BracketConversionTests.Run();

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

var qaSession = new AndroidInputSession(new FakeConverter(), _ => [], (_, _, _) => { });
qaSession.Character('q'); qaSession.Character('a');
if (qaSession.View?.Text != "くぁ") throw new Exception("GODAN Q flick spelling qa does not convert to くぁ.");
Console.WriteLine("PASS: GODAN Q flick spelling qa converts to くぁ");

// GODAN の最下段中央キー: タップ=小書き/通常、左右フリック=濁点/半濁点。
var dakuten = new AndroidInputSession(new FakeConverter(), _ => [], (_, _, _) => { });
foreach (var c in "ha") dakuten.Character(c);
if (dakuten.View?.Text != "は" || !dakuten.ApplyDakuten(false) || dakuten.View?.Text != "ば")
    throw new Exception("GODAN 大⇔小 key did not add dakuten to は.");
var handakuten = new AndroidInputSession(new FakeConverter(), _ => [], (_, _, _) => { });
foreach (var c in "ha") handakuten.Character(c);
if (!handakuten.ApplyDakuten(true) || handakuten.View?.Text != "ぱ")
    throw new Exception("GODAN 大⇔小 key did not add handakuten to は.");
var small = new AndroidInputSession(new FakeConverter(), _ => [], (_, _, _) => { });
foreach (var c in "ya") small.Character(c);
if (small.View?.Text != "や" || !small.ToggleSmallLast() || small.View?.Text != "ゃ" || !small.ToggleSmallLast() || small.View?.Text != "や")
    throw new Exception("GODAN 大⇔小 key did not toggle や and ゃ.");
var tsu = new AndroidInputSession(new FakeConverter(), _ => [], (_, _, _) => { });
foreach (var c in "tsu") tsu.Character(c);
if (tsu.View?.Text != "つ" || !tsu.ToggleSmallLast() || tsu.View?.Text != "っ" || !tsu.ToggleSmallLast() || tsu.View?.Text != "つ")
    throw new Exception("GODAN 大⇔小 key did not toggle つ and っ.");
Console.WriteLine("PASS: GODAN 大⇔小 key toggles small/large and adds dakuten/handakuten");

// 大⇔小キーのタップ循環: は→ば→ぱ→は、や→ゃ→や。
var cycle = new AndroidInputSession(new FakeConverter(), _ => [], (_, _, _) => { });
foreach (var c in "ha") cycle.Character(c);
if (!cycle.CycleKanaVariant() || cycle.View?.Text != "ば" || !cycle.CycleKanaVariant() || cycle.View?.Text != "ぱ" ||
    !cycle.CycleKanaVariant() || cycle.View?.Text != "は")
    throw new Exception("GODAN 大⇔小 key did not cycle は→ば→ぱ→は.");
var cycleSmall = new AndroidInputSession(new FakeConverter(), _ => [], (_, _, _) => { });
foreach (var c in "ya") cycleSmall.Character(c);
if (!cycleSmall.CycleKanaVariant() || cycleSmall.View?.Text != "ゃ" || !cycleSmall.CycleKanaVariant() || cycleSmall.View?.Text != "や")
    throw new Exception("GODAN 大⇔小 key did not cycle や→ゃ→や.");
var leftOnVoiced = new AndroidInputSession(new FakeConverter(), _ => [], (_, _, _) => { });
foreach (var c in "ha") leftOnVoiced.Character(c);
if (!leftOnVoiced.ApplyDakuten(false) || leftOnVoiced.View?.Text != "ば" || !leftOnVoiced.ApplyDakuten(true) || leftOnVoiced.View?.Text != "ぱ")
    throw new Exception("GODAN handakuten did not convert ば to ぱ.");
Console.WriteLine("PASS: GODAN 大⇔小 tap cycle and handakuten on voiced kana");

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

shift.Toggle(); shift.Toggle();
if (!shift.Locked || shift.Apply('a') != 'A' || shift.Apply('b') != 'B') throw new Exception("Double Shift did not lock capitals.");
shift.Consume();
if (!shift.Locked) throw new Exception("Other action cleared Caps Lock.");
shift.Toggle();
if (shift.Active || shift.Locked) throw new Exception("Shift did not unlock capitals.");
void ChoosePair(AndroidInputSession input, string pair)
{
    var quotes = "「『【".Contains(pair[0]);
    input.BeginBracketConversion(quotes);
    input.Select(input.View!.Candidates.ToList().IndexOf(pair));
}
shift.Reset(); shift.Toggle(1000); shift.Toggle(1400);
if (shift.Locked || shift.Active) throw new Exception("Slow Shift taps activated Caps Lock");
shift.Toggle(2000); shift.Toggle(2299);
if (!shift.Locked) throw new Exception("Fast Shift taps did not lock capitals");
shift.Reset(); shift.Toggle(3000); shift.Toggle(3300);
if (!shift.Locked) throw new Exception("300ms Shift boundary did not lock capitals");
shift.Reset(); shift.Toggle(4000); shift.Consume(); shift.Toggle(4100);
if (shift.Locked || !shift.Active) throw new Exception("Other action did not break double tap");
shift.Reset();
var modeOps = new List<(int Op, string Text)>();
var modes = new AndroidInputSession(new FakeConverter(), _ => [], (op, text, _) => modeOps.Add((op,text)));
foreach (var c in "nihongo") modes.Character(c);
modes.Character(' '); modeOps.Clear(); modes.ToggleKana();
if (modes.View?.Text != "ニホンゴ" || !modes.Katakana || modeOps.Any(o => o.Op == 1)) throw new Exception("Kana toggle committed or lost text.");
modes.ToggleKana();
if (modes.View?.Text != "日本語" || modes.Katakana) throw new Exception("Automatic conversion was not restored after katakana.");
foreach (var raw in new[] { "nihongo", "open", "kyouhagoogledekensaku" })
{
    var commits = new List<string>();
    var automatic = new AndroidInputSession(new FakeConverter(), _ => [], (_, _, _) => { });
    var toggled = new AndroidInputSession(new FakeConverter(), _ => [], (op, text, _) => { if (op == 1) commits.Add(text); });
    foreach (var c in raw) { automatic.Character(c); toggled.Character(c); }
    toggled.ToggleKana(); toggled.ToggleKana();
    if (toggled.Katakana || toggled.View?.Text != automatic.View?.Text || commits.Count > 0)
        throw new Exception("Katakana return changed automatic preview or committed: " + raw);
    automatic.Character('a'); toggled.Character('a');
    if (toggled.View?.Text != automatic.View?.Text)
        throw new Exception("Input remained forced kana after automatic return: " + raw);
    var visible = toggled.View!.Text;
    toggled.Commit();
    if (string.Concat(commits) != visible) throw new Exception("Automatic return changed confirmation: " + raw);
}
Console.WriteLine("PASS: katakana returns to automatic Japanese/English/mixed preview, subsequent input and confirmation without early commit");
modeOps.Clear(); ChoosePair(modes, "()");
if (!modeOps.Any(o => o.Op == 1 && o.Text == "日本語") || modeOps.Last() != (7,"()") || modes.View != null) throw new Exception("Bracket insertion did not preserve composition and move inside.");
modes.SetEnglish(true); modeOps.Clear(); ChoosePair(modes, "()");
if (modeOps.Last() != (7,"()")) throw new Exception("English bracket insertion failed.");
foreach (var pair in new[] { "()", "「」", "{}", "[]", "【】", "『』" })
{
    modes.SetEnglish(false); modes.Reset(); modeOps.Clear();
    foreach (var c in "nihongo") modes.Character(c);
    ChoosePair(modes, pair);
    if (!modeOps.Any(o => o.Op == 1 && o.Text == "日本語") || modeOps.Last() != (7,pair) || modes.View != null)
        throw new Exception("Bracket pair did not preserve pending composition: " + pair);
    modes.SetEnglish(true); modeOps.Clear(); ChoosePair(modes, pair);
    if (modeOps.Last() != (7,pair)) throw new Exception("English bracket pair failed: " + pair);
}
Console.WriteLine("PASS: Caps Lock, kana toggle without confirmation, all six bracket pairs after composition");

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

foreach (var converting in new[] { false, true })
{
    partial.Reset(); partialDocument = "";
    foreach (var c in "warewarehautyuujinda") partial.Character(c);
    if (converting) { partial.Space(false); partial.Key(0x27); partial.Space(false); }
    var suffix = converting ? "宇宙人だよ" : "宇宙人だ";
    partial.SelectPosition(1);
    Equal("", partialDocument);
    if (partial.View?.SelectedClause != 0) throw new Exception("Editor selection did not select earlier clause");
    partial.Select(partial.View.Candidates.ToList().IndexOf("われわれは"));
    Equal("われわれは", partialDocument); Equal(suffix, partialPreedit);
    if (partial.View is not { Converting: true }) throw new Exception("Earlier edit confirmed later clause");
    partial.Select(partial.View.Candidates.ToList().IndexOf("宇宙人だよ"));
    Equal("われわれは宇宙人だよ", partialDocument);
}
Console.WriteLine("PASS: editor selection of earlier live/converting clause preserves later candidates and chosen text");

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

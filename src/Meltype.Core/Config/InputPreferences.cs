// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text.Json;

namespace Meltype.Config;

public sealed record InputPreferences
{
    public string Comma { get; init; } = "，";
    public string Period { get; init; } = "．";
    public bool LearningEnabled { get; init; } = true;
    public bool CalculatorCandidates { get; init; } = true;
    public bool ConsonantAbbreviations { get; init; } = false;
    public bool ClipboardCandidates { get; init; } = false;
    public bool Predictions { get; init; } = true;
    public bool EnglishNamePredictions { get; init; } = true;
    public bool ShowCandidatesWhileTyping { get; init; } = true;
    public bool FullWidthParentheses { get; init; } = true;
    public bool CursorIntoBrackets { get; init; } = true;
    public bool SquareBrackets { get; init; } = false;
    public bool FullWidthSlash { get; init; } = false;
    public bool LaughterAfterKana { get; init; } = true;
    public bool FullWidthSpaceByDefault { get; init; } = false;
    public bool ShiftSpaceHalfWidth { get; init; } = false;
    public bool EnterCommitsAsShown { get; init; } = false;
    public bool FunctionKeysAffectSelectedClause { get; init; } = false;
    public bool AlphanumericKeyStartsAsTyped { get; init; } = false;
    public bool EnglishFirstInput { get; init; } = false;
    public bool ContextualLearning { get; init; } = false;
    public bool ShowTypedKeys { get; init; } = false;
    public bool LocalZenz { get; init; } = true;
    public InputPreferences Validate() => this with
    {
        Comma = Comma is "、" or "，" or "," ? Comma : "，",
        Period = Period is "。" or "．" or "." ? Period : "．"
    };
}

public sealed class InputPreferenceStore(string path)
{
    private readonly object _gate = new();
    public InputPreferences Current
    {
        get
        {
            lock (_gate)
            {
                try
                {
                    if (!File.Exists(path)) return new();
                    if (new FileInfo(path).Length > 4096) return new() { LearningEnabled = false, LocalZenz = false };
                    using var document = JsonDocument.Parse(File.ReadAllText(path));
                    if (document.RootElement.ValueKind != JsonValueKind.Object)
                        return new() { LearningEnabled = false, LocalZenz = false };
                    var preferences = document.RootElement.Deserialize<InputPreferences>() ??
                        new InputPreferences { LearningEnabled = false, LocalZenz = false };
                    if (!document.RootElement.TryGetProperty(nameof(InputPreferences.LocalZenz), out _))
                        preferences = preferences with { LocalZenz = false };
                    return preferences.Validate();
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
                { return new() { LearningEnabled = false, LocalZenz = false }; }
            }
        }
    }
    public void Save(InputPreferences value)
    {
        lock (_gate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temp, JsonSerializer.Serialize(value.Validate()));
                File.Move(temp, path, overwrite: true);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
    }
}

public sealed record InputFeatureSetting(string Key, string Section, string Title,
    Func<InputPreferences, bool> Read, Func<InputPreferences, bool, InputPreferences> Write);
public static class InputFeatureSettings
{
    public static IReadOnlyList<InputFeatureSetting> All { get; } =
    [
        new("CalculatorCandidates", "変換候補", "計算結果を候補に出す", p => p.CalculatorCandidates, (p, value) => p with { CalculatorCandidates = value }),
        new("ConsonantAbbreviations", "変換候補", "子音の略入力を候補に出す", p => p.ConsonantAbbreviations, (p, value) => p with { ConsonantAbbreviations = value }),
        new("ClipboardCandidates", "変換候補", "読みからクリップボードを変換する", p => p.ClipboardCandidates, (p, value) => p with { ClipboardCandidates = value }),
        new("Predictions", "変換候補", "語句・英単語の予測候補を表示する", p => p.Predictions, (p, value) => p with { Predictions = value }),
        new("EnglishNamePredictions", "変換候補", "予測候補に英字の社名・サービス名を含める", p => p.EnglishNamePredictions, (p, value) => p with { EnglishNamePredictions = value }),
        new("ShowCandidatesWhileTyping", "変換候補", "入力中の変換候補を表示する", p => p.ShowCandidatesWhileTyping, (p, value) => p with { ShowCandidatesWhileTyping = value }),
        new("FullWidthParentheses", "記号・空白", "丸括弧を全角にする", p => p.FullWidthParentheses, (p, value) => p with { FullWidthParentheses = value }),
        new("CursorIntoBrackets", "記号・空白", "括弧の確定後に内側へ移動する", p => p.CursorIntoBrackets, (p, value) => p with { CursorIntoBrackets = value }),
        new("SquareBrackets", "記号・空白", "角括弧を［ ］にする", p => p.SquareBrackets, (p, value) => p with { SquareBrackets = value }),
        new("FullWidthSlash", "記号・空白", "スラッシュを全角にする", p => p.FullWidthSlash, (p, value) => p with { FullWidthSlash = value }),
        new("LaughterAfterKana", "記号・空白", "かなの後のwを笑いとして残す", p => p.LaughterAfterKana, (p, value) => p with { LaughterAfterKana = value }),
        new("FullWidthSpaceByDefault", "記号・空白", "通常の空白を全角にする", p => p.FullWidthSpaceByDefault, (p, value) => p with { FullWidthSpaceByDefault = value }),
        new("ShiftSpaceHalfWidth", "記号・空白", "Shiftと空白で半角スペースを入れる", p => p.ShiftSpaceHalfWidth, (p, value) => p with { ShiftSpaceHalfWidth = value }),
        new("EnterCommitsAsShown", "入力操作", "表示どおりに確定する", p => p.EnterCommitsAsShown, (p, value) => p with { EnterCommitsAsShown = value }),
        new("FunctionKeysAffectSelectedClause", "入力操作", "文字種の変更は選択文節だけに適用する", p => p.FunctionKeysAffectSelectedClause, (p, value) => p with { FunctionKeysAffectSelectedClause = value }),
        new("AlphanumericKeyStartsAsTyped", "入力操作", "英数字の文字種変更は打ったままから始める", p => p.AlphanumericKeyStartsAsTyped, (p, value) => p with { AlphanumericKeyStartsAsTyped = value }),
        new("EnglishFirstInput", "入力操作", "英字を中心に入力する", p => p.EnglishFirstInput, (p, value) => p with { EnglishFirstInput = value }),
        new("ContextualLearning", "学習・表示", "英語・日本語の学習を前の文脈で分ける", p => p.ContextualLearning, (p, value) => p with { ContextualLearning = value }),
        new("ShowTypedKeys", "学習・表示", "入力したローマ字を候補欄に表示する", p => p.ShowTypedKeys, (p, value) => p with { ShowTypedKeys = value }),
        new("LocalZenz", "端末内AI", "zenzで文脈変換・誤字補正する", p => p.LocalZenz, (p, value) => p with { LocalZenz = value }),
    ];
}

// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Meltype.Config;

public enum InputStyle
{
    /// <summary>ローマ字入力。</summary>
    [Description("ローマ字入力")] Romaji,
    /// <summary>JIS かな入力。</summary>
    [Description("かな入力 (JIS、α版)")] Kana,
    /// <summary>両方を判定する。</summary>
    [Description("両方を判定")] Both,
}

/// <summary>英語か日本語かの自動判定の強さ。</summary>
public enum DetectionLevel
{
    /// <summary>少しでも英語らしければ英字にする。</summary>
    [Description("積極的 (Aggressive)")] Aggressive,
    /// <summary>既定。短い語 (no, to) は前後が英語のときだけ英字。</summary>
    [Description("標準 (Balanced)")] Balanced,
    /// <summary>確信度が高いときだけ英字にする。</summary>
    [Description("慎重 (Conservative)")] Conservative,
    /// <summary>自動では切り替えず、提案だけ出す。</summary>
    [Description("手動 (提案のみ)")] Manual,
}

/// <summary>変換ボックスの文字の大きさ。</summary>
public enum CompositionSize
{
    /// <summary>入力欄の文字の高さ (キャレットの高さ) に合わせる。分からなければ「中」。</summary>
    [Description("自動 (入力欄に合わせる)")] Auto,
    [Description("小")] Small,
    [Description("中")] Medium,
    [Description("大")] Large,
}

/// <summary>変換ボックスを出す位置。</summary>
public enum CompositionPlacement
{
    /// <summary>打っている行に重ねる (入力欄の中に打っているように見える)。</summary>
    [Description("入力位置に重ねる")] Overlay,
    [Description("カーソルの下")] BelowCaret,
}

/// <summary>かな漢字変換のエンジン。</summary>
public enum ConversionEngine
{
    /// <summary>Mozc で変換し、使えないときは OS の変換エンジン。候補は両方。</summary>
    [Description("両方 (Mozc を優先)")] Hybrid,
    [Description("Mozc")] Mozc,
    /// <summary>OS の変換エンジン (Windows では Microsoft IME)。</summary>
    [Description("Microsoft IME")] System,
    [Description("Google日本語入力（試験版）")] Google,
}

public enum InputMode
{
    /// <summary>Meltype 自身の変換ボックスで入力する (半角/全角 不要)。</summary>
    [Description("Meltype キーボード (変換ボックスで入力)")] Keyboard,
    /// <summary>入力開始時に判定して Microsoft IME の ON/OFF を切り替える (v1 の動作)。</summary>
    [Description("IME 自動切替 (Microsoft IME を使う)")] AutoSwitch,
}

[TypeConverter(typeof(ExpandableObjectConverter))]
public sealed class AppRule
{
    [DisplayName("プロセス名"), Description("例: code.exe / chrome.exe / WindowsTerminal.exe")]
    public string Process { get; set; } = "";

    [DisplayName("自動切替"), Description("false にするとこのアプリでは一切キーを保留しません。")]
    public bool Enabled { get; set; } = true;

    [DisplayName("種類"), Description("コード = コードエディターやターミナル。基本は英数で、コメントや \"…\" の中だけ日本語を判定します。")]
    public AppProfile Profile { get; set; } = AppProfile.General;

    /// <summary>ユーザーが作った種類 (<see cref="Settings.AppKinds"/>) の名前。指定があれば Profile より優先。</summary>
    public string? Kind { get; set; }

    public override string ToString() => $"{Process}: {(Enabled ? "ON" : "OFF")}, {Kind ?? Profile.ToString()}";
}

/// <summary>
/// ユーザーが作るアプリの種類 (例: 「チャット」「ゲーム」)。一般 / コード を元に、判定の強さ・ライブ変換・最初の入力モードを変えられる。
/// null の項目は全体の設定のまま。
/// </summary>
public sealed class AppKind
{
    public string Name { get; set; } = "";
    public AppProfile Base { get; set; } = AppProfile.General;
    public DetectionLevel? DetectionLevel { get; set; }
    public bool? LiveConversion { get; set; }
    /// <summary>このアプリに切り替えたら英数 (直接入力) から始める。</summary>
    public bool StartInEnglish { get; set; }

    public AppKind Clone() => (AppKind)MemberwiseClone();
}

/// <summary>
/// プロファイル (仕事用・趣味用・SNS 用など)。設定の値をまとめて名前を付けたもの。
/// 使っているプロファイルの値は Settings そのもの。ほかのプロファイルの値は Values に入れておき、切り替えたときに Settings に読み込む。
/// </summary>
public sealed class SettingsProfile
{
    public string Name { get; set; } = "";

    /// <summary>このプロファイルの設定の値 (config.json と同じ形)。全員で共通の項目 (ログ・更新など) は入れない。</summary>
    public JsonObject? Values { get; set; }

    public SettingsProfile Clone() => new() { Name = Name, Values = Values?.DeepClone() as JsonObject };
}

/// <summary>アプリの種類。アプリに合わせて、英語と日本語のどちらを基本にするかを変える。</summary>
public enum AppProfile
{
    /// <summary>一般 (文章を書くアプリ)。日本語が基本で、英単語を自動で見分ける。</summary>
    [Description("一般")] General,
    /// <summary>コードエディター・ターミナル。英数が基本で、コメントと文字列 ("…") の中だけ日本語を判定する。</summary>
    [Description("コード")] Code,
    /// <summary>ゲーム。Meltype は何もしない (キーを横取りせず、Windows の IME の ON/OFF にも触らない。ゲームのチャットは Windows の IME で打つ)。</summary>
    [Description("ゲーム")] Game,
}

/// <summary>
/// config.json の内容。値はすべて保守的な初期値にしてある (設計書 §18)。
/// インスタンスは不変として扱い、変更時は Clone して差し替える。
/// </summary>
public sealed class Settings
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    [Category("1. 全般"), DisplayName("Meltype を有効にする")]
    public bool Enabled { get; set; } = true;

    [Category("1. 全般"), DisplayName("日本語キーボードのときだけ動作"),
     Description("Windows の入力言語が日本語のときだけ動作します。韓国語・英語などでは入力処理と IME の自動制御を停止し、日本語に戻すと再開します。物理キーボードの JIS / US 配列や IME の「あ」「A」の状態は問いません。全プロファイル共通です。")]
    public bool JapaneseKeyboardOnly { get; set; }

    [Category("1. 全般"), DisplayName("動作モード"),
     Description("Keyboard = Meltype の変換ボックスで入力 (英単語は自動で英字、Space で変換、Enter で確定) / AutoSwitch = 入力開始時に判定して Microsoft IME を自動で ON にする")]
    public InputMode Mode { get; set; } = InputMode.Keyboard;

    [Category("1. 全般"), DisplayName("半角/全角 で Meltype を ON/OFF"),
     Description("Keyboard モードで、変換ボックスが出ていないときの 半角/全角 キーを Meltype キーボードの ON/OFF (直接入力) に使います。")]
    public bool HankakuTogglesKeyboard { get; set; } = true;

    [Category("1. 全般"), DisplayName("確定後も文脈に合わせて直す"),
     Description("英語とも日本語とも読める語 (i, sushi など) を確定した後、次の語で英語か日本語かがはっきりしたら自動で確定し直します (i → 胃 と確定した後に want と打つと I want)。")]
    public bool AutoCorrectAfterCommit { get; set; } = true;

    [Category("1. 全般"), DisplayName("英数状態でもローマ字を検知"),
     Description("Keyboard モードの英数 (直接入力) 状態でも単語の打ち始めを判定し、ローマ字 (日本語) なら自動で日本語入力に戻します。")]
    public bool DirectModeAutoDetect { get; set; } = true;

    [Category("1. 全般"), DisplayName("英単語の前後に半角スペース"),
     Description("確定するときに、日本語と英単語の間に半角スペースを入れます (今日はGitHubにpushした → 今日は GitHub に push した)。数字だけの語 (3時) には入れません。")]
    public bool SpaceAroundEnglish { get; set; }

    [Category("1. 全般"), DisplayName("ライブ変換"),
     Description("Keyboard モードで、Space を押さなくても打ったそばから漢字に変換して表示します。")]
    public bool LiveConversion { get; set; } = true;

    [Category("1. 全般"), DisplayName("変換エンジン"),
     Description("かな漢字変換に使うエンジン。「両方」はMozcを優先します。「Google日本語入力（試験版）」はインストール済みのGoogle本体で変換し、確定した日本語をGoogleにも学習させます。Googleで一度入力してから使用してください。")]
    public ConversionEngine ConversionEngine { get; set; } = ConversionEngine.Hybrid;

    [Category("1. 全般"), DisplayName("英訳の候補"),
     Description("変換の候補の後ろに英訳も出します (複雑な → complex, complicated)。JMdict のよく使う語から。選んだ英訳は少しずつ前に出ます。")]
    public bool TranslationCandidates { get; set; } = true;

    [Category("1. 全般"), DisplayName("候補の意味を表示"),
     Description("変換中に同じ候補で少し (約 1.5 秒) 止まると、その候補の意味をウィクショナリー日本語版から候補の一覧の横に出します (日本語の意味が無い語は JMdict の英訳: 橋 → bridge)。同音異義語を選ぶときの手がかりに。")]
    public bool ShowCandidateMeanings { get; set; } = true;

    [Category("1. 全般"), DisplayName("打ち間違いを直す"),
     Description("Space・Enter で変換・確定するときに打ち間違いを直します。ローマ字: 読めない子音が残ったとき、隣のキーの押し間違い・入れ替わり・抜けを 1 文字だけ直します (onegaishimsu → お願いします、sumimasne → すみません。よく使う語の読みになるときだけ)。英語: Windows の自動修正の一覧にある打ち間違いを直します (teh → the、recieve → receive)。")]
    public bool CorrectTypos { get; set; } = true;

    [Category("1. 全般"), DisplayName("入力モードをカーソルの近くに表示"),
     Description("入力欄をクリックしたときと 半角/全角 を押したときに、カーソルの近くに「あ」(日本語) か「A」(英数) を一瞬表示します。Meltype キーボードの使用中は Windows の IME を OFF にしているので、タスクバーの IME の表示は常に「A」になります。今のモードはこの表示かトレイの Meltype のアイコンで確認してください。")]
    public bool ShowModeIndicator { get; set; } = true;

    [Category("1. 全般"), DisplayName("入力欄に入ったときも入力モードを表示"),
     Description("「入力モードをカーソルの近くに表示」が ON のとき、入力欄をクリックしたとき (フォーカスが入ったとき) にも「あ」「A」を出します。OFF にすると、半角/全角 を押したときだけ出します。")]
    public bool ShowModeIndicatorOnFocus { get; set; } = true;

    [Category("1. 全般"), DisplayName("変換ボックスの位置"),
     Description("入力位置に重ねる: 打っている文字が入力欄の中の入力位置にそのまま出ているように見えます。カーソルの下: 入力位置の下に別の枠で出します (今までの出し方)。入力位置が分からないアプリでは、どちらも入力欄の下に出します。")]
    public CompositionPlacement CompositionPlacement { get; set; } = CompositionPlacement.Overlay;

    [Category("1. 全般"), DisplayName("変換ボックスの文字の大きさ"),
     Description("自動: 入力欄の文字の高さに合わせます (小さな入力欄では小さく出ます)。入力欄の文字の高さが分からないアプリでは「中」になります。")]
    public CompositionSize CompositionSize { get; set; } = CompositionSize.Auto;

    [Category("1. 全般"), DisplayName("通知を出す"),
     Description("Meltype を有効・一時停止にしたときなどに、画面の右下に通知を出します (Windows の通知の音も鳴ります)。OFF にすると通知も音も出しません。")]
    public bool ShowNotifications { get; set; } = true;

    [Category("9. 更新"), DisplayName("自動で更新する"),
     Description("新しい版が公開されたら自動でダウンロードし、次に Meltype を起動したとき (Windows にサインインしたとき) に更新します。トレイの「更新して再起動」で今すぐ更新もできます。設定・学習データはそのまま残ります。")]
    public bool AutoUpdate { get; set; } = true;

    [Category("1. 全般"), DisplayName("入力方式"), Description("ローマ字入力 / かな入力 (JIS、α版: 試験中) / 両方を判定。Meltype キーボードでは、かな入力を選ぶと JIS かな配列で入力し (Shift+E = ぃ, Shift+Z = っ, Shift+ね = 、)、打ったキーの英字が英単語なら英字で見せます。「両方を判定」は IME 自動切替のみ (Meltype キーボードではローマ字入力)。")]
    public InputStyle InputStyle { get; set; } = InputStyle.Romaji;

    [Category("2. 判定"), DisplayName("自動判定の強さ"),
     Description("積極的 = 英語らしければすぐ英字 / 標準 = 短い語 (no, to, ga) は前後が英語のときだけ英字 / 慎重 = 確信度が高いときだけ英字 / 手動 = 自動では切り替えず提案だけ (変換ボックスで Tab を押すと提案どおり英字に)。Meltype キーボード・IME 自動切替・英数状態の検知・かな入力のすべてに効きます。")]
    public DetectionLevel DetectionLevel { get; set; } = DetectionLevel.Balanced;

    /// <summary>判定の強さを反映した日本語判定の閾値 (IME 自動切替・英数状態の検知)。</summary>
    [Browsable(false), JsonIgnore]
    public int EffectiveJapaneseThreshold => JapaneseThreshold + DetectionLevel switch
    {
        DetectionLevel.Aggressive => -1,
        DetectionLevel.Conservative => 3,
        _ => 0,
    };

    [Category("2. 判定"), DisplayName("日本語判定の閾値"), Description("JapaneseScore がこの値以上、かつ EnglishScore をこの値以上上回ったときだけ切り替えます。大きいほど誤爆が減ります。")]
    public int JapaneseThreshold { get; set; } = 4;

    [Category("2. 判定"), DisplayName("Typo 判定を使う"), Description("辞書語との編集距離 1 以内を補助的な日本語スコアとして加点します。Typo 一致だけでは切り替えません。")]
    public bool TypoEnabled { get; set; } = true;

    [Category("3. 保留"), DisplayName("最大保留キー数"), Description("判定のために保留する文字数の上限。超えたら判定不能としてそのまま出力します。")]
    public int MaxPendingKeys { get; set; } = 6;

    [Category("3. 保留"), DisplayName("無入力で出力するまでの時間 (ms)")]
    public int IdleFlushMs { get; set; } = 700;

    [Category("3. 保留"), DisplayName("最大保留時間 (ms)"), Description("最初のキーからこの時間が経ったら、判定途中でも保留分を出力します。")]
    public int MaxHoldMs { get; set; } = 2500;

    /// <summary>config.json の形式のバージョン。古い既定値を持つ設定ファイルを移行するのに使う。</summary>
    [Browsable(false)]
    public int SettingsVersion { get; set; } = CurrentVersion;

    /// <summary>初めて起動したときの「使い方」を見せたか (見せたら true にして、次からは出さない)。</summary>
    [Browsable(false)]
    public bool WelcomeShown { get; set; }

    /// <summary>プロファイル (仕事用・趣味用・SNS 用など)。設定画面の上と、トレイのメニューで切り替える。</summary>
    [Browsable(false)]
    public List<SettingsProfile> Profiles { get; set; } = [];

    /// <summary>使っているプロファイルの名前。</summary>
    [Browsable(false)]
    public string ActiveProfile { get; set; } = DefaultProfileName;

    public const string DefaultProfileName = "標準";

    /// <summary>
    /// プロファイルごとに変えない項目 (どのプロファイルでも共通)。Meltype の ON/OFF・ログ・更新と、内部で使う値。
    /// </summary>
    private static readonly HashSet<string> SharedKeys =
    [
        nameof(Profiles), nameof(ActiveProfile), nameof(SettingsVersion), nameof(WelcomeShown),
        nameof(Enabled), nameof(JapaneseKeyboardOnly), nameof(FileLog), nameof(LogTypedText), nameof(AutoUpdate),
    ];

    /// <summary>今の設定の値のうち、プロファイルに入れるもの。</summary>
    public JsonObject ProfileValues()
    {
        var values = JsonSerializer.SerializeToNode(this, JsonOptions)!.AsObject();
        foreach (var key in SharedKeys) values.Remove(key);
        return values;
    }

    /// <summary>プロファイルの名前の一覧 (使っているものも含む。並びは作った順)。</summary>
    public IReadOnlyList<string> ProfileNames => Profiles.Select(p => p.Name).ToList();

    /// <summary>
    /// プロファイルを切り替えた設定を返す。今の値は今のプロファイルに入れておき、切り替え先の値を読み込む。
    /// 切り替え先が無ければ、今の設定のまま (名前だけが変わることはない)。
    /// </summary>
    public Settings SwitchProfile(string name)
    {
        var current = Clone().Normalize();
        if (name == current.ActiveProfile || current.Profiles.FirstOrDefault(p => p.Name == name) is not { } target) return current;
        current.Profiles.First(p => p.Name == current.ActiveProfile).Values = current.ProfileValues();
        // 共通の項目は今の値のまま、プロファイルの項目だけを切り替え先の値にする
        var merged = JsonSerializer.SerializeToNode(current, JsonOptions)!.AsObject();
        foreach (var (key, value) in target.Values ?? []) merged[key] = value?.DeepClone();
        var next = merged.Deserialize<Settings>(JsonOptions) ?? current;
        next.ActiveProfile = name;
        return next.Normalize();
    }

    /// <summary>今の値をそのまま写した新しいプロファイルを足して、それに切り替えた設定を返す。名前が空・使われているなら null。</summary>
    public Settings? AddProfile(string name)
    {
        name = name.Trim();
        var current = Clone().Normalize();
        if (name.Length == 0 || current.Profiles.Any(p => p.Name == name)) return null;
        current.Profiles.First(p => p.Name == current.ActiveProfile).Values = current.ProfileValues();
        current.Profiles.Add(new SettingsProfile { Name = name, Values = current.ProfileValues() });
        current.ActiveProfile = name;
        return current;
    }

    /// <summary>プロファイルの名前を変えた設定を返す。新しい名前が空・使われているなら null。</summary>
    public Settings? RenameProfile(string oldName, string newName)
    {
        newName = newName.Trim();
        var current = Clone().Normalize();
        if (newName.Length == 0 || current.Profiles.Any(p => p.Name == newName) || current.Profiles.FirstOrDefault(p => p.Name == oldName) is not { } profile) return null;
        profile.Name = newName;
        if (current.ActiveProfile == oldName) current.ActiveProfile = newName;
        return current;
    }

    /// <summary>プロファイルを消した設定を返す。最後の 1 つは消せない (null)。使っているものを消したら、残りの最初のものに切り替える。</summary>
    public Settings? RemoveProfile(string name)
    {
        var current = Clone().Normalize();
        if (current.Profiles.Count <= 1 || current.Profiles.All(p => p.Name != name)) return null;
        if (current.ActiveProfile == name) current = current.SwitchProfile(current.Profiles.First(p => p.Name != name).Name);
        current.Profiles.RemoveAll(p => p.Name == name);
        return current;
    }

    /// <summary>書き出したプロファイルのファイルの印 (ほかの JSON と見分ける)。</summary>
    private const string ProfileFileFormat = "meltype-profile";

    /// <summary>
    /// 使っているプロファイルを、ほかの人に渡せる形 (JSON) にする。共通の項目 (ON/OFF・ログ・更新) は入れない。
    /// アプリ別設定 (プロセス名) は入るので、渡す前に見られてもよいか確かめてもらう。
    /// </summary>
    public string ExportProfile()
    {
        var file = new JsonObject
        {
            ["format"] = ProfileFileFormat,
            ["version"] = CurrentVersion,
            ["name"] = ActiveProfile,
            ["values"] = ProfileValues(),
        };
        return file.ToJsonString(JsonOptions);
    }

    /// <summary>
    /// 書き出したプロファイルを新しいプロファイルとして足し、それに切り替えた設定を返す。読めないファイルなら null。
    /// 同じ名前があれば「名前 (2)」にする。知らない項目と共通の項目は無視し、値は範囲に収める (Normalize)。
    /// </summary>
    public Settings? ImportProfile(string json)
    {
        JsonObject? values;
        string name;
        try
        {
            if (JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }) is not JsonObject file ||
                file["format"]?.GetValue<string>() != ProfileFileFormat || file["values"] is not JsonObject raw) return null;
            name = (file["name"]?.GetValue<string>() ?? "").Trim();
            // 既定の設定に、知っている項目だけを重ねてから読み直す (型の違う値はここで例外になる)
            var merged = JsonSerializer.SerializeToNode(new Settings().Normalize(), JsonOptions)!.AsObject();
            foreach (var (key, value) in raw)
            {
                if (merged.ContainsKey(key) && !SharedKeys.Contains(key)) merged[key] = value?.DeepClone();
            }
            values = (merged.Deserialize<Settings>(JsonOptions) ?? new Settings()).Normalize().ProfileValues();
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException or NotSupportedException)
        {
            return null;
        }
        if (name.Length == 0) name = "読み込んだプロファイル";
        if (name.Length > 50) name = name[..50];
        var current = Clone().Normalize();
        var unique = name;
        for (var i = 2; current.Profiles.Any(p => p.Name == unique); i++) unique = $"{name} ({i})";
        current.Profiles.First(p => p.Name == current.ActiveProfile).Values = current.ProfileValues();
        current.Profiles.Add(new SettingsProfile { Name = unique, Values = values });
        return current.SwitchProfile(unique);
    }

    public const int CurrentVersion = 5;

    [Category("4. セッション"), DisplayName("新しいセッションとみなす無入力時間 (ms)")]
    public int SessionIdleMs { get; set; } = 1500;

    [Category("4. セッション"), DisplayName("英語の後の Space では判定しない"), Description("英語と判定した直後に Space で区切られた次の単語は、保留せずそのまま通します (英文入力中の遅延を減らします)。")]
    public bool ContinueEnglishAfterSpace { get; set; }

    [Category("5. 学習"), DisplayName("ユーザー学習を使う")]
    public bool LearningEnabled { get; set; } = true;

    [Category("5. 学習"), DisplayName("誤判定フィードバックの受付時間 (ms)"), Description("自動判定の後、この時間内に 半角/全角 などの IME 切替キーが押されたら誤判定として学習します。")]
    public int FeedbackWindowMs { get; set; } = 4000;

    [Category("6. IME"), DisplayName("TSF を使う"), Description("IMM32 で入力言語を切り替えられなかったとき、TSF のプロファイル切替を試します。")]
    public bool UseTsf { get; set; } = true;

    [Category("6. IME"), DisplayName("IME 操作のタイムアウト (ms)")]
    public int ImeTimeoutMs { get; set; } = 300;

    [Category("7. アプリ"), DisplayName("貼り付けで入力するアプリ"),
     Description("確定した文字を 1 文字ずつ送ると取り違えるアプリ (DaVinci Resolve で「あいうえお」→「あああああ」)。ここに書いたアプリ (プロセス名、カンマ区切り) では、クリップボードを使って貼り付けで入れます (元のクリップボードの中身は戻します)。")]
    public string PasteApps { get; set; } = "Resolve.exe";

    /// <summary>このアプリでは確定した文字を貼り付けで入れるか (<see cref="PasteApps"/>)。</summary>
    public bool UsesPaste(string? processName) =>
        !string.IsNullOrEmpty(processName) &&
        (PasteApps ?? "").Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries).Any(app => string.Equals(app.Trim(), processName, StringComparison.OrdinalIgnoreCase));

    [Category("7. アプリ"), DisplayName("全画面アプリでは無効"), Description("ゲームや動画など全画面のウィンドウではキーを保留しません。")]
    public bool ExcludeFullscreen { get; set; } = true;

    [Category("7. アプリ"), DisplayName("ゲームでは止める"),
     Description("Steam・Epic Games・Riot Games・EA・Ubisoft・Battle.net・Xbox のゲームのフォルダーにあるアプリ (ウィンドウ表示のゲームも) では、Meltype は何もしません (キーを横取りせず、Windows の IME にも触らないので、ゲームのチャットは Windows の IME で打てます)。ほかのゲームは、アプリ別設定で種類を「ゲーム」にしてください。")]
    public bool StopInGames { get; set; } = true;

    [Category("7. アプリ"), DisplayName("アプリ別設定"), Description("プロセス名ごとに、自動切替の ON/OFF と種類を指定します。種類「コード」(コードエディター・ターミナル) では基本は英数のままで、コメント (// # -- など) と文字列 (\"…\" など) の中だけ日本語を判定します。コードの行で 半角/全角 を押すと、その行だけ日本語で入力できます。README.md などの文章ファイルを開いているときは一般として扱います。")]
    public List<AppRule> AppRules { get; set; } = DefaultAppRules();

    [Category("7. アプリ"), DisplayName("独自の種類"), Description("アプリ別設定の「種類」に使える、自分で作る種類です。一般 / コード を元に、判定の強さ・ライブ変換・最初は英数にするか を変えられます (「全体と同じ」なら上の設定のまま)。")]
    public List<AppKind> AppKinds { get; set; } = [];

    [Category("8. ログ"), DisplayName("ファイルにログを書く"), Description("%LOCALAPPDATA%\\Meltype\\meltype.log にログを書きます (OFF でも、トレイの「ログ / 判定理由」で見られるログは Meltype が動いている間だけメモリに残ります)。判定した語の先頭の数文字・アプリ名・入力欄の名前が含まれます。確定した文字列などは「ログに入力した文字を残す」が ON のときだけ残ります。")]
    public bool FileLog { get; set; }

    [Category("8. ログ"), DisplayName("ログに入力した文字を残す"), Description("確定した文字列・打った英字・読み・直した語をログに残します (不具合を調べるとき用)。OFF なら文字数だけを残します。パスワード欄では入力を扱わないので残りません。")]
    public bool LogTypedText { get; set; }

    public static List<AppRule> DefaultAppRules() =>
    [
        // キー入力が別のマシン/VM に届くアプリ。保留すると相手側の IME 状態と食い違う。
        new() { Process = "mstsc.exe", Enabled = false },
        new() { Process = "msrdc.exe", Enabled = false },
        new() { Process = "vmconnect.exe", Enabled = false },
        new() { Process = "VirtualBoxVM.exe", Enabled = false },
        new() { Process = "vmware-vmx.exe", Enabled = false },
        new() { Process = "vmware.exe", Enabled = false },
        .. CodeApps.Select(process => new AppRule { Process = process, Enabled = true, Profile = AppProfile.Code }),
    ];

    /// <summary>既定で「コード」として扱うアプリ (コードエディター・IDE・ターミナル)。</summary>
    public static readonly string[] CodeApps =
    [
        "Code.exe", "Code - Insiders.exe", "Cursor.exe", "Windsurf.exe", "zed.exe", "devenv.exe",
        "idea64.exe", "pycharm64.exe", "webstorm64.exe", "rider64.exe", "clion64.exe", "goland64.exe",
        "phpstorm64.exe", "rubymine64.exe", "datagrip64.exe", "studio64.exe", "sublime_text.exe", "notepad++.exe",
        "WindowsTerminal.exe", "cmd.exe", "powershell.exe", "pwsh.exe", "wezterm-gui.exe", "alacritty.exe", "mintty.exe",
        // アプリの中にスクリプトエディターがある 3DCG ソフト (Maya の Script Editor)
        "maya.exe",
    ];

    public bool IsAppEnabled(string? processName) => FindRule(processName)?.Enabled ?? true;

    /// <summary>
    /// ゲームとして Meltype を止めるか: アプリ別設定の種類が「ゲーム」か、アプリ別設定が無く、ゲームのフォルダーにあるアプリ (looksLikeGame) で「ゲームでは止める」が ON。
    /// </summary>
    public bool IsGame(string? processName, bool looksLikeGame) =>
        ProfileFor(processName) == AppProfile.Game || (StopInGames && looksLikeGame && FindRule(processName) is null);

    /// <summary>アプリの種類 (アプリ別設定に無ければ一般)。独自の種類なら、その元にした種類。</summary>
    public AppProfile ProfileFor(string? processName) =>
        KindFor(processName)?.Base ?? FindRule(processName)?.Profile ?? AppProfile.General;

    /// <summary>アプリに割り当てた独自の種類 (無ければ null)。</summary>
    public AppKind? KindFor(string? processName) =>
        FindRule(processName)?.Kind is { Length: > 0 } name ? AppKinds.FirstOrDefault(k => k.Name == name) : null;

    /// <summary>アプリの独自の種類で変えた項目 (判定の強さ・ライブ変換) を反映した設定。変えていなければ自分自身。</summary>
    public Settings ForApp(string? processName)
    {
        if (KindFor(processName) is not { } kind || (kind.DetectionLevel is null && kind.LiveConversion is null)) return this;
        var copy = Clone();
        if (kind.DetectionLevel is { } level) copy.DetectionLevel = level;
        if (kind.LiveConversion is { } live) copy.LiveConversion = live;
        return copy;
    }

    private AppRule? FindRule(string? processName)
    {
        if (string.IsNullOrEmpty(processName)) return null;
        foreach (var rule in AppRules)
        {
            if (string.Equals(rule.Process, processName, StringComparison.OrdinalIgnoreCase)) return rule;
        }
        return null;
    }

    public Settings Clone()
    {
        var copy = (Settings)MemberwiseClone();
        copy.AppRules = AppRules.Select(r => new AppRule { Process = r.Process, Enabled = r.Enabled, Profile = r.Profile, Kind = r.Kind }).ToList();
        copy.AppKinds = AppKinds.Select(k => k.Clone()).ToList();
        copy.Profiles = (Profiles ?? []).Select(p => p.Clone()).ToList();
        return copy;
    }

    /// <summary>手で編集された config.json の極端な値を安全な範囲に収める。</summary>
    public Settings Normalize()
    {
        JapaneseThreshold = Math.Clamp(JapaneseThreshold, 2, 20);
        MaxPendingKeys = Math.Clamp(MaxPendingKeys, 2, 12);
        IdleFlushMs = Math.Clamp(IdleFlushMs, 100, 3000);
        MaxHoldMs = Math.Clamp(MaxHoldMs, 200, 5000);
        SessionIdleMs = Math.Clamp(SessionIdleMs, 300, 60000);
        FeedbackWindowMs = Math.Clamp(FeedbackWindowMs, 500, 30000);
        ImeTimeoutMs = Math.Clamp(ImeTimeoutMs, 50, 2000);
        AppRules ??= [];
        AppKinds ??= [];
        AppRules.RemoveAll(r => r is null || string.IsNullOrWhiteSpace(r.Process));
        // プロファイル: 名前の無いもの・同じ名前のものは除き、使っているプロファイルは必ず一覧にある
        Profiles ??= [];
        Profiles.RemoveAll(p => p is null || string.IsNullOrWhiteSpace(p.Name));
        Profiles = Profiles.GroupBy(p => p.Name.Trim()).Select(g => { var p = g.First(); p.Name = g.Key; return p; }).ToList();
        if (string.IsNullOrWhiteSpace(ActiveProfile)) ActiveProfile = Profiles.FirstOrDefault()?.Name ?? DefaultProfileName;
        ActiveProfile = ActiveProfile.Trim();
        if (Profiles.All(p => p.Name != ActiveProfile)) Profiles.Insert(0, new SettingsProfile { Name = ActiveProfile });
        foreach (var rule in AppRules) rule.Process = rule.Process.Trim();
        return this;
    }

    /// <summary>古い既定値のままの項目だけを新しい既定値に更新する (ユーザーが変えた値は触らない)。</summary>
    internal bool Migrate()
    {
        if (SettingsVersion >= CurrentVersion) return false;
        if (SettingsVersion < 2)
        {
            // v1 の 400ms / 1200ms では、ゆっくり打つと判定前に保留が切れて日本語を見逃していた。
            if (IdleFlushMs == 400) IdleFlushMs = 700;
            if (MaxHoldMs == 1200) MaxHoldMs = 2500;
        }
        // v3: Meltype キーボード (変換ボックス) を追加。v2 以前の config.json には Mode が無いので、
        // 読み込み時に初期値 (Keyboard) になる。
        if (SettingsVersion < 4)
        {
            // v4: アプリの種類 (コード) を追加。コードエディター・ターミナルを「コード」にする (ユーザーが OFF にしたものはそのまま)。
            foreach (var process in CodeApps)
            {
                var rule = FindRule(process);
                if (rule is null) AppRules.Add(new AppRule { Process = process, Enabled = true, Profile = AppProfile.Code });
                else rule.Profile = AppProfile.Code;
            }
        }
        if (SettingsVersion < 5)
        {
            // v5: 既定の「コード」のアプリに Maya (Script Editor) を追加。ユーザーが自分で入れていれば、そのまま。
            if (FindRule("maya.exe") is null) AppRules.Add(new AppRule { Process = "maya.exe", Enabled = true, Profile = AppProfile.Code });
        }
        SettingsVersion = CurrentVersion;
        return true;
    }

    public static Settings Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return new Settings();
            var json = File.ReadAllText(path);
            var settings = JsonSerializer.Deserialize<Settings>(json, JsonOptions) ?? new Settings();
            if (!json.Contains(nameof(SettingsVersion))) settings.SettingsVersion = 1;
            if (settings.Migrate()) settings.Save(path);
            return settings.Normalize();
        }
        catch (Exception ex)
        {
            // 壊れた設定で起動不能にしない。元ファイルは退避して既定値で動く。
            try { File.Copy(path, path + ".broken", overwrite: true); } catch { }
            Diagnostics.Log.Warn($"config.json を読み込めなかったため既定値を使います: {ex.Message}");
            return new Settings();
        }
    }

    /// <summary>config.json と同じ形式の文字列 (変更があったかを比べるのに使う)。</summary>
    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this, JsonOptions));
        File.Move(temp, path, overwrite: true);
    }
}

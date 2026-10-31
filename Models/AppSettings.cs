using System;
using System.Collections.Generic;

namespace TekitouKeyborad.Models;

/// <summary>
/// 校正履歴の1件を表すデータモデル
/// </summary>
public class ProofreadHistoryItem
{
    /// <summary>実行日時</summary>
    public DateTime Timestamp { get; set; } = DateTime.Now;

    /// <summary>校正前の元テキスト</summary>
    public string OriginalText { get; set; } = string.Empty;

    /// <summary>校正後のテキスト</summary>
    public string ProofreadText { get; set; } = string.Empty;

    /// <summary>使用したモデル名</summary>
    public string ModelName { get; set; } = string.Empty;
}

/// <summary>
/// アプリケーション全体の設定を保持するモデルクラス
/// </summary>
public class AppSettings
{
    // --- ウィンドウ位置・サイズの設定 ---
    /// <summary>ウィンドウの左端座標</summary>
    public double? WindowLeft { get; set; }

    /// <summary>ウィンドウの上端座標</summary>
    public double? WindowTop { get; set; }

    /// <summary>ウィンドウの幅</summary>
    public double WindowWidth { get; set; } = 920;

    /// <summary>ウィンドウの高さ</summary>
    public double WindowHeight { get; set; } = 680;

    /// <summary>最大化状態かどうか</summary>
    public bool IsMaximized { get; set; } = false;

    // --- 常駐・起動動作の設定 ---
    /// <summary>Windowsへのサインイン時に自動起動するか</summary>
    public bool StartWithWindows { get; set; } = false;

    /// <summary>起動時にシステムトレイへ最小化した状態で開始するか</summary>
    public bool StartMinimized { get; set; } = false;

    /// <summary>ウィンドウの閉じるボタン押下時に終了せずタスクトレイに格納するか</summary>
    public bool CloseToTray { get; set; } = true;

    /// <summary>校正完了時に画面右下のトーストポップアップ通知を表示するか</summary>
    public bool ShowNotification { get; set; } = true;

    /// <summary>校正完了後にクリップボード経由で自動的にCtrl+V貼り付けを行うか</summary>
    public bool AutoPaste { get; set; } = false;

    // --- ショートカットキーの設定 ---
    /// <summary>
    /// Win32 RegisterHotKey 用の修飾キーフラグ
    /// (MOD_ALT = 0x0001, MOD_CONTROL = 0x0002, MOD_SHIFT = 0x0004, MOD_WIN = 0x0008)
    /// デフォルト: Ctrl + Alt (0x0002 | 0x0001 = 3)
    /// </summary>
    public uint HotKeyModifiers { get; set; } = 0x0002 | 0x0001;

    /// <summary>
    /// Win32 仮想キーコード (VK_*)
    /// デフォルト: 'K' (0x4B)
    /// </summary>
    public uint HotKeyVirtualKey { get; set; } = 0x4B;

    /// <summary>画面表示用のショートカットキー文字列</summary>
    public string HotKeyDisplayText { get; set; } = "Ctrl + Alt + K";

    // --- Microsoft Foundry Local の設定 ---
    /// <summary>接続方式 ("FoundryLocalSdk" または "OpenAICompatibleEndpoint")</summary>
    public string ConnectionMode { get; set; } = "FoundryLocalSdk";

    /// <summary>Foundry Local で使用するモデルのエイリアス名</summary>
    public string SelectedModelAlias { get; set; } = "phi-3.5-mini";

    /// <summary>OpenAI互換ローカルエンドポイントを使用する場合のURL</summary>
    public string CustomEndpointUrl { get; set; } = "http://127.0.0.1:5272/v1/chat/completions";

    /// <summary>OpenAI互換エンドポイント用の任意APIキー</summary>
    public string CustomApiKey { get; set; } = "foundry-local";

    /// <summary>生成時のTemperature (0.0 ～ 1.0)</summary>
    public float Temperature { get; set; } = 0.2f;

    /// <summary>最大出力トークン数</summary>
    public int MaxTokens { get; set; } = 1024;

    /// <summary>校正プリセットの識別子 ("Standard", "Business", "Concise", "Custom", "Romaji")</summary>
    public string PromptPreset { get; set; } = "Standard";

    /// <summary>AIに渡すシステムプロンプト（校正指示文）</summary>
    public string SystemPrompt { get; set; } = DefaultSystemPromptJa;

    // --- 表示言語・テーマの設定 ---
    /// <summary>言語モード ("Auto", "ja", "en")</summary>
    public string LanguageMode { get; set; } = "Auto";

    /// <summary>テーマモード ("Auto", "Light", "Dark")</summary>
    public string ThemeMode { get; set; } = "Auto";

    // --- 履歴 ---
    /// <summary>最近の校正履歴（最大30件保存）</summary>
    public List<ProofreadHistoryItem> HistoryItems { get; set; } = new();

    /// <summary>日本語の標準システムプロンプト</summary>
    public const string DefaultSystemPromptJa =
        "あなたは優秀な文章校正アシスタントです。ユーザーから渡されたテキストの誤字脱字、文法的な誤り、不自然な表現を修正し、自然で読みやすい文章に校正してください。\n" +
        "【重要なルール】\n" +
        "・元の文章の意味やトーンは維持してください。\n" +
        "・校正後のテキストのみを出力し、解説や挨拶、引用符（「」や```など）は一切付けないでください。";

    /// <summary>ビジネス丁寧語変換用のシステムプロンプト</summary>
    public const string BusinessSystemPromptJa =
        "あなたはビジネス文書の校正アシスタントです。ユーザーから渡されたテキストの誤字脱字を修正し、ビジネスシーンにふさわしい丁寧で礼儀正しい表現（敬語）に整えてください。\n" +
        "【重要なルール】\n" +
        "・校正後のテキストのみを出力し、解説や挨拶、引用符は一切付けないでください。";

    /// <summary>簡潔・要約用のシステムプロンプト</summary>
    public const string ConciseSystemPromptJa =
        "あなたは文章編集アシスタントです。ユーザーから渡されたテキストの誤字脱字を修正し、冗長な表現を省いて簡潔で分かりやすい文章に整えてください。\n" +
        "【重要なルール】\n" +
        "・校正後のテキストのみを出力し、解説や挨拶、引用符は一切付けないでください。";

    /// <summary>ローマ字の日本語を漢字・かな交じりの日本語に校正するシステムプロンプト</summary>
    public const string RomajiSystemPromptJa =
        "あなたは日本語の文章校正アシスタントです。入力は日本語をローマ字（アルファベット）で書いた文章です。意味を読み取り、漢字・ひらがな・カタカナを使った自然な日本語に変換し、誤字脱字や文法も校正してください。\n" +
        "【重要なルール】\n" +
        "・入力に「ローマ字原文」と「かな読み」がある場合は両方を参考にし、原文の単語や文脈を使って正しい漢字・文法に直してください。\n" +
        "・単語間に空白がない入力、ヘボン式・訓令式の違い、日本語とローマ字の混在、軽微な打ち間違いにも対応してください。\n" +
        "・元の意味やトーン、日時・場所・対象・行為などの情報は省略したり、別の意味に変えたりしないでください。\n" +
        "・固有名詞・略語・URL・メールアドレス・コードは原文の綴りをそのまま残してください。略語の展開や翻訳はしないでください（VS CodeはVS Codeのまま残す）。\n" +
        "・校正後の日本語本文だけを出力し、解説や挨拶、引用符、変換前のローマ字文は付けないでください。\n" +
        "【例】\n" +
        "watashiwaashitakaiginisankashimasu → 私は明日、会議に参加します。\n" +
        "yorosiku onegaisimasu → よろしくお願いします。\n" +
        "asu no kaigi de Windows no settei wo kakunin shimasu → 明日の会議でWindowsの設定を確認します。\n" +
        "回答は校正後の日本語本文のみです。原文・かな読みのラベルや解説、注釈は絶対に出力しないでください。";
}

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
    public double WindowLeft { get; set; } = double.NaN;

    /// <summary>ウィンドウの上端座標</summary>
    public double WindowTop { get; set; } = double.NaN;

    /// <summary>ウィンドウの幅</summary>
    public double WindowWidth { get; set; } = 920;

    /// <summary>ウィンドウの高さ</summary>
    public double WindowHeight { get; set; } = 680;

    /// <summary>最大化状態かどうか</summary>
    public bool IsMaximized { get; set; } = false;

    // --- 常駐・起動動作の設定 ---
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

    /// <summary>校正プリセットの識別子 ("Standard", "Business", "Concise", "Custom")</summary>
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
}


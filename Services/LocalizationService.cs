using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;

namespace TekitouKeyborad.Services;

/// <summary>
/// 多言語表示（日本語・英語）を管理し、Windowsの表示言語設定に応じて切り替えるサービスクラス
/// </summary>
public class LocalizationService : INotifyPropertyChanged
{
    public static LocalizationService Instance { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? LanguageChanged;

    private string _currentCulture = "ja";

    /// <summary>現在適用されている言語コード ("ja" または "en")</summary>
    public string CurrentLanguage => _currentCulture;

    /// <summary>日本語翻訳辞書</summary>
    private static readonly Dictionary<string, string> JaStrings = new()
    {
        ["AppTitle"] = "適当キーボード",
        ["AppSubtitle"] = "Microsoft Foundry Local 常駐型テキスト校正ツール",
        ["TabHome"] = "ホーム・履歴",
        ["TabModels"] = "Foundry モデル管理",
        ["TabSettings"] = "設定",
        ["StatusReady"] = "待機中（ショートカットキーで選択テキストを校正）",
        ["StatusProofreading"] = "Microsoft Foundry でテキストを校正中...",
        ["StatusCompleted"] = "校正完了！クリップボードにコピーしました",
        ["StatusAutoPasteSkipped"] = "結果をコピーしました。入力先・クリップボード・修飾キーの状態を確認できないため、自動貼り付けを中止しました。",
        ["ClipboardReadFailed"] = "クリップボードを読み取れませんでした。他のアプリの使用状況を確認して再試行してください。",
        ["ClipboardWriteFailed"] = "クリップボードにコピーできませんでした。結果を画面から再コピーしてください。",
        ["InputSendFailed"] = "コピーまたは貼り付けのキー送信に失敗しました。対象アプリの権限などを確認してください。",
        ["CaptureTargetUnavailable"] = "選択した入力欄を確認できませんでした。対象アプリを手前にして再試行してください。",
        ["CaptureModifiersHeld"] = "キーを離すまでに時間がかかりました。ショートカットを押した後は、すべてのキーを離してください。",
        ["CaptureTargetChanged"] = "コピー中に入力先が変わったため中止しました。文字を選択し、別の画面を操作せずに再試行してください。",
        ["CaptureClipboardChanged"] = "コピー中にクリップボードが別の内容に更新されたため中止しました。再試行してください。",
        ["CaptureCopyNotConfirmed"] = "選択文字のコピーを確認できませんでした。選択範囲と対象アプリの権限を確認してください。",
        ["StatusNoText"] = "テキストが選択されていないか、取得できませんでした",
        ["StatusError"] = "校正エラー: ",
        ["ActiveShortcut"] = "現在のショートカット",
        ["ActiveModel"] = "使用中のモデル",
        ["QuickAutoPaste"] = "校正後に自動で貼り付け (Ctrl+V)",
        ["TestSectionTitle"] = "テスト校正プレイグラウンド",
        ["TestInputPlaceholder"] = "ローマ字の日本語（例: yorosiku onegaisimasu）は、設定で「ローマ字 → 日本語」プリセットを選んで保存すると、自然な日本語に校正できます。通常の英文校正には「標準校正」を使ってください。",
        ["BtnRunProofread"] = "テスト校正を実行",
        ["BtnCopyResult"] = "結果をコピー",
        ["HistorySectionTitle"] = "最近の校正履歴",
        ["BtnClearHistory"] = "履歴をクリア",
        ["ColTime"] = "日時",
        ["ColOriginal"] = "元のテキスト",
        ["ColProofread"] = "校正後のテキスト",
        ["ColModel"] = "モデル",
        ["EmptyHistory"] = "まだ校正履歴がありません。テキストを選択してショートカットキーを押すとここに履歴が表示されます。",
        ["ModelsHeaderTitle"] = "Microsoft Foundry Local モデルカタログ",
        ["ModelsHeaderDesc"] = "ローカルPC上でオフライン推論を行うAIモデルをダウンロード・ロードします。初回校正時に自動ロードされます。",
        ["BtnRefreshCatalog"] = "カタログ更新",
        ["BtnDownloadRegisterEps"] = "GPU/NPU アクセラレーション(EP)を検出・登録",
        ["ColAlias"] = "モデルエイリアス",
        ["ColDisplayName"] = "表示名",
        ["ColSize"] = "サイズ(MB)",
        ["ColCached"] = "ダウンロード済",
        ["ColLoaded"] = "メモリロード済",
        ["BtnSelectAndLoad"] = "選択してロード",
        ["BtnDownloadModel"] = "ダウンロード",
        ["BtnUnloadModel"] = "アンロード",
        ["Yes"] = "はい",
        ["No"] = "いいえ",
        ["SettingsShortcutHeader"] = "ショートカットキー設定",
        ["SettingsShortcutDesc"] = "入力欄をクリックし、登録したいキーの組み合わせ（例: Ctrl + Alt + K）を押してください。",
        ["BtnApplyShortcut"] = "ショートカットを適用",
        ["SettingsConnectionHeader"] = "Microsoft Foundry 接続方式",
        ["ModeFoundrySdk"] = "Foundry Local SDK (インプロセス直接実行・推奨)",
        ["ModeOpenAiEndpoint"] = "OpenAI 互換ローカルエンドポイント (foundry service HTTP 経由)",
        ["EndpointUrlLabel"] = "エンドポイントURL:",
        ["SettingsPromptHeader"] = "校正プロンプト設定",
        ["PresetLabel"] = "プリセット:",
        ["PresetStandard"] = "標準校正（誤字脱字・自然な文章）",
        ["PresetBusiness"] = "ビジネス丁寧語（敬語・メール向け）",
        ["PresetConcise"] = "簡潔・要約（無駄を省く）",
        ["PresetCustom"] = "カスタム指示",
        ["PresetRomaji"] = "ローマ字 → 日本語",
        ["SystemPromptLabel"] = "システムプロンプト（AIへの指示）:",
        ["TemperatureLabel"] = "Temperature (創造性):",
        ["SettingsBehaviorHeader"] = "動作・表示設定",
        ["ChkShowNotification"] = "校正完了時に画面右下にトースト通知を表示する",
        ["ChkAutoPaste"] = "校正完了後に元のウィンドウへ自動貼り付け (Ctrl+V) する",
        ["ChkStartWithWindows"] = "Windows起動時に自動起動する（サインイン時）",
        ["StartupSettingsFailed"] = "Windows自動起動の設定を確認・変更できませんでした: ",
        ["StartupFolderUnavailable"] = "スタートアップフォルダーを取得できませんでした。",
        ["StartupExecutableUnavailable"] = "自動起動する実行ファイルを確認できませんでした。アプリのEXEから起動してください。",
        ["ChkStartMinimized"] = "アプリ起動時にシステムトレイへ最小化して開始する",
        ["ChkCloseToTray"] = "ウィンドウを閉じる(×)ときに終了せずシステムトレイに常駐する",
        ["LanguageLabel"] = "表示言語 (Language):",
        ["LangAuto"] = "自動 (Windowsの表示言語に従う)",
        ["LangJa"] = "日本語 (Japanese)",
        ["LangEn"] = "英語 (English)",
        ["ThemeLabel"] = "アプリのテーマ:",
        ["ThemeAuto"] = "自動 (Windowsのテーマ設定に従う)",
        ["ThemeLight"] = "ライトモード (Light)",
        ["ThemeDark"] = "ダークモード (Dark)",
        ["BtnSaveSettings"] = "設定を保存",
        ["SettingsSavedMsg"] = "設定を保存しました。",
        ["TrayOpen"] = "適当キーボードを開く",
        ["TrayProofreadClipboard"] = "現在のクリップボードを校正",
        ["TrayExit"] = "終了",
        ["ToastProofreadingTitle"] = "適当キーボード - 校正中...",
        ["ToastCompletedTitle"] = "適当キーボード - 校正完了（クリップボードに格納済）",
        ["ToastAutoPasteSkippedTitle"] = "適当キーボード - 自動貼り付け中止（結果コピー済）",
        ["ToastErrorTitle"] = "適当キーボード - エラー",
        ["ToastOriginalLabel"] = "校正前:",
        ["ToastResultLabel"] = "校正後:",
        ["ToastCopyBtn"] = "再コピー",
        ["ToastCloseBtn"] = "閉じる"
    };

    /// <summary>英語翻訳辞書</summary>
    private static readonly Dictionary<string, string> EnStrings = new()
    {
        ["AppTitle"] = "Tekitou Keyboard",
        ["AppSubtitle"] = "Microsoft Foundry Local Resident Text Proofreader",
        ["TabHome"] = "Home & History",
        ["TabModels"] = "Foundry Models",
        ["TabSettings"] = "Settings",
        ["StatusReady"] = "Ready (Select text and press shortcut key to proofread)",
        ["StatusProofreading"] = "Proofreading text with Microsoft Foundry...",
        ["StatusCompleted"] = "Proofreading complete! Copied to clipboard.",
        ["StatusAutoPasteSkipped"] = "Result copied. Auto-paste was canceled because the input target, clipboard, or modifier key state could not be verified.",
        ["ClipboardReadFailed"] = "Could not read the clipboard. Check whether another app is using it and try again.",
        ["ClipboardWriteFailed"] = "Could not copy to the clipboard. Copy the result again from the app.",
        ["InputSendFailed"] = "Could not send the copy or paste keys. Check the target app's permissions.",
        ["CaptureTargetUnavailable"] = "Could not identify the selected input field. Bring the target app to the foreground and try again.",
        ["CaptureModifiersHeld"] = "Timed out waiting for key release. Release all keys after pressing the shortcut.",
        ["CaptureTargetChanged"] = "Capture canceled because the input target changed. Select the text and try again without switching apps.",
        ["CaptureClipboardChanged"] = "Capture canceled because the clipboard was replaced while copying. Try again.",
        ["CaptureCopyNotConfirmed"] = "Could not confirm copying the selected text. Check the selection and the target app's permissions.",
        ["StatusNoText"] = "No text was selected or could not be captured.",
        ["StatusError"] = "Proofreading error: ",
        ["ActiveShortcut"] = "Active Shortcut",
        ["ActiveModel"] = "Active Model",
        ["QuickAutoPaste"] = "Auto-paste after proofreading (Ctrl+V)",
        ["TestSectionTitle"] = "Test Proofreading Playground",
        ["TestInputPlaceholder"] = "For romanized Japanese (e.g. yorosiku onegaisimasu), select and save the 'Romaji → Japanese' preset in Settings. Use 'Standard Proofreading' for ordinary English proofreading.",
        ["BtnRunProofread"] = "Run Test Proofread",
        ["BtnCopyResult"] = "Copy Result",
        ["HistorySectionTitle"] = "Recent Proofreading History",
        ["BtnClearHistory"] = "Clear History",
        ["ColTime"] = "Time",
        ["ColOriginal"] = "Original Text",
        ["ColProofread"] = "Proofread Text",
        ["ColModel"] = "Model",
        ["EmptyHistory"] = "No proofreading history yet. Select text in any app and press your shortcut key.",
        ["ModelsHeaderTitle"] = "Microsoft Foundry Local Model Catalog",
        ["ModelsHeaderDesc"] = "Download and load local AI models for offline inference on your PC.",
        ["BtnRefreshCatalog"] = "Refresh Catalog",
        ["BtnDownloadRegisterEps"] = "Detect & Register GPU/NPU Execution Providers",
        ["ColAlias"] = "Model Alias",
        ["ColDisplayName"] = "Display Name",
        ["ColSize"] = "Size (MB)",
        ["ColCached"] = "Downloaded",
        ["ColLoaded"] = "Loaded",
        ["BtnSelectAndLoad"] = "Select & Load",
        ["BtnDownloadModel"] = "Download",
        ["BtnUnloadModel"] = "Unload",
        ["Yes"] = "Yes",
        ["No"] = "No",
        ["SettingsShortcutHeader"] = "Shortcut Key Configuration",
        ["SettingsShortcutDesc"] = "Click the box below and press your desired key combination (e.g., Ctrl + Alt + K).",
        ["BtnApplyShortcut"] = "Apply Shortcut",
        ["SettingsConnectionHeader"] = "Microsoft Foundry Connection Mode",
        ["ModeFoundrySdk"] = "Foundry Local SDK (In-Process Native Execution - Recommended)",
        ["ModeOpenAiEndpoint"] = "OpenAI-Compatible Local Endpoint (via foundry service HTTP)",
        ["EndpointUrlLabel"] = "Endpoint URL:",
        ["SettingsPromptHeader"] = "Proofreading Prompt Configuration",
        ["PresetLabel"] = "Preset:",
        ["PresetStandard"] = "Standard Proofreading (Grammar & Natural Flow)",
        ["PresetBusiness"] = "Business Polite (Formal & Professional)",
        ["PresetConcise"] = "Concise & Clear (Remove Redundancy)",
        ["PresetCustom"] = "Custom Instructions",
        ["PresetRomaji"] = "Romaji → Japanese",
        ["SystemPromptLabel"] = "System Prompt (Instructions for AI):",
        ["TemperatureLabel"] = "Temperature:",
        ["SettingsBehaviorHeader"] = "Behavior & Appearance",
        ["ChkShowNotification"] = "Show toast notification at bottom-right when proofreading completes",
        ["ChkAutoPaste"] = "Automatically paste (Ctrl+V) into active window after proofreading",
        ["ChkStartWithWindows"] = "Start automatically with Windows (at sign-in)",
        ["StartupSettingsFailed"] = "Could not read or change the Windows startup setting: ",
        ["StartupFolderUnavailable"] = "The Startup folder could not be located.",
        ["StartupExecutableUnavailable"] = "The startup executable could not be identified. Launch the app from its EXE file.",
        ["ChkStartMinimized"] = "Start minimized in the system tray on launch",
        ["ChkCloseToTray"] = "Minimize to system tray instead of exiting when closing window (X)",
        ["LanguageLabel"] = "Display Language:",
        ["LangAuto"] = "Auto (Follow Windows Display Language)",
        ["LangJa"] = "Japanese (日本語)",
        ["LangEn"] = "English",
        ["ThemeLabel"] = "App Theme:",
        ["ThemeAuto"] = "Auto (Follow Windows App Theme)",
        ["ThemeLight"] = "Light Mode",
        ["ThemeDark"] = "Dark Mode",
        ["BtnSaveSettings"] = "Save Settings",
        ["SettingsSavedMsg"] = "Settings have been saved.",
        ["TrayOpen"] = "Open Tekitou Keyboard",
        ["TrayProofreadClipboard"] = "Proofread Current Clipboard",
        ["TrayExit"] = "Exit",
        ["ToastProofreadingTitle"] = "Tekitou Keyboard - Proofreading...",
        ["ToastCompletedTitle"] = "Tekitou Keyboard - Copied to Clipboard",
        ["ToastAutoPasteSkippedTitle"] = "Tekitou Keyboard - Auto-paste Canceled (Copied)",
        ["ToastErrorTitle"] = "Tekitou Keyboard - Error",
        ["ToastOriginalLabel"] = "Original:",
        ["ToastResultLabel"] = "Proofread:",
        ["ToastCopyBtn"] = "Copy Again",
        ["ToastCloseBtn"] = "Close"
    };

    /// <summary>
    /// インデクサーでキーに対応する現在の言語の文字列を取得する
    /// </summary>
    public string this[string key] => Get(key);

    /// <summary>
    /// 指定キーの翻訳文字列を取得する
    /// </summary>
    public string Get(string key)
    {
        var dict = _currentCulture == "en" ? EnStrings : JaStrings;
        if (dict.TryGetValue(key, out string? value))
        {
            return value;
        }
        return JaStrings.TryGetValue(key, out string? fallback) ? fallback : key;
    }

    /// <summary>
    /// 設定値（"Auto", "ja", "en"）とWindowsの表示言語から表示言語を適用する
    /// </summary>
    public void ApplyLanguage(string languageMode)
    {
        string resolved;
        if (string.Equals(languageMode, "ja", StringComparison.OrdinalIgnoreCase))
        {
            resolved = "ja";
        }
        else if (string.Equals(languageMode, "en", StringComparison.OrdinalIgnoreCase))
        {
            resolved = "en";
        }
        else
        {
            // Windowsの表示言語（CurrentUICulture）を確認する
            var uiCulture = CultureInfo.CurrentUICulture;
            resolved = uiCulture.TwoLetterISOLanguageName.Equals("ja", StringComparison.OrdinalIgnoreCase)
                ? "ja"
                : "en";
        }

        if (_currentCulture != resolved)
        {
            _currentCulture = resolved;
        }

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }
}

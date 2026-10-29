using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TekitouKeyborad.Models;
using TekitouKeyborad.Services;
using TekitouKeyborad.Views;
using Forms = System.Windows.Forms;

namespace TekitouKeyborad;

/// <summary>
/// メインウィンドウ（ホーム・履歴、Foundryモデル管理、設定画面）およびシステムトレイ常駐制御クラス
/// </summary>
public partial class MainWindow : Window
{
    private readonly GlobalHotKeyService _hotKeyService = new();
    private readonly FoundryProofreadService _foundryService = new();
    private ToastNotificationWindow? _toastWindow;
    private Forms.NotifyIcon? _notifyIcon;
    private Forms.ToolStripMenuItem? _trayOpenItem;
    private Forms.ToolStripMenuItem? _trayProofreadClipItem;
    private Forms.ToolStripMenuItem? _trayExitItem;

    private bool _isExplicitExit;
    private bool _isBusyProofreading;
    private bool _isLoadingSettingsToUi;

    // ショートカット入力欄で一時記録されたキー情報
    private uint _pendingModifiers;
    private uint _pendingVirtualKey;
    private string _pendingHotKeyText = string.Empty;

    public MainWindow()
    {
        InitializeComponent();

        // バージョン表記の設定
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        VersionBadgeText.Text = $"v{version?.ToString() ?? "1.0.0.0"}";

        // ウィンドウ位置・サイズの復元
        RestoreWindowBounds();

        // システムトレイアイコンの初期化
        InitializeTrayIcon();

        // Foundry進捗メッセージの購読
        _foundryService.StatusMessageReported += msg =>
        {
            Dispatcher.InvokeAsync(() =>
            {
                StatusBannerText.Text = msg;
            });
        };

        // 言語変更イベントの購読
        LocalizationService.Instance.LanguageChanged += (_, _) =>
        {
            ApplyLocalizedStrings();
        };

        // UIへ設定値と翻訳テキストを反映
        LoadSettingsToUi();
        ApplyLocalizedStrings();
        RefreshHistoryListUi();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        // タイトルバーのダークモード適用
        ThemeService.ApplyWindowDarkTitleBar(this, ThemeService.IsDarkMode);

        // グローバルショートカットキーの登録
        var settings = SettingsService.Current;
        _hotKeyService.HotKeyPressed += OnGlobalHotKeyPressed;
        bool registered = _hotKeyService.InitializeAndRegister(this, settings.HotKeyModifiers, settings.HotKeyVirtualKey);
        if (!registered)
        {
            StatusBannerText.Text = $"ショートカット ({settings.HotKeyDisplayText}) の登録に失敗しました。他アプリと競合している可能性があります。";
        }
    }

    /// <summary>
    /// 前回終了時のウィンドウ位置およびサイズを復元する
    /// </summary>
    private void RestoreWindowBounds()
    {
        var s = SettingsService.Current;
        if (s.WindowWidth >= MinWidth && s.WindowHeight >= MinHeight)
        {
            Width = s.WindowWidth;
            Height = s.WindowHeight;
        }

        if (!double.IsNaN(s.WindowLeft) && !double.IsNaN(s.WindowTop))
        {
            // 画面領域内に収まっているか検証する
            double vLeft = SystemParameters.VirtualScreenLeft;
            double vTop = SystemParameters.VirtualScreenTop;
            double vWidth = SystemParameters.VirtualScreenWidth;
            double vHeight = SystemParameters.VirtualScreenHeight;

            if (s.WindowLeft >= vLeft - 50 && s.WindowLeft < vLeft + vWidth - 100 &&
                s.WindowTop >= vTop - 50 && s.WindowTop < vTop + vHeight - 100)
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Left = s.WindowLeft;
                Top = s.WindowTop;
            }
            else
            {
                WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        if (s.IsMaximized)
        {
            WindowState = WindowState.Maximized;
        }
    }

    /// <summary>
    /// 現在のウィンドウ位置およびサイズを設定へ保存する
    /// </summary>
    private void SaveWindowBounds()
    {
        var s = SettingsService.Current;
        s.IsMaximized = WindowState == WindowState.Maximized;

        Rect bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        if (!bounds.IsEmpty && bounds.Width >= MinWidth && bounds.Height >= MinHeight)
        {
            s.WindowLeft = bounds.Left;
            s.WindowTop = bounds.Top;
            s.WindowWidth = bounds.Width;
            s.WindowHeight = bounds.Height;
        }
        SettingsService.Save();
    }

    /// <summary>
    /// システムトレイ（通知領域）アイコンとコンテキストメニューを初期化する
    /// </summary>
    private void InitializeTrayIcon()
    {
        _notifyIcon = new Forms.NotifyIcon
        {
            Text = "適当キーボード (TekitouKeyborad)",
            Visible = true
        };

        try
        {
            var iconStream = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico"))?.Stream;
            if (iconStream != null)
            {
                _notifyIcon.Icon = new System.Drawing.Icon(iconStream);
            }
        }
        catch
        {
            // 万一リソース取得失敗時は標準アイコンを使用
            _notifyIcon.Icon = System.Drawing.SystemIcons.Application;
        }

        var menu = new Forms.ContextMenuStrip();
        _trayOpenItem = new Forms.ToolStripMenuItem(LocalizationService.Instance["TrayOpen"]);
        _trayOpenItem.Click += (_, _) => ShowAndActivateWindow();

        _trayProofreadClipItem = new Forms.ToolStripMenuItem(LocalizationService.Instance["TrayProofreadClipboard"]);
        _trayProofreadClipItem.Click += async (_, _) => await ProofreadCurrentClipboardAsync();

        _trayExitItem = new Forms.ToolStripMenuItem(LocalizationService.Instance["TrayExit"]);
        _trayExitItem.Click += (_, _) => ExitApplication();

        menu.Items.Add(_trayOpenItem);
        menu.Items.Add(_trayProofreadClipItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(_trayExitItem);

        _notifyIcon.ContextMenuStrip = menu;
        _notifyIcon.DoubleClick += (_, _) => ShowAndActivateWindow();
    }

    private void ShowAndActivateWindow()
    {
        Dispatcher.Invoke(() =>
        {
            if (!IsVisible)
            {
                Show();
            }
            if (WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
            }
            Activate();
            Topmost = true;
            Topmost = false;
            Focus();
        });
    }

    /// <summary>
    /// グローバルショートカットキーが押されたときの処理
    /// （選択テキスト取得 → Microsoft Foundry で校正 → クリップボード格納 → 通知/自動貼り付け）
    /// </summary>
    private async void OnGlobalHotKeyPressed(object? sender, EventArgs e)
    {
        if (_isBusyProofreading) return;
        _isBusyProofreading = true;

        try
        {
            string? selectedText = await TextCaptureAndPasteService.CaptureSelectedTextAsync();
            if (string.IsNullOrWhiteSpace(selectedText))
            {
                StatusBannerText.Text = LocalizationService.Instance["StatusNoText"];
                if (SettingsService.Current.ShowNotification)
                {
                    EnsureToastWindow().ShowError(LocalizationService.Instance["StatusNoText"]);
                }
                return;
            }

            await ExecuteProofreadWorkflowAsync(selectedText, allowAutoPaste: SettingsService.Current.AutoPaste);
        }
        finally
        {
            _isBusyProofreading = false;
        }
    }

    /// <summary>
    /// トレイメニューから現在のクリップボード内テキストを直接校正する
    /// </summary>
    private async Task ProofreadCurrentClipboardAsync()
    {
        if (_isBusyProofreading) return;
        _isBusyProofreading = true;

        try
        {
            string? clipText = await TextCaptureAndPasteService.GetClipboardTextSafeAsync();
            if (string.IsNullOrWhiteSpace(clipText))
            {
                StatusBannerText.Text = LocalizationService.Instance["StatusNoText"];
                if (SettingsService.Current.ShowNotification)
                {
                    EnsureToastWindow().ShowError(LocalizationService.Instance["StatusNoText"]);
                }
                return;
            }

            await ExecuteProofreadWorkflowAsync(clipText, allowAutoPaste: false);
        }
        finally
        {
            _isBusyProofreading = false;
        }
    }

    /// <summary>
    /// 校正の共通ワークフロー（Foundry推論 → クリップボード保存 → 履歴追加 → トースト表示 → 自動貼り付け）
    /// </summary>
    private async Task ExecuteProofreadWorkflowAsync(string inputText, bool allowAutoPaste)
    {
        var settings = SettingsService.Current;
        GlobalBusyBar.Visibility = Visibility.Visible;
        StatusBannerText.Text = LocalizationService.Instance["StatusProofreading"];

        if (settings.ShowNotification)
        {
            EnsureToastWindow().ShowProgress(inputText);
        }

        try
        {
            string proofread = await Task.Run(() => _foundryService.ProofreadTextAsync(inputText));

            // クリップボードへ校正後のテキストを格納する
            await TextCaptureAndPasteService.SetClipboardTextSafeAsync(proofread);

            // プレイグラウンド出力欄にも反映
            TestInputBox.Text = inputText;
            TestOutputBox.Text = proofread;

            // 履歴へ追加して保存
            settings.HistoryItems.Insert(0, new ProofreadHistoryItem
            {
                Timestamp = DateTime.Now,
                OriginalText = inputText,
                ProofreadText = proofread,
                ModelName = settings.SelectedModelAlias
            });
            SettingsService.Save();
            RefreshHistoryListUi();

            StatusBannerText.Text = LocalizationService.Instance["StatusCompleted"];

            if (settings.ShowNotification)
            {
                EnsureToastWindow().ShowCompleted(inputText, proofread);
            }

            // 自動貼り付けが有効な場合は元のウィンドウへ Ctrl+V を送信する
            if (allowAutoPaste)
            {
                await TextCaptureAndPasteService.SendPasteAsync();
            }
        }
        catch (Exception ex)
        {
            string errMsg = LocalizationService.Instance["StatusError"] + ex.Message;
            StatusBannerText.Text = errMsg;
            if (settings.ShowNotification)
            {
                EnsureToastWindow().ShowError(errMsg);
            }
        }
        finally
        {
            GlobalBusyBar.Visibility = Visibility.Collapsed;
        }
    }

    private ToastNotificationWindow EnsureToastWindow()
    {
        if (_toastWindow == null || !_toastWindow.IsLoaded)
        {
            _toastWindow = new ToastNotificationWindow();
        }
        return _toastWindow;
    }

    // ==================== ナビゲーション切り替え ====================

    private void NavHomeBtn_Click(object sender, RoutedEventArgs e)
    {
        SetActiveTab(0);
    }

    private async void NavModelsBtn_Click(object sender, RoutedEventArgs e)
    {
        SetActiveTab(1);
        if (ModelsListBox.ItemsSource == null)
        {
            await RefreshModelCatalogAsync();
        }
    }

    private void NavSettingsBtn_Click(object sender, RoutedEventArgs e)
    {
        SetActiveTab(2);
    }

    private void SetActiveTab(int index)
    {
        HomeView.Visibility = index == 0 ? Visibility.Visible : Visibility.Collapsed;
        ModelsView.Visibility = index == 1 ? Visibility.Visible : Visibility.Collapsed;
        SettingsView.Visibility = index == 2 ? Visibility.Visible : Visibility.Collapsed;

        NavHomeBtn.Style = (Style)FindResource(index == 0 ? "PrimaryButtonStyle" : "SecondaryButtonStyle");
        NavModelsBtn.Style = (Style)FindResource(index == 1 ? "PrimaryButtonStyle" : "SecondaryButtonStyle");
        NavSettingsBtn.Style = (Style)FindResource(index == 2 ? "PrimaryButtonStyle" : "SecondaryButtonStyle");
    }

    // ==================== 1. ホーム・履歴タブのイベント ====================

    private async void BtnRunProofread_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusyProofreading) return;
        string input = TestInputBox.Text.Trim();
        if (string.IsNullOrEmpty(input)) return;

        _isBusyProofreading = true;
        BtnRunProofread.IsEnabled = false;
        try
        {
            await ExecuteProofreadWorkflowAsync(input, allowAutoPaste: false);
        }
        finally
        {
            BtnRunProofread.IsEnabled = true;
            _isBusyProofreading = false;
        }
    }

    private async void BtnCopyResult_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(TestOutputBox.Text))
        {
            await TextCaptureAndPasteService.SetClipboardTextSafeAsync(TestOutputBox.Text);
            StatusBannerText.Text = LocalizationService.Instance["StatusCompleted"];
        }
    }

    private void QuickAutoPasteCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (_isLoadingSettingsToUi) return;
        bool val = QuickAutoPasteCheck.IsChecked == true;
        SettingsService.Current.AutoPaste = val;
        ChkAutoPaste.IsChecked = val;
        SettingsService.Save();
    }

    private void BtnClearHistory_Click(object sender, RoutedEventArgs e)
    {
        SettingsService.Current.HistoryItems.Clear();
        SettingsService.Save();
        RefreshHistoryListUi();
    }

    private async void HistoryListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (HistoryListBox.SelectedItem is ProofreadHistoryItem item)
        {
            TestInputBox.Text = item.OriginalText;
            TestOutputBox.Text = item.ProofreadText;
            await TextCaptureAndPasteService.SetClipboardTextSafeAsync(item.ProofreadText);
            StatusBannerText.Text = LocalizationService.Instance["StatusCompleted"];
        }
    }

    private void RefreshHistoryListUi()
    {
        HistoryListBox.ItemsSource = null;
        HistoryListBox.ItemsSource = SettingsService.Current.HistoryItems;
    }

    // ==================== 2. Foundry モデル管理タブのイベント ====================

    private async void BtnRefreshCatalog_Click(object sender, RoutedEventArgs e)
    {
        await RefreshModelCatalogAsync();
    }

    private async Task RefreshModelCatalogAsync()
    {
        BtnRefreshCatalog.IsEnabled = false;
        GlobalBusyBar.Visibility = Visibility.Visible;
        StatusBannerText.Text = "Foundry Local モデルカタログを取得中...";

        try
        {
            var models = await Task.Run(() => _foundryService.GetCatalogModelsAsync());
            ModelsListBox.ItemsSource = models;

            // 現在選択されているモデルの行を選択状態にする
            var current = models.FirstOrDefault(m =>
                string.Equals(m.Alias, SettingsService.Current.SelectedModelAlias, StringComparison.OrdinalIgnoreCase));
            if (current != null)
            {
                ModelsListBox.SelectedItem = current;
            }
            else if (models.Count > 0)
            {
                ModelsListBox.SelectedIndex = 0;
            }

            StatusBannerText.Text = LocalizationService.Instance["StatusReady"];
        }
        catch (Exception ex)
        {
            StatusBannerText.Text = $"カタログ取得エラー: {ex.Message}";
        }
        finally
        {
            BtnRefreshCatalog.IsEnabled = true;
            GlobalBusyBar.Visibility = Visibility.Collapsed;
        }
    }

    private async void BtnDownloadRegisterEps_Click(object sender, RoutedEventArgs e)
    {
        BtnDownloadRegisterEps.IsEnabled = false;
        ModelDownloadBar.Visibility = Visibility.Visible;
        ModelDownloadBar.IsIndeterminate = false;
        ModelDownloadBar.Value = 0;

        try
        {
            string status = await Task.Run(() => _foundryService.DiscoverAndRegisterEpsAsync((epName, percent) =>
            {
                Dispatcher.InvokeAsync(() =>
                {
                    ModelDownloadBar.Value = percent;
                    StatusBannerText.Text = $"Execution Provider '{epName}' を構成中... ({percent:F1}%)";
                });
            }));
            StatusBannerText.Text = $"EP 登録完了: {status}";
            await RefreshModelCatalogAsync();
        }
        catch (Exception ex)
        {
            StatusBannerText.Text = $"EP 登録エラー: {ex.Message}";
        }
        finally
        {
            BtnDownloadRegisterEps.IsEnabled = true;
            ModelDownloadBar.Visibility = Visibility.Collapsed;
        }
    }

    private async void BtnSelectAndLoad_Click(object sender, RoutedEventArgs e)
    {
        if (ModelsListBox.SelectedItem is not FoundryModelItem selected) return;

        BtnSelectAndLoad.IsEnabled = false;
        ModelDownloadBar.Visibility = Visibility.Visible;
        ModelDownloadBar.Value = 0;

        try
        {
            SettingsService.Current.SelectedModelAlias = selected.Alias;
            BadgeModelText.Text = selected.Alias;
            SettingsService.Save();

            await Task.Run(() => _foundryService.LoadModelAsync(selected.Alias, p =>
            {
                Dispatcher.InvokeAsync(() =>
                {
                    ModelDownloadBar.Value = p;
                });
            }));

            StatusBannerText.Text = $"モデル '{selected.Alias}' をロードし、有効化しました。";
            await RefreshModelCatalogAsync();
        }
        catch (Exception ex)
        {
            StatusBannerText.Text = $"モデルロードエラー: {ex.Message}";
        }
        finally
        {
            BtnSelectAndLoad.IsEnabled = true;
            ModelDownloadBar.Visibility = Visibility.Collapsed;
        }
    }

    private async void BtnDownloadModel_Click(object sender, RoutedEventArgs e)
    {
        if (ModelsListBox.SelectedItem is not FoundryModelItem selected) return;

        BtnDownloadModel.IsEnabled = false;
        ModelDownloadBar.Visibility = Visibility.Visible;
        ModelDownloadBar.Value = 0;

        try
        {
            await Task.Run(() => _foundryService.DownloadModelAsync(selected.Alias, p =>
            {
                Dispatcher.InvokeAsync(() =>
                {
                    ModelDownloadBar.Value = p;
                    StatusBannerText.Text = $"モデル '{selected.Alias}' をダウンロード中... ({p:F1}%)";
                });
            }));

            StatusBannerText.Text = $"モデル '{selected.Alias}' のダウンロードが完了しました。";
            await RefreshModelCatalogAsync();
        }
        catch (Exception ex)
        {
            StatusBannerText.Text = $"ダウンロードエラー: {ex.Message}";
        }
        finally
        {
            BtnDownloadModel.IsEnabled = true;
            ModelDownloadBar.Visibility = Visibility.Collapsed;
        }
    }

    private async void BtnUnloadModel_Click(object sender, RoutedEventArgs e)
    {
        if (ModelsListBox.SelectedItem is not FoundryModelItem selected) return;

        BtnUnloadModel.IsEnabled = false;
        try
        {
            await Task.Run(() => _foundryService.UnloadModelAsync(selected.Alias));
            StatusBannerText.Text = $"モデル '{selected.Alias}' をアンロードしました。";
            await RefreshModelCatalogAsync();
        }
        catch (Exception ex)
        {
            StatusBannerText.Text = $"アンロードエラー: {ex.Message}";
        }
        finally
        {
            BtnUnloadModel.IsEnabled = true;
        }
    }

    // ==================== 3. 設定タブのイベント ====================

    private void LoadSettingsToUi()
    {
        _isLoadingSettingsToUi = true;
        try
        {
            var s = SettingsService.Current;

            BadgeShortcutText.Text = s.HotKeyDisplayText;
            BadgeModelText.Text = s.SelectedModelAlias;

            _pendingModifiers = s.HotKeyModifiers;
            _pendingVirtualKey = s.HotKeyVirtualKey;
            _pendingHotKeyText = s.HotKeyDisplayText;
            HotKeyRecorderBox.Text = s.HotKeyDisplayText;

            bool isHttp = string.Equals(s.ConnectionMode, "OpenAICompatibleEndpoint", StringComparison.OrdinalIgnoreCase);
            RadioFoundrySdk.IsChecked = !isHttp;
            RadioOpenAiEndpoint.IsChecked = isHttp;
            EndpointConfigPanel.Visibility = isHttp ? Visibility.Visible : Visibility.Collapsed;
            EndpointUrlBox.Text = s.CustomEndpointUrl;

            SelectComboByTag(PresetComboBox, s.PromptPreset);
            SystemPromptBox.Text = s.SystemPrompt;
            TemperatureSlider.Value = s.Temperature;

            QuickAutoPasteCheck.IsChecked = s.AutoPaste;
            ChkShowNotification.IsChecked = s.ShowNotification;
            ChkAutoPaste.IsChecked = s.AutoPaste;
            ChkStartMinimized.IsChecked = s.StartMinimized;
            ChkCloseToTray.IsChecked = s.CloseToTray;

            SelectComboByTag(LanguageComboBox, s.LanguageMode);
            SelectComboByTag(ThemeComboBox, s.ThemeMode);
        }
        finally
        {
            _isLoadingSettingsToUi = false;
        }
    }

    private static void SelectComboByTag(System.Windows.Controls.ComboBox combo, string tagValue)
    {
        foreach (ComboBoxItem item in combo.Items)
        {
            if (string.Equals(item.Tag?.ToString(), tagValue, StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedItem = item;
                return;
            }
        }
        if (combo.Items.Count > 0)
        {
            combo.SelectedIndex = 0;
        }
    }

    private static string GetSelectedComboTag(System.Windows.Controls.ComboBox combo, string defaultVal)
    {
        if (combo.SelectedItem is ComboBoxItem item && item.Tag != null)
        {
            return item.Tag.ToString() ?? defaultVal;
        }
        return defaultVal;
    }

    /// <summary>
    /// ショートカットキー入力欄で押されたキーの組み合わせをキャプチャする
    /// </summary>
    private void HotKeyRecorderBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        e.Handled = true;

        Key key = e.Key == Key.System ? e.SystemKey : e.Key;

        // 修飾キー単体の押下時はまだ確定しない
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
        {
            return;
        }

        uint mods = 0;
        var parts = new List<string>();

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            mods |= GlobalHotKeyService.MOD_CONTROL;
            parts.Add("Ctrl");
        }
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
        {
            mods |= GlobalHotKeyService.MOD_ALT;
            parts.Add("Alt");
        }
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            mods |= GlobalHotKeyService.MOD_SHIFT;
            parts.Add("Shift");
        }
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Windows))
        {
            mods |= GlobalHotKeyService.MOD_WIN;
            parts.Add("Win");
        }

        // 修飾キーが1つもない場合は安全のため Ctrl + Alt を自動付与する
        if (mods == 0)
        {
            mods = GlobalHotKeyService.MOD_CONTROL | GlobalHotKeyService.MOD_ALT;
            parts.Add("Ctrl");
            parts.Add("Alt");
        }

        int vk = KeyInterop.VirtualKeyFromKey(key);
        parts.Add(key.ToString());

        _pendingModifiers = mods;
        _pendingVirtualKey = (uint)vk;
        _pendingHotKeyText = string.Join(" + ", parts);
        HotKeyRecorderBox.Text = _pendingHotKeyText;
    }

    private void BtnApplyShortcut_Click(object sender, RoutedEventArgs e)
    {
        if (_pendingVirtualKey == 0) return;

        bool ok = _hotKeyService.Register(_pendingModifiers, _pendingVirtualKey);
        if (ok)
        {
            var s = SettingsService.Current;
            s.HotKeyModifiers = _pendingModifiers;
            s.HotKeyVirtualKey = _pendingVirtualKey;
            s.HotKeyDisplayText = _pendingHotKeyText;
            BadgeShortcutText.Text = _pendingHotKeyText;
            SettingsService.Save();
            StatusBannerText.Text = $"ショートカットキーを '{_pendingHotKeyText}' に変更しました。";
        }
        else
        {
            StatusBannerText.Text = $"ショートカットキー '{_pendingHotKeyText}' の登録に失敗しました（他アプリで使用中）。";
        }
    }

    private void ConnModeRadio_Changed(object sender, RoutedEventArgs e)
    {
        if (_isLoadingSettingsToUi || EndpointConfigPanel == null) return;
        EndpointConfigPanel.Visibility = RadioOpenAiEndpoint.IsChecked == true
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void PresetComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoadingSettingsToUi || SystemPromptBox == null) return;

        string tag = GetSelectedComboTag(PresetComboBox, "Standard");
        switch (tag)
        {
            case "Standard":
                SystemPromptBox.Text = AppSettings.DefaultSystemPromptJa;
                break;
            case "Business":
                SystemPromptBox.Text = AppSettings.BusinessSystemPromptJa;
                break;
            case "Concise":
                SystemPromptBox.Text = AppSettings.ConciseSystemPromptJa;
                break;
        }
    }

    private void BtnSaveSettings_Click(object sender, RoutedEventArgs e)
    {
        var s = SettingsService.Current;

        if (_pendingVirtualKey != 0)
        {
            if (_hotKeyService.Register(_pendingModifiers, _pendingVirtualKey))
            {
                s.HotKeyModifiers = _pendingModifiers;
                s.HotKeyVirtualKey = _pendingVirtualKey;
                s.HotKeyDisplayText = _pendingHotKeyText;
                BadgeShortcutText.Text = _pendingHotKeyText;
            }
        }

        s.ConnectionMode = RadioOpenAiEndpoint.IsChecked == true ? "OpenAICompatibleEndpoint" : "FoundryLocalSdk";
        s.CustomEndpointUrl = EndpointUrlBox.Text.Trim();
        s.PromptPreset = GetSelectedComboTag(PresetComboBox, "Standard");
        s.SystemPrompt = SystemPromptBox.Text.Trim();
        s.Temperature = (float)TemperatureSlider.Value;

        s.ShowNotification = ChkShowNotification.IsChecked == true;
        s.AutoPaste = ChkAutoPaste.IsChecked == true;
        QuickAutoPasteCheck.IsChecked = s.AutoPaste;
        s.StartMinimized = ChkStartMinimized.IsChecked == true;
        s.CloseToTray = ChkCloseToTray.IsChecked == true;

        s.LanguageMode = GetSelectedComboTag(LanguageComboBox, "Auto");
        s.ThemeMode = GetSelectedComboTag(ThemeComboBox, "Auto");

        SettingsService.Save();

        LocalizationService.Instance.ApplyLanguage(s.LanguageMode);
        ThemeService.ApplyTheme(s.ThemeMode);

        StatusBannerText.Text = LocalizationService.Instance["SettingsSavedMsg"];
    }

    /// <summary>
    /// 現在の表示言語に合わせて画面上の全ラベル・ボタン文字列を更新する
    /// </summary>
    private void ApplyLocalizedStrings()
    {
        var loc = LocalizationService.Instance;

        Title = $"{loc["AppTitle"]} (TekitouKeyborad)";
        AppTitleText.Text = loc["AppTitle"];
        StatusBannerText.Text = loc["StatusReady"];
        LblActiveShortcut.Text = loc["ActiveShortcut"];
        LblActiveModel.Text = loc["ActiveModel"];

        NavHomeBtn.Content = loc["TabHome"];
        NavModelsBtn.Content = loc["TabModels"];
        NavSettingsBtn.Content = loc["TabSettings"];

        LblTestSectionTitle.Text = loc["TestSectionTitle"];
        QuickAutoPasteCheck.Content = loc["QuickAutoPaste"];
        BtnRunProofread.Content = loc["BtnRunProofread"];
        BtnCopyResult.Content = loc["BtnCopyResult"];
        LblHistoryTitle.Text = loc["HistorySectionTitle"];
        BtnClearHistory.Content = loc["BtnClearHistory"];

        LblModelsHeaderTitle.Text = loc["ModelsHeaderTitle"];
        LblModelsHeaderDesc.Text = loc["ModelsHeaderDesc"];
        BtnRefreshCatalog.Content = loc["BtnRefreshCatalog"];
        BtnDownloadRegisterEps.Content = loc["BtnDownloadRegisterEps"];
        BtnSelectAndLoad.Content = loc["BtnSelectAndLoad"];
        BtnDownloadModel.Content = loc["BtnDownloadModel"];
        BtnUnloadModel.Content = loc["BtnUnloadModel"];

        LblSettingsShortcutHeader.Text = loc["SettingsShortcutHeader"];
        LblSettingsShortcutDesc.Text = loc["SettingsShortcutDesc"];
        BtnApplyShortcut.Content = loc["BtnApplyShortcut"];
        LblSettingsConnectionHeader.Text = loc["SettingsConnectionHeader"];
        RadioFoundrySdk.Content = loc["ModeFoundrySdk"];
        RadioOpenAiEndpoint.Content = loc["ModeOpenAiEndpoint"];
        LblEndpointUrl.Text = loc["EndpointUrlLabel"];
        LblSettingsPromptHeader.Text = loc["SettingsPromptHeader"];
        LblPreset.Text = loc["PresetLabel"];

        if (PresetComboBox.Items.Count >= 4)
        {
            ((ComboBoxItem)PresetComboBox.Items[0]).Content = loc["PresetStandard"];
            ((ComboBoxItem)PresetComboBox.Items[1]).Content = loc["PresetBusiness"];
            ((ComboBoxItem)PresetComboBox.Items[2]).Content = loc["PresetConcise"];
            ((ComboBoxItem)PresetComboBox.Items[3]).Content = loc["PresetCustom"];
        }

        LblSystemPrompt.Text = loc["SystemPromptLabel"];
        LblTemperature.Text = loc["TemperatureLabel"];
        LblSettingsBehaviorHeader.Text = loc["SettingsBehaviorHeader"];
        ChkShowNotification.Content = loc["ChkShowNotification"];
        ChkAutoPaste.Content = loc["ChkAutoPaste"];
        ChkStartMinimized.Content = loc["ChkStartMinimized"];
        ChkCloseToTray.Content = loc["ChkCloseToTray"];
        LblLanguage.Text = loc["LanguageLabel"];

        if (LanguageComboBox.Items.Count >= 3)
        {
            ((ComboBoxItem)LanguageComboBox.Items[0]).Content = loc["LangAuto"];
            ((ComboBoxItem)LanguageComboBox.Items[1]).Content = loc["LangJa"];
            ((ComboBoxItem)LanguageComboBox.Items[2]).Content = loc["LangEn"];
        }

        LblTheme.Text = loc["ThemeLabel"];
        if (ThemeComboBox.Items.Count >= 3)
        {
            ((ComboBoxItem)ThemeComboBox.Items[0]).Content = loc["ThemeAuto"];
            ((ComboBoxItem)ThemeComboBox.Items[1]).Content = loc["ThemeLight"];
            ((ComboBoxItem)ThemeComboBox.Items[2]).Content = loc["ThemeDark"];
        }

        BtnSaveSettings.Content = loc["BtnSaveSettings"];

        if (_trayOpenItem != null) _trayOpenItem.Text = loc["TrayOpen"];
        if (_trayProofreadClipItem != null) _trayProofreadClipItem.Text = loc["TrayProofreadClipboard"];
        if (_trayExitItem != null) _trayExitItem.Text = loc["TrayExit"];
    }

    /// <summary>
    /// ウィンドウの閉じるボタン押下時の処理
    /// CloseToTray が有効かつ明示的終了でない場合はシステムトレイへ格納する
    /// </summary>
    private void Window_Closing(object sender, CancelEventArgs e)
    {
        SaveWindowBounds();

        if (!_isExplicitExit && SettingsService.Current.CloseToTray)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        CleanupResources();
        System.Windows.Application.Current.Shutdown();
    }

    /// <summary>
    /// トレイメニュー等からアプリケーションを完全に終了する
    /// </summary>
    private void ExitApplication()
    {
        _isExplicitExit = true;
        SaveWindowBounds();
        CleanupResources();
        System.Windows.Application.Current.Shutdown();
    }

    private void CleanupResources()
    {
        _hotKeyService.Dispose();
        if (_notifyIcon != null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _notifyIcon = null;
        }
        _toastWindow?.Close();
        _foundryService.Dispose();
    }
}

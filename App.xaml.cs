using System;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using TekitouKeyborad.Services;

namespace TekitouKeyborad;

/// <summary>
/// アプリケーションのエントリーポイントおよびライフサイクル管理クラス
/// </summary>
public partial class App : System.Windows.Application
{
    private static Mutex? _singleInstanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 二重起動防止チェック
        _singleInstanceMutex = new Mutex(true, "TekitouKeyborad_SingleInstance_Mutex", out bool createdNew);
        if (!createdNew)
        {
            System.Windows.MessageBox.Show(
                "適当キーボード (TekitouKeyborad) は既に起動しています。\nシステムトレイのアイコンを確認してください。",
                "適当キーボード",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Shutdown();
            return;
        }

        // 設定の読み込み
        var settings = SettingsService.Load();

        // 表示言語およびテーマの初期化
        LocalizationService.Instance.ApplyLanguage(settings.LanguageMode);
        ThemeService.Initialize();

        // メインウィンドウの生成
        var mainWindow = new MainWindow();
        MainWindow = mainWindow;

        // グローバルホットキー登録のためウィンドウハンドルを確実に作成する
        var helper = new WindowInteropHelper(mainWindow);
        helper.EnsureHandle();

        if (!settings.StartMinimized)
        {
            mainWindow.Show();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        ThemeService.Cleanup();
        SettingsService.Save();
        _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }
}

using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using TekitouKeyborad.Services;

namespace TekitouKeyborad.Views;

/// <summary>
/// アクティブウィンドウのフォーカスを奪わずに画面右下へ校正状況・結果を表示するポップアップ通知ウィンドウ
/// </summary>
public partial class ToastNotificationWindow : Window
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

    private readonly DispatcherTimer _autoCloseTimer;

    public ToastNotificationWindow()
    {
        InitializeComponent();

        _autoCloseTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(4.5)
        };
        _autoCloseTimer.Tick += (_, _) =>
        {
            _autoCloseTimer.Stop();
            Hide();
        };

        Loaded += (_, _) => RepositionBottomRight();
        SizeChanged += (_, _) => RepositionBottomRight();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        // フォーカスを奪わない WS_EX_NOACTIVATE スタイルを設定する
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
    }

    /// <summary>
    /// 「校正中...」状態の通知を表示する
    /// </summary>
    public void ShowProgress(string originalText, string? statusMessage = null)
    {
        _autoCloseTimer.Stop();
        TitleText.Text = statusMessage ?? LocalizationService.Instance["ToastProofreadingTitle"];
        BusyBar.Visibility = Visibility.Visible;
        OriginalPreviewText.Text = originalText;
        ResultPreviewText.Text = LocalizationService.Instance["StatusProofreading"];

        if (!IsVisible)
        {
            Show();
        }
        RepositionBottomRight();
    }

    /// <summary>
    /// 「校正完了」状態の通知を表示し、一定時間後に自動で閉じる
    /// </summary>
    public void ShowCompleted(string originalText, string proofreadText)
    {
        _autoCloseTimer.Stop();
        TitleText.Text = LocalizationService.Instance["ToastCompletedTitle"];
        BusyBar.Visibility = Visibility.Collapsed;
        OriginalPreviewText.Text = $"{LocalizationService.Instance["ToastOriginalLabel"]} {originalText}";
        ResultPreviewText.Text = proofreadText;

        if (!IsVisible)
        {
            Show();
        }
        RepositionBottomRight();
        _autoCloseTimer.Start();
    }

    /// <summary>
    /// エラーメッセージを表示する
    /// </summary>
    public void ShowError(string errorMessage)
    {
        _autoCloseTimer.Stop();
        TitleText.Text = LocalizationService.Instance["ToastErrorTitle"];
        BusyBar.Visibility = Visibility.Collapsed;
        OriginalPreviewText.Text = string.Empty;
        ResultPreviewText.Text = errorMessage;

        if (!IsVisible)
        {
            Show();
        }
        RepositionBottomRight();
        _autoCloseTimer.Start();
    }

    private void RepositionBottomRight()
    {
        Rect workArea = SystemParameters.WorkArea;
        double actualW = ActualWidth > 0 ? ActualWidth : Width;
        double actualH = ActualHeight > 0 ? ActualHeight : 140;
        Left = workArea.Right - actualW - 12;
        Top = workArea.Bottom - actualH - 12;
    }

    private void CloseBtn_Click(object sender, RoutedEventArgs e)
    {
        _autoCloseTimer.Stop();
        Hide();
    }
}


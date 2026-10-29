using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace TekitouKeyborad.Services;

/// <summary>
/// Windowsのテーマ設定（ライトモード・ダークモード）を監視し、アプリの配色を切り替えるサービスクラス
/// </summary>
public static class ThemeService
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    /// <summary>現在ダークモードが適用されているか</summary>
    public static bool IsDarkMode { get; private set; }

    private static bool _isListening;

    /// <summary>
    /// Windowsのシステム設定変更イベントの監視を開始する
    /// </summary>
    public static void Initialize()
    {
        if (!_isListening)
        {
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
            _isListening = true;
        }
        ApplyTheme(SettingsService.Current.ThemeMode);
    }

    /// <summary>
    /// 終了時にシステムイベント購読を解除する
    /// </summary>
    public static void Cleanup()
    {
        if (_isListening)
        {
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            _isListening = false;
        }
    }

    private static void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category == UserPreferenceCategory.General)
        {
            System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
            {
                if (SettingsService.Current.ThemeMode == "Auto")
                {
                    ApplyTheme("Auto");
                }
            });
        }
    }

    /// <summary>
    /// Windowsレジストリから現在のアプリモード（ライト＝true、ダーク＝false）を取得する
    /// </summary>
    public static bool IsWindowsInDarkMode()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            if (key != null)
            {
                object? val = key.GetValue("AppsUseLightTheme");
                if (val is int intVal)
                {
                    return intVal == 0;
                }
            }
        }
        catch
        {
            // レジストリ取得失敗時はライトモードを既定とする
        }
        return false;
    }

    /// <summary>
    /// 指定されたテーマモード（"Auto", "Light", "Dark"）に応じてアプリケーションリソースの色を更新する
    /// </summary>
    public static void ApplyTheme(string themeMode)
    {
        bool useDark = themeMode switch
        {
            "Dark" => true,
            "Light" => false,
            _ => IsWindowsInDarkMode()
        };

        IsDarkMode = useDark;

        var res = System.Windows.Application.Current?.Resources;
        if (res == null) return;

        if (useDark)
        {
            // ダークモード配色
            SetBrush(res, "BgPrimaryBrush", "#0F172A");
            SetBrush(res, "BgSurfaceBrush", "#1E293B");
            SetBrush(res, "BgCardBrush", "#263349");
            SetBrush(res, "BgInputBrush", "#0F172A");
            SetBrush(res, "TextPrimaryBrush", "#F8FAFC");
            SetBrush(res, "TextSecondaryBrush", "#94A3B8");
            SetBrush(res, "BorderSubtleBrush", "#334155");
            SetBrush(res, "AccentPrimaryBrush", "#6366F1");
            SetBrush(res, "AccentHoverBrush", "#818CF8");
            SetBrush(res, "SuccessBrush", "#10B981");
            SetBrush(res, "DangerBrush", "#EF4444");
        }
        else
        {
            // ライトモード配色
            SetBrush(res, "BgPrimaryBrush", "#F1F5F9");
            SetBrush(res, "BgSurfaceBrush", "#FFFFFF");
            SetBrush(res, "BgCardBrush", "#F8FAFC");
            SetBrush(res, "BgInputBrush", "#FFFFFF");
            SetBrush(res, "TextPrimaryBrush", "#0F172A");
            SetBrush(res, "TextSecondaryBrush", "#475569");
            SetBrush(res, "BorderSubtleBrush", "#CBD5E1");
            SetBrush(res, "AccentPrimaryBrush", "#4F46E5");
            SetBrush(res, "AccentHoverBrush", "#4338CA");
            SetBrush(res, "SuccessBrush", "#059669");
            SetBrush(res, "DangerBrush", "#DC2626");
        }

        // 開いているすべてのウィンドウのタイトルバーにダークモード属性を反映
        if (System.Windows.Application.Current != null)
        {
            foreach (Window win in System.Windows.Application.Current.Windows)
            {
                ApplyWindowDarkTitleBar(win, useDark);
            }
        }
    }

    /// <summary>
    /// ウィンドウのタイトルバーにDWMダークモード属性を適用する
    /// </summary>
    public static void ApplyWindowDarkTitleBar(Window window, bool isDark)
    {
        try
        {
            IntPtr hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd != IntPtr.Zero)
            {
                int useImmersiveDarkMode = isDark ? 1 : 0;
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useImmersiveDarkMode, sizeof(int));
            }
        }
        catch
        {
            // 非対応OS等では無視する
        }
    }

    private static void SetBrush(ResourceDictionary dict, string key, string hexColor)
    {
        var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hexColor);
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        dict[key] = brush;
    }
}


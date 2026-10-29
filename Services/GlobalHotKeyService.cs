using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace TekitouKeyborad.Services;

/// <summary>
/// Win32 API (RegisterHotKey / UnregisterHotKey) を用いて常駐グローバルショートカットキーを監視するサービスクラス
/// </summary>
public class GlobalHotKeyService : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const int HOTKEY_ID = 9001;

    /// <summary>修飾キー定数: Alt</summary>
    public const uint MOD_ALT = 0x0001;
    /// <summary>修飾キー定数: Ctrl</summary>
    public const uint MOD_CONTROL = 0x0002;
    /// <summary>修飾キー定数: Shift</summary>
    public const uint MOD_SHIFT = 0x0004;
    /// <summary>修飾キー定数: Win</summary>
    public const uint MOD_WIN = 0x0008;
    /// <summary>キーリピート抑止フラグ</summary>
    public const uint MOD_NOREPEAT = 0x4000;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private HwndSource? _hwndSource;
    private IntPtr _windowHandle = IntPtr.Zero;
    private bool _isRegistered;

    /// <summary>登録されたショートカットキーが押されたときに発火するイベント</summary>
    public event EventHandler? HotKeyPressed;

    /// <summary>現在ホットキーが正常に登録されているか</summary>
    public bool IsRegistered => _isRegistered;

    /// <summary>
    /// 対象ウィンドウのハンドルにフックを接続し、指定されたホットキーを登録する
    /// </summary>
    public bool InitializeAndRegister(Window window, uint modifiers, uint virtualKey)
    {
        var helper = new WindowInteropHelper(window);
        _windowHandle = helper.EnsureHandle();

        if (_hwndSource == null)
        {
            _hwndSource = HwndSource.FromHwnd(_windowHandle);
            _hwndSource?.AddHook(WndProc);
        }

        return Register(modifiers, virtualKey);
    }

    /// <summary>
    /// ホットキーを再登録する
    /// </summary>
    public bool Register(uint modifiers, uint virtualKey)
    {
        if (_windowHandle == IntPtr.Zero) return false;

        Unregister();

        // MOD_NOREPEAT を付与して押しっぱなしによる連続発火を防ぐ
        _isRegistered = RegisterHotKey(_windowHandle, HOTKEY_ID, modifiers | MOD_NOREPEAT, virtualKey);
        return _isRegistered;
    }

    /// <summary>
    /// 現在登録されているホットキーを解除する
    /// </summary>
    public void Unregister()
    {
        if (_isRegistered && _windowHandle != IntPtr.Zero)
        {
            UnregisterHotKey(_windowHandle, HOTKEY_ID);
            _isRegistered = false;
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
        {
            handled = true;
            HotKeyPressed?.Invoke(this, EventArgs.Empty);
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        Unregister();
        if (_hwndSource != null)
        {
            _hwndSource.RemoveHook(WndProc);
            _hwndSource = null;
        }
    }
}


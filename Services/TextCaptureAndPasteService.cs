using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace TekitouKeyborad.Services;

/// <summary>
/// アクティブウィンドウで選択されているテキストの取得（Ctrl+C送信）と、
/// クリップボード格納および自動貼り付け（Ctrl+V送信）を行うサービスクラス
/// </summary>
public static class TextCaptureAndPasteService
{
    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    private const ushort VK_CONTROL = 0x11;
    private const ushort VK_SHIFT = 0x10;
    private const ushort VK_MENU = 0x12; // Alt
    private const ushort VK_LWIN = 0x5B;
    private const ushort VK_RWIN = 0x5C;
    private const ushort VK_C = 0x43;
    private const ushort VK_V = 0x56;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern uint GetClipboardSequenceNumber();

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public MOUSEINPUT mi;
        [FieldOffset(0)]
        public KEYBDINPUT ki;
        [FieldOffset(0)]
        public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL;
        public ushort wParamH;
    }

    /// <summary>
    /// 現在アクティブなウィンドウで選択されているテキストを Ctrl+C シミュレートにより取得する
    /// </summary>
    public static async Task<string?> CaptureSelectedTextAsync()
    {
        // ユーザーがショートカットキー（Alt や Shift など）を離すのを最大250ms待機し、さらに論理キー解放を送る
        for (int i = 0; i < 12; i++)
        {
            bool altDown = (GetAsyncKeyState(VK_MENU) & 0x8000) != 0;
            bool shiftDown = (GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0;
            if (!altDown && !shiftDown)
            {
                break;
            }
            await Task.Delay(20);
        }

        uint seqBefore = GetClipboardSequenceNumber();

        // 修飾キー（Alt, Shift, Win）を解放した上で Ctrl+C を送信する
        var inputs = new INPUT[]
        {
            CreateKeyInput(VK_MENU, true),
            CreateKeyInput(VK_SHIFT, true),
            CreateKeyInput(VK_LWIN, true),
            CreateKeyInput(VK_RWIN, true),
            CreateKeyInput(VK_CONTROL, false),
            CreateKeyInput(VK_C, false),
            CreateKeyInput(VK_C, true),
            CreateKeyInput(VK_CONTROL, true)
        };

        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());

        // クリップボードのシーケンス番号が更新されるまで最大450ms待機する
        for (int i = 0; i < 18; i++)
        {
            await Task.Delay(25);
            if (GetClipboardSequenceNumber() != seqBefore)
            {
                break;
            }
        }

        // UIスレッドからクリップボードのテキストを取得する
        await Task.Delay(20);
        return await GetClipboardTextSafeAsync();
    }

    /// <summary>
    /// クリップボードからテキストを安全に取得する（リトライ付き）
    /// </summary>
    public static async Task<string?> GetClipboardTextSafeAsync()
    {
        string? result = null;
        for (int attempt = 0; attempt < 5; attempt++)
        {
            bool ok = false;
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    if (System.Windows.Clipboard.ContainsText())
                    {
                        result = System.Windows.Clipboard.GetText();
                    }
                    ok = true;
                }
                catch
                {
                    // 他プロセスがクリップボードをロックしている場合はリトライ
                }
            });

            if (ok) break;
            await Task.Delay(30);
        }
        return result;
    }

    /// <summary>
    /// 指定されたテキストをクリップボードへ安全に格納する（リトライ付き）
    /// </summary>
    public static async Task<bool> SetClipboardTextSafeAsync(string text)
    {
        for (int attempt = 0; attempt < 6; attempt++)
        {
            bool ok = false;
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    System.Windows.Clipboard.SetDataObject(text, true);
                    ok = true;
                }
                catch
                {
                    // クリップボード競合時はリトライ
                }
            });

            if (ok) return true;
            await Task.Delay(40);
        }
        return false;
    }

    /// <summary>
    /// アクティブウィンドウへ Ctrl+V を送信してクリップボードのテキストを貼り付ける
    /// </summary>
    public static async Task SendPasteAsync()
    {
        await Task.Delay(60);
        var inputs = new INPUT[]
        {
            CreateKeyInput(VK_CONTROL, false),
            CreateKeyInput(VK_V, false),
            CreateKeyInput(VK_V, true),
            CreateKeyInput(VK_CONTROL, true)
        };
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }

    private static INPUT CreateKeyInput(ushort vk, bool keyUp)
    {
        return new INPUT
        {
            type = INPUT_KEYBOARD,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = vk,
                    wScan = 0,
                    dwFlags = keyUp ? KEYEVENTF_KEYUP : 0,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero
                }
            }
        };
    }
}


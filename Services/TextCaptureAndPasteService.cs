using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace TekitouKeyborad.Services;

internal readonly record struct PasteTarget(IntPtr WindowHandle, IntPtr FocusHandle, uint ThreadId, uint ProcessId);

internal sealed record CapturedText(string Text, PasteTarget Target);

internal readonly record struct ClipboardTextSnapshot(string? Text, uint SequenceBeforeRead, uint SequenceAfterRead);

internal interface ITextCapturePlatform
{
    PasteTarget? GetForegroundTarget();
    bool AreModifiersReleased();
    uint GetClipboardSequenceNumber();
    void SendCopy();
    void SendPaste();
    Task<ClipboardTextSnapshot> ReadClipboardTextAsync();
    Task WriteClipboardTextAsync(string text);
}

/// <summary>
/// アクティブウィンドウで選択されているテキストの取得（Ctrl+C送信）と、
/// クリップボード格納および自動貼り付け（Ctrl+V送信）を行うサービスクラス
/// </summary>
public static class TextCaptureAndPasteService
{
    private static readonly ITextCapturePlatform Platform = new WindowsTextCapturePlatform();

    internal static Task<CapturedText?> CaptureSelectedTextAsync() => CaptureSelectedTextAsync(Platform);

    internal static async Task<CapturedText?> CaptureSelectedTextAsync(ITextCapturePlatform platform)
    {
        var target = platform.GetForegroundTarget();
        if (target == null)
        {
            throw new InvalidOperationException(LocalizationService.Instance["CaptureTargetUnavailable"]);
        }

        for (int i = 0; i < 100 && !platform.AreModifiersReleased(); i++)
        {
            await Task.Delay(20);
        }
        if (!platform.AreModifiersReleased())
        {
            throw new InvalidOperationException(LocalizationService.Instance["CaptureModifiersHeld"]);
        }
        var releasedTarget = platform.GetForegroundTarget();
        if (releasedTarget == null ||
            releasedTarget.Value.WindowHandle != target.Value.WindowHandle ||
            releasedTarget.Value.ThreadId != target.Value.ThreadId ||
            releasedTarget.Value.ProcessId != target.Value.ProcessId)
        {
            throw new InvalidOperationException(LocalizationService.Instance["CaptureTargetChanged"]);
        }
        // Altの解放でメニューから入力欄へフォーカスが戻るため、コピー先はキー解放後に確定する。
        target = releasedTarget;

        uint sequenceBefore = platform.GetClipboardSequenceNumber();
        platform.SendCopy();

        for (int i = 0; i < 60; i++)
        {
            await Task.Delay(25);
            if (platform.GetForegroundTarget() != target)
            {
                throw new InvalidOperationException(LocalizationService.Instance["CaptureTargetChanged"]);
            }

            uint copiedSequence = platform.GetClipboardSequenceNumber();
            if (copiedSequence == sequenceBefore)
            {
                continue;
            }

            await Task.Delay(20);
            // コピー後に別の内容へ更新された場合も、選択テキストとして扱わない。
            if (platform.GetClipboardSequenceNumber() != copiedSequence)
            {
                throw new InvalidOperationException(LocalizationService.Instance["CaptureClipboardChanged"]);
            }
            var snapshot = await GetClipboardSnapshotSafeAsync(platform);
            if (snapshot.SequenceBeforeRead != copiedSequence ||
                platform.GetClipboardSequenceNumber() != snapshot.SequenceAfterRead)
            {
                throw new InvalidOperationException(LocalizationService.Instance["CaptureClipboardChanged"]);
            }
            if (platform.GetForegroundTarget() != target)
            {
                throw new InvalidOperationException(LocalizationService.Instance["CaptureTargetChanged"]);
            }
            if (string.IsNullOrWhiteSpace(snapshot.Text))
            {
                return null;
            }
            return new CapturedText(snapshot.Text, target.Value);
        }

        throw new InvalidOperationException(LocalizationService.Instance["CaptureCopyNotConfirmed"]);
    }

    /// <summary>
    /// クリップボードからテキストを安全に取得する（リトライ付き）
    /// </summary>
    public static Task<string?> GetClipboardTextSafeAsync() => GetClipboardTextSafeAsync(Platform);

    internal static async Task<string?> GetClipboardTextSafeAsync(ITextCapturePlatform platform) =>
        (await GetClipboardSnapshotSafeAsync(platform)).Text;

    private static async Task<ClipboardTextSnapshot> GetClipboardSnapshotSafeAsync(ITextCapturePlatform platform)
    {
        ExternalException? lastError = null;
        for (int attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                return await platform.ReadClipboardTextAsync();
            }
            catch (ExternalException ex)
            {
                lastError = ex;
            }
            await Task.Delay(30);
        }

        throw new InvalidOperationException(LocalizationService.Instance["ClipboardReadFailed"], lastError);
    }

    /// <summary>
    /// 指定されたテキストをクリップボードへ安全に格納する（リトライ付き）
    /// </summary>
    public static Task SetClipboardTextSafeAsync(string text) => SetClipboardTextSafeAsync(text, Platform);

    internal static async Task SetClipboardTextSafeAsync(string text, ITextCapturePlatform platform)
    {
        ExternalException? lastError = null;
        for (int attempt = 0; attempt < 6; attempt++)
        {
            try
            {
                await platform.WriteClipboardTextAsync(text);
                return;
            }
            catch (ExternalException ex)
            {
                lastError = ex;
            }
            await Task.Delay(40);
        }

        throw new InvalidOperationException(LocalizationService.Instance["ClipboardWriteFailed"], lastError);
    }

    /// <summary>
    /// 元のウィンドウと入力先、クリップボード内容を確認してから貼り付ける
    /// </summary>
    internal static Task<bool> SendPasteAsync(PasteTarget target, string expectedText) =>
        SendPasteAsync(target, expectedText, Platform);

    internal static async Task<bool> SendPasteAsync(PasteTarget target, string expectedText, ITextCapturePlatform platform)
    {
        await Task.Delay(60);
        if (platform.GetForegroundTarget() != target || !platform.AreModifiersReleased())
        {
            return false;
        }

        uint sequence = platform.GetClipboardSequenceNumber();
        var snapshot = await GetClipboardSnapshotSafeAsync(platform);
        if (!string.Equals(snapshot.Text, expectedText, StringComparison.Ordinal) ||
            snapshot.SequenceBeforeRead != sequence ||
            platform.GetClipboardSequenceNumber() != snapshot.SequenceAfterRead ||
            platform.GetForegroundTarget() != target || !platform.AreModifiersReleased())
        {
            return false;
        }

        platform.SendPaste();
        return true;
    }

    internal static void EnsureInputSent(uint sent, uint expected, int error)
    {
        if (sent != expected)
        {
            throw new InvalidOperationException(
                LocalizationService.Instance["InputSendFailed"], new Win32Exception(error));
        }
    }

    private sealed class WindowsTextCapturePlatform : ITextCapturePlatform
    {
        private const uint INPUT_KEYBOARD = 1;
        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const uint CF_UNICODETEXT = 13;

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

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool OpenClipboard(IntPtr owner);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool CloseClipboard();

        [DllImport("user32.dll")]
        private static extern bool IsClipboardFormatAvailable(uint format);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr GetClipboardData(uint format);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalLock(IntPtr memory);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GlobalUnlock(IntPtr memory);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

        [DllImport("user32.dll")]
        private static extern bool GetGUIThreadInfo(uint threadId, ref GUITHREADINFO info);

        [StructLayout(LayoutKind.Sequential)]
        private struct GUITHREADINFO
        {
            public uint cbSize;
            public uint flags;
            public IntPtr hwndActive;
            public IntPtr hwndFocus;
            public IntPtr hwndCapture;
            public IntPtr hwndMenuOwner;
            public IntPtr hwndMoveSize;
            public IntPtr hwndCaret;
            public int caretLeft;
            public int caretTop;
            public int caretRight;
            public int caretBottom;
        }

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

        public PasteTarget? GetForegroundTarget()
        {
            IntPtr window = GetForegroundWindow();
            if (window == IntPtr.Zero)
            {
                return null;
            }

            uint threadId = GetWindowThreadProcessId(window, out uint processId);
            var info = new GUITHREADINFO { cbSize = (uint)Marshal.SizeOf<GUITHREADINFO>() };
            if (threadId == 0 || !GetGUIThreadInfo(threadId, ref info) ||
                info.hwndActive != window || info.hwndFocus == IntPtr.Zero ||
                GetForegroundWindow() != window)
            {
                return null;
            }

            return new PasteTarget(window, info.hwndFocus, threadId, processId);
        }

        public bool AreModifiersReleased() =>
            (GetAsyncKeyState(VK_CONTROL) & 0x8000) == 0 &&
            (GetAsyncKeyState(VK_MENU) & 0x8000) == 0 &&
            (GetAsyncKeyState(VK_SHIFT) & 0x8000) == 0 &&
            (GetAsyncKeyState(VK_LWIN) & 0x8000) == 0 &&
            (GetAsyncKeyState(VK_RWIN) & 0x8000) == 0;

        uint ITextCapturePlatform.GetClipboardSequenceNumber() => GetClipboardSequenceNumber();

        public void SendCopy()
        {
            var inputs = new INPUT[]
            {
            CreateKeyInput(VK_CONTROL, false),
            CreateKeyInput(VK_C, false),
            CreateKeyInput(VK_C, true),
            CreateKeyInput(VK_CONTROL, true)
            };

            SendKeysChecked(inputs, VK_C);
        }

        public async Task<ClipboardTextSnapshot> ReadClipboardTextAsync() =>
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(ReadClipboardSnapshot);

        private static ClipboardTextSnapshot ReadClipboardSnapshot()
        {
            if (!OpenClipboard(IntPtr.Zero))
            {
                throw new ExternalException("Could not open the clipboard.", Marshal.GetHRForLastWin32Error());
            }

            try
            {
                uint sequenceBefore = GetClipboardSequenceNumber();
                string? text = null;
                if (IsClipboardFormatAvailable(CF_UNICODETEXT))
                {
                    IntPtr data = GetClipboardData(CF_UNICODETEXT);
                    if (data == IntPtr.Zero)
                    {
                        throw new Win32Exception(Marshal.GetLastWin32Error());
                    }
                    IntPtr pointer = GlobalLock(data);
                    if (pointer == IntPtr.Zero)
                    {
                        throw new Win32Exception(Marshal.GetLastWin32Error());
                    }
                    try
                    {
                        text = Marshal.PtrToStringUni(pointer);
                    }
                    finally
                    {
                        GlobalUnlock(data);
                    }
                }

                // ロック中の番号更新は遅延描画によるもの。他のアプリの書き換えとは区別する。
                return new ClipboardTextSnapshot(text, sequenceBefore, GetClipboardSequenceNumber());
            }
            finally
            {
                if (!CloseClipboard())
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }
            }
        }

        public async Task WriteClipboardTextAsync(string text) =>
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                System.Windows.Clipboard.SetDataObject(text, true));

        public void SendPaste()
        {
            var inputs = new INPUT[]
            {
            CreateKeyInput(VK_CONTROL, false),
            CreateKeyInput(VK_V, false),
            CreateKeyInput(VK_V, true),
            CreateKeyInput(VK_CONTROL, true)
            };
            SendKeysChecked(inputs, VK_V);
        }

        private static void SendKeysChecked(INPUT[] inputs, ushort key)
        {
            uint sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
            int error = sent == inputs.Length ? 0 : Marshal.GetLastWin32Error();
            // 一部だけ送信された場合に、注入したキーを押下状態のまま残さない。
            if (sent > 0 && sent != inputs.Length)
            {
                var release = new[] { CreateKeyInput(key, true), CreateKeyInput(VK_CONTROL, true) };
                uint released = SendInput((uint)release.Length, release, Marshal.SizeOf<INPUT>());
                if (released != release.Length)
                {
                    System.Diagnostics.Debug.WriteLine($"SendInput key release failed: {released}/{release.Length}");
                }
            }
            EnsureInputSent(sent, (uint)inputs.Length, error);
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
}

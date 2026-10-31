using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Text.Json;
using TekitouKeyborad.Models;
using TekitouKeyborad.Services;

namespace TekitouKeyborad.Tests;

internal static class Program
{
    private static readonly PasteTarget Target = new(new IntPtr(1), new IntPtr(2), 3, 4);

    private static async Task<int> Main()
    {
        var tests = new (string Name, Func<Task> Run)[]
        {
            ("Default settings save as readable UTF-8 JSON", DefaultSettingsRoundTripAsync),
            ("Existing numeric coordinates remain compatible", ExistingCoordinatesAsync),
            ("Missing and null coordinates remain unset", UnsetCoordinatesAsync),
            ("Romaji preset covers Japanese conversion and spelling preservation", RomajiPresetInstructionsAsync),
            ("Plain romaji is converted accurately before proofreading", PlainRomajiInputAsync),
            ("Mixed input, names, URLs, code, and unsupported spelling remain intact", PreservedRomajiInputAsync),
            ("Romaji preset and prompt round-trip through settings", RomajiPresetSettingsAsync),
            ("Existing custom proofreading instructions remain unchanged", CustomProofreadPromptAsync),
            ("Windows startup settings remain independent of tray startup", StartupSettingsRoundTripAsync),
            ("Startup shortcuts can be enabled, updated, and disabled", StartupShortcutLifecycleAsync),
            ("Invalid startup executables never create a shortcut", InvalidStartupExecutableAsync),
            ("Startup shortcut write failures are surfaced", StartupShortcutFailureAsync),
            ("No input target does not send copy", NoTargetAsync),
            ("No clipboard update never reads old text", NoClipboardUpdateAsync),
            ("Verified copy returns text and original target", VerifiedCopyAsync),
            ("Delayed clipboard rendering is accepted", DelayedRenderingAsync),
            ("Modifiers released after 500ms still allow capture", SlowModifierReleaseAsync),
            ("Alt release can restore input focus before copy", ShortcutFocusRestoredAsync),
            ("Copy completion after 450ms still allows capture", SlowCopyAsync),
            ("Clipboard sequence wraparound is accepted", SequenceWraparoundAsync),
            ("Target changed before copy cancels capture", TargetChangedBeforeCopyAsync),
            ("Target changed after copy cancels capture", TargetChangedAfterCopyAsync),
            ("Focus changed by copy cancels capture", FocusChangedAfterCopyAsync),
            ("Clipboard changed while reading cancels capture", ClipboardChangedDuringCaptureAsync),
            ("Focus changed while reading cancels capture", FocusChangedDuringCaptureAsync),
            ("Empty copied text is not captured", EmptyCaptureAsync),
            ("Held modifiers cancel capture without forced key release", HeldModifiersAsync),
            ("Copy key send failure is surfaced", CopySendFailureAsync),
            ("Transient clipboard read failures are retried", ReadRetryAsync),
            ("Exhausted clipboard read retries throw", ReadFailureAsync),
            ("Unexpected clipboard read errors are not swallowed", UnexpectedReadFailureAsync),
            ("Transient clipboard write failures are retried", WriteRetryAsync),
            ("Exhausted clipboard write retries throw", WriteFailureAsync),
            ("Unexpected clipboard write errors are not swallowed", UnexpectedWriteFailureAsync),
            ("Unchanged target and clipboard allow paste", VerifiedPasteAsync),
            ("Different window, focus, thread, or process cancel paste", DifferentPasteTargetsAsync),
            ("Missing paste target cancels paste", MissingPasteTargetAsync),
            ("Replaced clipboard content cancels paste", ReplacedClipboardAsync),
            ("Held modifiers cancel paste", PasteModifiersAsync),
            ("Clipboard sequence changed while reading cancels paste", ClipboardChangedDuringPasteAsync),
            ("Focus changed while reading cancels paste", FocusChangedDuringPasteAsync),
            ("Paste clipboard read failure is surfaced", PasteReadFailureAsync),
            ("Paste key send failure is surfaced", PasteSendFailureAsync),
            ("Key input succeeds only when all events were sent", InputCountsAsync),
            ("New status messages are localized in Japanese and English", LocalizedMessagesAsync)
        };

        int failures = 0;
        foreach (var (name, run) in tests)
        {
            try
            {
                await run();
                Console.WriteLine($"PASS {name}");
            }
            catch (Exception ex)
            {
                failures++;
                Console.Error.WriteLine($"FAIL {name}: {ex}");
            }
        }
        Console.WriteLine($"{tests.Length - failures}/{tests.Length} passed");
        return failures == 0 ? 0 : 1;
    }

    private static JsonSerializerOptions SettingsJsonOptions =>
        typeof(SettingsService).GetField("JsonOptions", BindingFlags.NonPublic | BindingFlags.Static)
            ?.GetValue(null) is JsonSerializerOptions options
            ? options : throw new InvalidOperationException("Settings serialization options not found.");

    private static async Task DefaultSettingsRoundTripAsync()
    {
        var settings = new AppSettings();
        settings.HistoryItems.Add(new ProofreadHistoryItem
        {
            OriginalText = "よろしくおねがいします。",
            ProofreadText = "よろしくお願いします。"
        });
        string json = JsonSerializer.Serialize(settings, SettingsJsonOptions);
        Check(json.Contains("\"WindowLeft\": null", StringComparison.Ordinal), "Left coordinate must be null.");
        Check(json.Contains("\"WindowTop\": null", StringComparison.Ordinal), "Top coordinate must be null.");
        Check(json.Contains("よろしくお願いします。", StringComparison.Ordinal), "Japanese text must not be escaped.");

        string directory = Path.Combine(Path.GetTempPath(), $"TekitouKeyborad-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string file = Path.Combine(directory, "settings.json");
        try
        {
            await File.WriteAllTextAsync(file, json, new UTF8Encoding(false));
            var loaded = JsonSerializer.Deserialize<AppSettings>(await File.ReadAllTextAsync(file), SettingsJsonOptions);
            Check(loaded != null && loaded.WindowLeft == null && loaded.WindowTop == null, "Unset coordinates must round-trip.");
            Check(loaded.HistoryItems[0].ProofreadText == settings.HistoryItems[0].ProofreadText, "History must round-trip.");
        }
        finally
        {
            File.Delete(file);
            Directory.Delete(directory);
        }
    }

    private static Task ExistingCoordinatesAsync()
    {
        var loaded = JsonSerializer.Deserialize<AppSettings>(
            """{"WindowLeft":-1280.5,"WindowTop":42,"WindowWidth":1024,"WindowHeight":768,"IsMaximized":true,"AutoPaste":true}""",
            SettingsJsonOptions);
        Check(loaded != null && loaded.WindowLeft == -1280.5 && loaded.WindowTop == 42, "Existing numeric coordinates must load.");
        Check(loaded.WindowWidth == 1024 && loaded.WindowHeight == 768 && loaded.IsMaximized && loaded.AutoPaste,
            "Existing settings must be preserved.");
        var roundTrip = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(loaded, SettingsJsonOptions), SettingsJsonOptions);
        Check(roundTrip != null && roundTrip.WindowLeft == loaded.WindowLeft && roundTrip.WindowTop == loaded.WindowTop,
            "Numeric coordinates must round-trip.");
        return Task.CompletedTask;
    }

    private static Task UnsetCoordinatesAsync()
    {
        foreach (string json in new[] { "{}", """{"WindowLeft":null,"WindowTop":null}""" })
        {
            var loaded = JsonSerializer.Deserialize<AppSettings>(json, SettingsJsonOptions);
            Check(loaded != null && loaded.WindowLeft == null && loaded.WindowTop == null, "Missing coordinates must stay unset.");
        }
        return Task.CompletedTask;
    }

    private static async Task NoTargetAsync()
    {
        var platform = new FakePlatform { CurrentTarget = null };
        await CaptureFailureAsync(platform, "CaptureTargetUnavailable");
        Check(platform.CopyCount == 0 && platform.ReadCount == 0, "No input or clipboard read is allowed.");
    }

    private static Task RomajiPresetInstructionsAsync()
    {
        string prompt = AppSettings.RomajiSystemPromptJa;
        Check(prompt.Contains("漢字・ひらがな・カタカナ", StringComparison.Ordinal), "Romaji must be converted to Japanese.");
        Check(prompt.Contains("空白がない入力", StringComparison.Ordinal) &&
            prompt.Contains("ヘボン式・訓令式", StringComparison.Ordinal), "Unspaced input and romaji spellings must be covered.");
        Check(prompt.Contains("yorosiku onegaisimasu → よろしくお願いします。", StringComparison.Ordinal), "A concrete conversion example is required.");
        Check(prompt.Contains("原文の綴りをそのまま", StringComparison.Ordinal) &&
            prompt.Contains("VS CodeはVS Codeのまま", StringComparison.Ordinal), "Proper names and abbreviations must retain their spelling.");
        return Task.CompletedTask;
    }

    private static Task PlainRomajiInputAsync()
    {
        foreach (var (input, expected) in new[]
        {
            ("arigatougozaimasu.", "ありがとうございます."),
            ("yorosiku onegaisimasu.", "よろしく おねがいします."),
            ("watashiwaashitakaiginisankashimasu.", "わたしわあしたかいぎにさんかします."),
            ("konnichiwa", "こんにちわ"),
            ("kan'i", "かんい"),
            ("shin'you", "しんよう"),
            ("hon", "ほん"),
            ("hann", "はん"),
            ("anna", "あんな"),
            ("sinnbun", "しんぶん"),
            ("konnya", "こんにゃ"),
            ("gakkou", "がっこう"),
            ("maccha matcha", "まっちゃ まっちゃ"),
            ("syashin shashin", "しゃしん しゃしん"),
            ("tyuui chuui", "ちゅうい ちゅうい"),
            ("tukue tsukue", "つくえ つくえ"),
            ("ryokou, nyuugaku!\r\n", "りょこう, にゅうがく!\r\n"),
            ("kya gyu jo pyo fa va", "きゃ ぎゅ じょ ぴょ ふぁ ゔぁ")
        })
        {
            Check(RomajiInputService.PrepareForProofreading(input) == expected, $"Incorrect phonetic conversion: {input}");
        }
        return Task.CompletedTask;
    }

    private static Task PreservedRomajiInputAsync()
    {
        foreach (string input in new[]
        {
            "明日の kaigi ni sankashimasu.", "VS Code no settei", "Tokyo",
            "https://tokyo.ai/path", "tokyo.ai", "test@example.com", "namae = value;", "`namae`",
            "arigatou keyboard", "She go to school every day.", string.Empty, " \r\n "
        })
        {
            Check(RomajiInputService.PrepareForProofreading(input) == input, $"Input must not be partially converted: {input}");
        }
        return Task.CompletedTask;
    }

    private static Task RomajiPresetSettingsAsync()
    {
        var settings = new AppSettings { PromptPreset = "Romaji", SystemPrompt = AppSettings.RomajiSystemPromptJa };
        var loaded = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings, SettingsJsonOptions), SettingsJsonOptions);
        Check(loaded != null && loaded.PromptPreset == "Romaji" && loaded.SystemPrompt == AppSettings.RomajiSystemPromptJa,
            "The selected romaji preset and its prompt must survive restart.");
        foreach (string prompt in new[]
        {
            AppSettings.DefaultSystemPromptJa, AppSettings.BusinessSystemPromptJa, AppSettings.ConciseSystemPromptJa
        })
        {
            Check(!prompt.Contains("ローマ字", StringComparison.Ordinal), "Existing presets must not silently enable romaji conversion.");
        }
        return Task.CompletedTask;
    }

    private static Task CustomProofreadPromptAsync()
    {
        const string custom = "校正後の文章をJSONのtextプロパティに入れて返してください。";
        var settings = new AppSettings { PromptPreset = "Custom", SystemPrompt = custom };
        var loaded = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings, SettingsJsonOptions), SettingsJsonOptions);
        Check(loaded != null && loaded.PromptPreset == "Custom" && loaded.SystemPrompt == custom,
            "Adding a preset must not rewrite existing custom instructions.");
        return Task.CompletedTask;
    }

    private static Task StartupSettingsRoundTripAsync()
    {
        var oldSettings = JsonSerializer.Deserialize<AppSettings>("{}", SettingsJsonOptions);
        Check(oldSettings != null && !oldSettings.StartWithWindows, "Existing settings must not opt in to Windows startup.");
        foreach (bool startWithWindows in new[] { false, true })
        {
            foreach (bool startMinimized in new[] { false, true })
            {
                var settings = new AppSettings { StartWithWindows = startWithWindows, StartMinimized = startMinimized };
                var loaded = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings, SettingsJsonOptions), SettingsJsonOptions);
                Check(loaded != null && loaded.StartWithWindows == startWithWindows && loaded.StartMinimized == startMinimized,
                    "Windows startup and tray startup must round-trip independently.");
            }
        }
        return Task.CompletedTask;
    }

    private static Task StartupShortcutLifecycleAsync()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"TekitouKeyborad-startup-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string shortcutPath = Path.Combine(directory, StartupService.ShortcutFileName);
        string executablePath = Path.Combine(directory, "適当 keyboard.exe");
        string movedPath = Path.Combine(directory, "移動後 keyboard.exe");
        string unrelatedPath = Path.Combine(directory, "Other app.lnk");
        try
        {
            File.WriteAllText(executablePath, string.Empty);
            File.WriteAllText(movedPath, string.Empty);
            File.WriteAllText(unrelatedPath, "unrelated");
            foreach (string target in new[] { executablePath, executablePath, movedPath })
            {
                StartupService.SetEnabled(true, shortcutPath, target);
                Check(File.Exists(shortcutPath), "Enabling startup must persist a shortcut.");
                var link = new StartupService.ShellLink();
                try
                {
                    ((IPersistFile)link).Load(shortcutPath, 0);
                    var shellLink = (StartupService.IShellLinkW)link;
                    var path = new StringBuilder(1024);
                    var workingDirectory = new StringBuilder(1024);
                    var arguments = new StringBuilder(1024);
                    shellLink.GetPath(path, path.Capacity, IntPtr.Zero, 4);
                    shellLink.GetWorkingDirectory(workingDirectory, workingDirectory.Capacity);
                    shellLink.GetArguments(arguments, arguments.Capacity);
                    Check(path.ToString() == target, "The shortcut must preserve spaces and Japanese in the executable path.");
                    Check(workingDirectory.ToString() == directory, "Startup must use the executable directory, not the Startup folder.");
                    Check(arguments.Length == 0, "Startup must not replay development or other launch arguments.");
                }
                finally
                {
                    Marshal.FinalReleaseComObject(link);
                }
            }
            StartupService.SetEnabled(false, shortcutPath, null);
            StartupService.SetEnabled(false, shortcutPath, null);
            Check(!File.Exists(shortcutPath), "Disabling must remove the shortcut even without an executable.");
            Check(File.ReadAllText(unrelatedPath) == "unrelated", "Other apps' startup entries must be preserved.");
        }
        finally
        {
            File.Delete(shortcutPath);
            File.Delete(executablePath);
            File.Delete(movedPath);
            File.Delete(unrelatedPath);
            Directory.Delete(directory);
        }
        return Task.CompletedTask;
    }

    private static async Task InvalidStartupExecutableAsync()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"TekitouKeyborad-startup-invalid-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string shortcutPath = Path.Combine(directory, StartupService.ShortcutFileName);
        string dotnetPath = Path.Combine(directory, "dotnet.exe");
        string textPath = Path.Combine(directory, "readme.txt");
        try
        {
            File.WriteAllText(dotnetPath, string.Empty);
            File.WriteAllText(textPath, string.Empty);
            foreach (string? executable in new[] { null, "relative.exe", Path.Combine(directory, "missing.exe"), dotnetPath, textPath })
            {
                await ThrowsAsync<InvalidOperationException>(() =>
                {
                    StartupService.SetEnabled(true, shortcutPath, executable);
                    return Task.CompletedTask;
                });
                Check(!File.Exists(shortcutPath), "An invalid target must not create a broken startup shortcut.");
            }
        }
        finally
        {
            File.Delete(shortcutPath);
            File.Delete(dotnetPath);
            File.Delete(textPath);
            Directory.Delete(directory);
        }
    }

    private static async Task StartupShortcutFailureAsync()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"TekitouKeyborad-startup-failure-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string blockedDirectory = Path.Combine(directory, "blocked");
        string executablePath = Path.Combine(directory, "app.exe");
        try
        {
            File.WriteAllText(blockedDirectory, "not a directory");
            File.WriteAllText(executablePath, string.Empty);
            await ThrowsAsync<IOException>(() =>
            {
                StartupService.SetEnabled(true, Path.Combine(blockedDirectory, StartupService.ShortcutFileName), executablePath);
                return Task.CompletedTask;
            });
        }
        finally
        {
            File.Delete(blockedDirectory);
            File.Delete(executablePath);
            Directory.Delete(directory);
        }
    }

    private static async Task NoClipboardUpdateAsync()
    {
        var platform = new FakePlatform { ClipboardText = "old unrelated text", UpdateOnCopy = false };
        await CaptureFailureAsync(platform, "CaptureCopyNotConfirmed");
        Check(platform.ReadCount == 0, "Clipboard must not be read when copy was not verified.");
    }

    private static async Task VerifiedCopyAsync()
    {
        var platform = new FakePlatform();
        var captured = await TextCaptureAndPasteService.CaptureSelectedTextAsync(platform);
        Check(captured != null && captured.Text == platform.ClipboardText && captured.Target == Target, "Capture must preserve text and target.");
        Check(platform.CopyCount == 1 && platform.ReadCount == 1, "Copy and read must occur once.");
    }

    private static async Task SequenceWraparoundAsync()
    {
        var platform = new FakePlatform { Sequence = uint.MaxValue };
        Check(await TextCaptureAndPasteService.CaptureSelectedTextAsync(platform) != null, "Wrapped sequence is still an update.");
    }

    private static async Task DelayedRenderingAsync()
    {
        var platform = new FakePlatform { RenderOnRead = true };
        var captured = await TextCaptureAndPasteService.CaptureSelectedTextAsync(platform);
        Check(captured != null && captured.Text == platform.ClipboardText, "Rendering the copied text must not be mistaken for another copy.");
    }

    private static async Task SlowModifierReleaseAsync()
    {
        var platform = new FakePlatform { ModifierChecksBeforeRelease = 35 };
        var captured = await TextCaptureAndPasteService.CaptureSelectedTextAsync(platform);
        Check(captured != null && platform.CopyCount == 1, "Capture must wait for a normal key release.");
    }

    private static async Task SlowCopyAsync()
    {
        var platform = new FakePlatform { UpdateOnCopy = false, SequenceChecksBeforeCopy = 26 };
        var captured = await TextCaptureAndPasteService.CaptureSelectedTextAsync(platform);
        Check(captured != null && platform.CopyCount == 1, "Capture must wait for a slow copy without sending it again.");
    }

    private static async Task ShortcutFocusRestoredAsync()
    {
        var platform = new FakePlatform
        {
            CurrentTarget = Target with { FocusHandle = new IntPtr(10) },
            ModifierChecksBeforeRelease = 2
        };
        platform.OnTargetRead = () =>
        {
            if (platform.TargetReadCount == 2)
                platform.CurrentTarget = Target;
        };
        var captured = await TextCaptureAndPasteService.CaptureSelectedTextAsync(platform);
        Check(captured != null && captured.Target == Target, "Capture must use the input focus restored after shortcut release.");
        Check(platform.CopyCount == 1, "The restored input field must receive one copy.");
        Check(await TextCaptureAndPasteService.SendPasteAsync(captured.Target, captured.Text, platform),
            "The restored input field must also be used for the later paste check.");
    }

    private static async Task TargetChangedBeforeCopyAsync()
    {
        foreach (var different in new[]
        {
            Target with { WindowHandle = new IntPtr(10) },
            Target with { ThreadId = 10 },
            Target with { ProcessId = 10 }
        })
        {
            var platform = new FakePlatform { ModifierChecksBeforeRelease = 2 };
            platform.OnTargetRead = () =>
            {
                if (platform.TargetReadCount == 2)
                    platform.CurrentTarget = different;
            };
            await CaptureFailureAsync(platform, "CaptureTargetChanged");
            Check(platform.CopyCount == 0, "Copy must not be sent to another window, thread, or process.");
        }
    }

    private static async Task TargetChangedAfterCopyAsync()
    {
        var platform = new FakePlatform();
        platform.OnCopy = () => platform.CurrentTarget = Target with { WindowHandle = new IntPtr(10) };
        await CaptureFailureAsync(platform, "CaptureTargetChanged");
        Check(platform.ReadCount == 0, "No text should be read from another target.");
    }

    private static async Task FocusChangedAfterCopyAsync()
    {
        var platform = new FakePlatform();
        platform.OnCopy = () => platform.CurrentTarget = Target with { FocusHandle = new IntPtr(10) };
        await CaptureFailureAsync(platform, "CaptureTargetChanged");
        Check(platform.ReadCount == 0, "A focus change after copy must still cancel capture.");
    }

    private static async Task ClipboardChangedDuringCaptureAsync()
    {
        var platform = new FakePlatform();
        platform.OnRead = () => platform.Sequence++;
        await CaptureFailureAsync(platform, "CaptureClipboardChanged");
    }

    private static async Task FocusChangedDuringCaptureAsync()
    {
        var platform = new FakePlatform();
        platform.OnRead = () => platform.CurrentTarget = Target with { FocusHandle = new IntPtr(10) };
        await CaptureFailureAsync(platform, "CaptureTargetChanged");
    }

    private static async Task EmptyCaptureAsync()
    {
        var platform = new FakePlatform { ClipboardText = " \r\n " };
        Check(await TextCaptureAndPasteService.CaptureSelectedTextAsync(platform) == null, "Empty text must not be captured.");
    }

    private static async Task HeldModifiersAsync()
    {
        var platform = new FakePlatform { ModifiersReleased = false };
        await CaptureFailureAsync(platform, "CaptureModifiersHeld");
        Check(platform.CopyCount == 0, "Copy must not be sent with held modifiers.");
    }

    private static async Task CopySendFailureAsync()
    {
        var platform = new FakePlatform { CopyError = new InvalidOperationException("send failed") };
        await ThrowsAsync<InvalidOperationException>(() => TextCaptureAndPasteService.CaptureSelectedTextAsync(platform));
        Check(platform.ReadCount == 0, "Failed copy must not read old clipboard content.");
    }

    private static async Task ReadRetryAsync()
    {
        var platform = new FakePlatform { ReadFailures = 2 };
        Check(await TextCaptureAndPasteService.GetClipboardTextSafeAsync(platform) == platform.ClipboardText, "Read must eventually succeed.");
        Check(platform.ReadCount == 3, "Only transient failures should be retried.");
    }

    private static async Task ReadFailureAsync()
    {
        var platform = new FakePlatform { ReadFailures = int.MaxValue };
        var error = await ThrowsAsync<InvalidOperationException>(() => TextCaptureAndPasteService.GetClipboardTextSafeAsync(platform));
        Check(platform.ReadCount == 5 && error.InnerException is ExternalException, "Read must fail explicitly after five attempts.");
    }

    private static async Task UnexpectedReadFailureAsync()
    {
        var platform = new FakePlatform { ReadError = new ArgumentException("unexpected") };
        await ThrowsAsync<ArgumentException>(() => TextCaptureAndPasteService.GetClipboardTextSafeAsync(platform));
        Check(platform.ReadCount == 1, "Unexpected errors must not be retried or hidden.");
    }

    private static async Task WriteRetryAsync()
    {
        var platform = new FakePlatform { WriteFailures = 2 };
        await TextCaptureAndPasteService.SetClipboardTextSafeAsync("corrected", platform);
        Check(platform.WriteCount == 3 && platform.ClipboardText == "corrected", "Write must retry and persist the result.");
    }

    private static async Task WriteFailureAsync()
    {
        var platform = new FakePlatform { WriteFailures = int.MaxValue, ClipboardText = "old text" };
        var error = await ThrowsAsync<InvalidOperationException>(
            () => TextCaptureAndPasteService.SetClipboardTextSafeAsync("corrected", platform));
        Check(platform.WriteCount == 6 && error.InnerException is ExternalException, "Write must fail explicitly after six attempts.");
        Check(platform.ClipboardText == "old text" && platform.PasteCount == 0, "Failure must not be success-shaped.");
    }

    private static async Task UnexpectedWriteFailureAsync()
    {
        var platform = new FakePlatform { WriteError = new ArgumentException("unexpected") };
        await ThrowsAsync<ArgumentException>(() => TextCaptureAndPasteService.SetClipboardTextSafeAsync("corrected", platform));
        Check(platform.WriteCount == 1, "Unexpected errors must not be retried or hidden.");
    }

    private static async Task VerifiedPasteAsync()
    {
        var platform = new FakePlatform();
        Check(await TextCaptureAndPasteService.SendPasteAsync(Target, "selected text", platform), "Verified paste must succeed.");
        Check(platform.PasteCount == 1, "Paste must be sent exactly once.");
    }

    private static async Task DifferentPasteTargetsAsync()
    {
        foreach (var different in new[]
        {
            Target with { WindowHandle = new IntPtr(10) },
            Target with { FocusHandle = new IntPtr(10) },
            Target with { ThreadId = 10 },
            Target with { ProcessId = 10 }
        })
        {
            var platform = new FakePlatform { CurrentTarget = different };
            Check(!await TextCaptureAndPasteService.SendPasteAsync(Target, "selected text", platform), "Different target must cancel paste.");
            Check(platform.PasteCount == 0, "Paste must never go to another target.");
        }
    }

    private static async Task MissingPasteTargetAsync()
    {
        var platform = new FakePlatform { CurrentTarget = null };
        Check(!await TextCaptureAndPasteService.SendPasteAsync(Target, "selected text", platform), "Missing target must cancel paste.");
        Check(platform.PasteCount == 0, "Paste must not be sent.");
    }

    private static async Task ReplacedClipboardAsync()
    {
        var platform = new FakePlatform { ClipboardText = "other text" };
        Check(!await TextCaptureAndPasteService.SendPasteAsync(Target, "corrected text", platform), "Different clipboard content must cancel paste.");
        Check(platform.PasteCount == 0, "Wrong clipboard content must never be pasted.");
    }

    private static async Task PasteModifiersAsync()
    {
        var platform = new FakePlatform { ModifiersReleased = false };
        Check(!await TextCaptureAndPasteService.SendPasteAsync(Target, "selected text", platform), "Held modifiers must cancel paste.");
        Check(platform.PasteCount == 0, "Paste must not be sent with held modifiers.");
    }

    private static async Task ClipboardChangedDuringPasteAsync()
    {
        var platform = new FakePlatform();
        platform.OnRead = () => platform.Sequence++;
        Check(!await TextCaptureAndPasteService.SendPasteAsync(Target, "selected text", platform), "Unstable clipboard must cancel paste.");
        Check(platform.PasteCount == 0, "Unverified content must not be pasted.");
    }

    private static async Task FocusChangedDuringPasteAsync()
    {
        var platform = new FakePlatform();
        platform.OnRead = () => platform.CurrentTarget = Target with { FocusHandle = new IntPtr(10) };
        Check(!await TextCaptureAndPasteService.SendPasteAsync(Target, "selected text", platform), "Changed focus must cancel paste.");
        Check(platform.PasteCount == 0, "Paste must not be sent after focus changes.");
    }

    private static async Task PasteReadFailureAsync()
    {
        var platform = new FakePlatform { ReadFailures = int.MaxValue };
        await ThrowsAsync<InvalidOperationException>(() => TextCaptureAndPasteService.SendPasteAsync(Target, "selected text", platform));
        Check(platform.PasteCount == 0, "Unverified clipboard must not be pasted.");
    }

    private static async Task PasteSendFailureAsync()
    {
        var platform = new FakePlatform { PasteError = new InvalidOperationException("send failed") };
        await ThrowsAsync<InvalidOperationException>(() => TextCaptureAndPasteService.SendPasteAsync(Target, "selected text", platform));
    }

    private static async Task InputCountsAsync()
    {
        TextCaptureAndPasteService.EnsureInputSent(4, 4, 0);
        foreach (uint sent in new uint[] { 0, 1, 2, 3 })
        {
            var error = await ThrowsAsync<InvalidOperationException>(() =>
            {
                TextCaptureAndPasteService.EnsureInputSent(sent, 4, 5);
                return Task.CompletedTask;
            });
            Check(error.InnerException is Win32Exception native && native.NativeErrorCode == 5,
                "Zero or partial key injection must report the native failure.");
        }
    }

    private static Task LocalizedMessagesAsync()
    {
        foreach (string language in new[] { "ja", "en" })
        {
            LocalizationService.Instance.ApplyLanguage(language);
            foreach (string key in new[]
            {
                "ClipboardReadFailed", "ClipboardWriteFailed", "InputSendFailed",
                "CaptureTargetUnavailable", "CaptureModifiersHeld", "CaptureTargetChanged",
                "CaptureClipboardChanged", "CaptureCopyNotConfirmed",
                "ChkStartWithWindows", "StartupSettingsFailed", "StartupFolderUnavailable", "StartupExecutableUnavailable",
                "TestInputPlaceholder",
                "PresetRomaji",
                "StatusAutoPasteSkipped", "ToastAutoPasteSkippedTitle"
            })
            {
                string message = LocalizationService.Instance[key];
                Check(!string.IsNullOrWhiteSpace(message) && message != key, $"{language} translation missing: {key}");
            }
        }
        return Task.CompletedTask;
    }

    private static void Check([DoesNotReturnIf(false)] bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private static async Task CaptureFailureAsync(FakePlatform platform, string key)
    {
        var error = await ThrowsAsync<InvalidOperationException>(() =>
            TextCaptureAndPasteService.CaptureSelectedTextAsync(platform));
        Check(error.Message == LocalizationService.Instance[key], $"Expected capture failure: {key}");
    }

    private static async Task<TException> ThrowsAsync<TException>(Func<Task> action) where TException : Exception
    {
        try
        {
            await action();
        }
        catch (TException ex)
        {
            return ex;
        }
        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }

    private sealed class FakePlatform : ITextCapturePlatform
    {
        public PasteTarget? CurrentTarget { get; set; } = Target;
        public bool ModifiersReleased { get; set; } = true;
        public uint Sequence { get; set; } = 1;
        public string? ClipboardText { get; set; } = "selected text";
        public bool UpdateOnCopy { get; set; } = true;
        public bool RenderOnRead { get; set; }
        public int ModifierChecksBeforeRelease { get; set; }
        public int SequenceChecksBeforeCopy { get; set; }
        public int CopyCount { get; private set; }
        public int PasteCount { get; private set; }
        public int ReadCount { get; private set; }
        public int WriteCount { get; private set; }
        public int TargetReadCount { get; private set; }
        public int ReadFailures { get; set; }
        public int WriteFailures { get; set; }
        public Exception? CopyError { get; set; }
        public Exception? PasteError { get; set; }
        public Exception? ReadError { get; set; }
        public Exception? WriteError { get; set; }
        public Action? OnCopy { get; set; }
        public Action? OnRead { get; set; }
        public Action? OnTargetRead { get; set; }

        public PasteTarget? GetForegroundTarget()
        {
            TargetReadCount++;
            OnTargetRead?.Invoke();
            return CurrentTarget;
        }

        public bool AreModifiersReleased() => ModifierChecksBeforeRelease-- <= 0 && ModifiersReleased;
        public uint GetClipboardSequenceNumber()
        {
            if (CopyCount > 0 && SequenceChecksBeforeCopy > 0 && --SequenceChecksBeforeCopy == 0)
                Sequence++;
            return Sequence;
        }

        public void SendCopy()
        {
            CopyCount++;
            if (CopyError != null)
                throw CopyError;
            if (UpdateOnCopy)
                Sequence++;
            OnCopy?.Invoke();
        }

        public void SendPaste()
        {
            PasteCount++;
            if (PasteError != null)
                throw PasteError;
        }

        public Task<ClipboardTextSnapshot> ReadClipboardTextAsync()
        {
            ReadCount++;
            if (ReadError != null)
                throw ReadError;
            if (ReadFailures-- > 0)
                throw new ExternalException("Clipboard locked.");
            uint sequenceBefore = Sequence;
            if (RenderOnRead)
            {
                Sequence++;
                RenderOnRead = false;
            }
            var snapshot = new ClipboardTextSnapshot(ClipboardText, sequenceBefore, Sequence);
            OnRead?.Invoke();
            return Task.FromResult(snapshot);
        }

        public Task WriteClipboardTextAsync(string text)
        {
            WriteCount++;
            if (WriteError != null)
                throw WriteError;
            if (WriteFailures-- > 0)
                throw new ExternalException("Clipboard locked.");
            ClipboardText = text;
            Sequence++;
            return Task.CompletedTask;
        }
    }
}

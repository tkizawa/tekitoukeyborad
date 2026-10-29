using System;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using TekitouKeyborad.Models;

namespace TekitouKeyborad.Services;

/// <summary>
/// アプリケーション設定の読み込みと保存を管理するサービスクラス
/// 保存先: AppData\Local\TekitouKeyborad\settings.json
/// </summary>
public static class SettingsService
{
    /// <summary>アプリケーション名（フォルダ名）</summary>
    public const string AppFolderName = "TekitouKeyborad";

    /// <summary>設定ファイルの保存ディレクトリパス</summary>
    public static string AppDataDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppFolderName);

    /// <summary>設定ファイルのフルパス</summary>
    public static string SettingsFilePath =>
        Path.Combine(AppDataDirectory, "settings.json");

    /// <summary>
    /// 日本語をUnicodeエスケープ（\uXXXX）せず、そのままUTF-8テキストとして保存するJSONシリアライズオプション
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>現在メモリ上に保持されている設定インスタンス</summary>
    public static AppSettings Current { get; private set; } = new();

    /// <summary>
    /// 設定ファイルを読み込む。存在しない場合や読み込み失敗時は初期設定を生成する。
    /// </summary>
    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsFilePath))
            {
                string json = File.ReadAllText(SettingsFilePath, Encoding.UTF8);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
                if (loaded != null)
                {
                    Current = loaded;
                    return Current;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"設定ファイルの読み込みに失敗しました: {ex.Message}");
        }

        Current = new AppSettings();
        Save();
        return Current;
    }

    /// <summary>
    /// 現在の設定を AppData\Local\TekitouKeyborad\settings.json に UTF-8 で保存する
    /// </summary>
    public static void Save()
    {
        try
        {
            if (!Directory.Exists(AppDataDirectory))
            {
                Directory.CreateDirectory(AppDataDirectory);
            }

            // 履歴が多すぎる場合は最新30件に制限する
            if (Current.HistoryItems.Count > 30)
            {
                Current.HistoryItems = Current.HistoryItems.GetRange(0, 30);
            }

            string json = JsonSerializer.Serialize(Current, JsonOptions);
            File.WriteAllText(SettingsFilePath, json, new UTF8Encoding(false));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"設定ファイルの保存に失敗しました: {ex.Message}");
        }
    }
}


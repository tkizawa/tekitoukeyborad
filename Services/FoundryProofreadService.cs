using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AI.Foundry.Local;
using Microsoft.Extensions.Logging.Abstractions;

namespace TekitouKeyborad.Services;

/// <summary>
/// UIの一覧表示用にFoundry Localモデルの情報を保持するDTOクラス
/// </summary>
public class FoundryModelItem
{
    public string Alias { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string TaskType { get; set; } = string.Empty;
    public long FileSizeMb { get; set; }
    public bool IsCached { get; set; }
    public bool IsLoaded { get; set; }

    public string CachedText => IsCached
        ? LocalizationService.Instance["Yes"]
        : LocalizationService.Instance["No"];

    public string LoadedText => IsLoaded
        ? LocalizationService.Instance["Yes"]
        : LocalizationService.Instance["No"];
}

/// <summary>
/// Microsoft Foundry Local SDK (およびOpenAI互換ローカルエンドポイント) を用いて
/// テキストの校正・モデル管理を行うサービスクラス
/// </summary>
public class FoundryProofreadService : IDisposable
{
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromMinutes(2) };
    private IModel? _activeLoadedModel;
    private string? _activeLoadedAlias;

    /// <summary>進捗メッセージやダウンロード率を通知するイベント</summary>
    public event Action<string>? StatusMessageReported;

    /// <summary>
    /// FoundryLocalManager シングルトンを初期化する
    /// </summary>
    public async Task EnsureInitializedAsync(CancellationToken ct = default)
    {
        if (FoundryLocalManager.IsInitialized)
        {
            return;
        }

        await _initLock.WaitAsync(ct);
        try
        {
            if (!FoundryLocalManager.IsInitialized)
            {
                var config = new Configuration
                {
                    AppName = "TekitouKeyborad"
                };
                await FoundryLocalManager.CreateAsync(config, NullLogger.Instance, ct);
            }
        }
        finally
        {
            _initLock.Release();
        }
    }

    /// <summary>
    /// カタログから利用可能なチャットモデルの一覧を取得する
    /// </summary>
    public async Task<List<FoundryModelItem>> GetCatalogModelsAsync(CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        var catalog = await FoundryLocalManager.Instance.GetCatalogAsync(ct);
        var models = await catalog.ListModelsAsync(ct);

        var items = new List<FoundryModelItem>();
        foreach (var m in models)
        {
            bool isCached = false;
            bool isLoaded = false;
            try
            {
                isCached = await m.IsCachedAsync(ct);
                isLoaded = await m.IsLoadedAsync(ct);
            }
            catch
            {
                isCached = m.Info?.Cached ?? false;
            }

            string taskType = m.Info?.Task ?? "chat-completion";
            long sizeMb = (long)(m.Info?.FileSizeMb ?? 0);

            items.Add(new FoundryModelItem
            {
                Alias = m.Alias ?? m.Id,
                Id = m.Id,
                DisplayName = m.Info?.DisplayName ?? m.Alias ?? m.Id,
                TaskType = taskType,
                FileSizeMb = sizeMb,
                IsCached = isCached,
                IsLoaded = isLoaded
            });
        }

        // ダウンロード済み・ロード済みを優先してソート
        return items
            .OrderByDescending(x => x.IsLoaded)
            .ThenByDescending(x => x.IsCached)
            .ThenBy(x => x.FileSizeMb)
            .ToList();
    }

    /// <summary>
    /// ハードウェアアクセラレーション用の Execution Provider (EP) を検出・ダウンロード・登録する
    /// </summary>
    public async Task<string> DiscoverAndRegisterEpsAsync(Action<string, double>? progressCallback = null, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        var mgr = FoundryLocalManager.Instance;
        var eps = mgr.DiscoverEps();
        if (eps == null || eps.Length == 0)
        {
            return "利用可能な Execution Provider が見つかりませんでした。";
        }

        var result = await mgr.DownloadAndRegisterEpsAsync((epName, percent) =>
        {
            progressCallback?.Invoke(epName, percent);
        }, ct);

        return $"Success: {result.Success}, Status: {result.Status}";
    }

    /// <summary>
    /// 指定されたエイリアスのモデルをダウンロードする
    /// </summary>
    public async Task DownloadModelAsync(string alias, Action<float>? progressCallback = null, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        var catalog = await FoundryLocalManager.Instance.GetCatalogAsync(ct);
        var model = await catalog.GetModelAsync(alias, ct)
            ?? throw new InvalidOperationException($"モデル '{alias}' がカタログに見つかりません。");

        await model.DownloadAsync(p => progressCallback?.Invoke(p), ct);
    }

    /// <summary>
    /// 指定されたエイリアスのモデルをメモリにロードし、アクティブモデルに設定する
    /// </summary>
    public async Task<IModel> LoadModelAsync(string alias, Action<float>? downloadProgress = null, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        var catalog = await FoundryLocalManager.Instance.GetCatalogAsync(ct);

        IModel? model = await catalog.GetModelAsync(alias, ct);
        if (model == null)
        {
            // キャッシュ済みモデルまたはカタログ内の最初のモデルにフォールバック
            var cached = await catalog.GetCachedModelsAsync(ct);
            model = cached.FirstOrDefault();
            if (model == null)
            {
                var all = await catalog.ListModelsAsync(ct);
                model = all.FirstOrDefault();
            }
        }

        if (model == null)
        {
            throw new InvalidOperationException("Foundry Local カタログに利用可能なAIモデルが見つかりません。インターネット接続または Execution Provider 設定を確認してください。");
        }

        bool isCached = await model.IsCachedAsync(ct);
        if (!isCached)
        {
            StatusMessageReported?.Invoke($"モデル '{model.Alias}' をダウンロード中...");
            await model.DownloadAsync(p =>
            {
                downloadProgress?.Invoke(p);
                StatusMessageReported?.Invoke($"モデル '{model.Alias}' をダウンロード中... ({p:F1}%)");
            }, ct);
        }

        bool isLoaded = await model.IsLoadedAsync(ct);
        if (!isLoaded)
        {
            StatusMessageReported?.Invoke($"モデル '{model.Alias}' をメモリにロード中...");
            await model.LoadAsync(ct);
        }

        _activeLoadedModel = model;
        _activeLoadedAlias = model.Alias;
        return model;
    }

    /// <summary>
    /// 指定されたエイリアスのモデルをメモリからアンロードする
    /// </summary>
    public async Task UnloadModelAsync(string alias, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        var catalog = await FoundryLocalManager.Instance.GetCatalogAsync(ct);
        var model = await catalog.GetModelAsync(alias, ct);
        if (model != null)
        {
            await model.UnloadAsync(ct);
            if (_activeLoadedAlias == alias)
            {
                _activeLoadedModel = null;
                _activeLoadedAlias = null;
            }
        }
    }

    /// <summary>
    /// 選択されたテキストを Microsoft Foundry Local で校正し、校正後のテキストを返す
    /// </summary>
    public async Task<string> ProofreadTextAsync(string inputText, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(inputText))
        {
            return string.Empty;
        }

        var settings = SettingsService.Current;
        bool isRomaji = string.Equals(settings.PromptPreset, "Romaji", StringComparison.OrdinalIgnoreCase);
        string kanaInput = isRomaji
            ? RomajiInputService.PrepareForProofreading(inputText)
            : inputText;
        string modelInput = isRomaji && !string.Equals(kanaInput, inputText, StringComparison.Ordinal)
            ? $"ローマ字原文:\n{inputText}\n\nかな読み:\n{kanaInput}"
            : inputText;
        string systemPrompt = settings.SystemPrompt;
        if (string.IsNullOrWhiteSpace(systemPrompt))
        {
            systemPrompt = isRomaji
                ? Models.AppSettings.RomajiSystemPromptJa
                : Models.AppSettings.DefaultSystemPromptJa;
        }

        // OpenAI互換HTTPエンドポイントモードが選択されている場合
        if (string.Equals(settings.ConnectionMode, "OpenAICompatibleEndpoint", StringComparison.OrdinalIgnoreCase))
        {
            return await ProofreadViaHttpEndpointAsync(inputText, modelInput, systemPrompt, settings, ct);
        }

        // Foundry Local SDK モード（標準）
        IModel model;
        if (_activeLoadedModel != null &&
            string.Equals(_activeLoadedAlias, settings.SelectedModelAlias, StringComparison.OrdinalIgnoreCase) &&
            await _activeLoadedModel.IsLoadedAsync(ct))
        {
            model = _activeLoadedModel;
        }
        else
        {
            model = await LoadModelAsync(settings.SelectedModelAlias, null, ct);
        }

        StatusMessageReported?.Invoke(LocalizationService.Instance["StatusProofreading"]);

        using var session = new ChatSession(model);
        session.SetOptions(new RequestOptions
        {
            Search = new SearchOptions
            {
                Temperature = Math.Clamp(settings.Temperature, 0.0f, 1.5f),
                MaxOutputTokens = Math.Max(128, settings.MaxTokens)
            }
        });

        using var request = new Request()
            .AddItem(MessageItem.System(systemPrompt))
            .AddItem(MessageItem.User(modelInput));

        using var response = await session.ProcessRequestAsync(request, ct);
        var sb = new StringBuilder();
        foreach (var item in response)
        {
            if (item is MessageItem msgItem)
            {
                sb.Append(msgItem.GetSimpleText());
            }
        }
        string rawOutput = sb.ToString();

        return CleanProofreadOutput(rawOutput, inputText);
    }

    /// <summary>
    /// OpenAI 互換のローカル HTTP エンドポイント（foundry service 等）経由で校正を実行する
    /// </summary>
    private async Task<string> ProofreadViaHttpEndpointAsync(
        string inputText,
        string modelInput,
        string systemPrompt,
        Models.AppSettings settings,
        CancellationToken ct)
    {
        string url = settings.CustomEndpointUrl?.Trim() ?? "http://127.0.0.1:5272/v1/chat/completions";
        var payload = new
        {
            model = settings.SelectedModelAlias,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = modelInput }
            },
            temperature = settings.Temperature,
            max_tokens = settings.MaxTokens
        };

        string json = JsonSerializer.Serialize(payload);
        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        if (!string.IsNullOrWhiteSpace(settings.CustomApiKey))
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.CustomApiKey);
        }

        using var resp = await _httpClient.SendAsync(req, ct);
        string respBody = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"HTTP {(int)resp.StatusCode}: {respBody}");
        }

        using var doc = JsonDocument.Parse(respBody);
        if (doc.RootElement.TryGetProperty("choices", out var choices) &&
            choices.GetArrayLength() > 0 &&
            choices[0].TryGetProperty("message", out var message) &&
            message.TryGetProperty("content", out var content))
        {
            return CleanProofreadOutput(content.GetString() ?? string.Empty, inputText);
        }

        return inputText;
    }

    /// <summary>
    /// AIの出力から不要な思考タグ（&lt;think&gt;...&lt;/think&gt;）やコードフェンスを除去し、純粋な校正後テキストを取り出す
    /// </summary>
    private static string CleanProofreadOutput(string output, string fallbackOriginal)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return fallbackOriginal;
        }

        string cleaned = output.Trim();

        // DeepSeek等の <think>...</think> タグが含まれる場合は除去する
        int thinkStart = cleaned.IndexOf("<think>", StringComparison.OrdinalIgnoreCase);
        int thinkEnd = cleaned.IndexOf("</think>", StringComparison.OrdinalIgnoreCase);
        if (thinkStart >= 0 && thinkEnd > thinkStart)
        {
            cleaned = cleaned.Remove(thinkStart, (thinkEnd + "</think>".Length) - thinkStart).Trim();
        }

        // ``` で囲まれている場合は中身だけを取り出す
        if (cleaned.StartsWith("```") && cleaned.EndsWith("```"))
        {
            int firstNewLine = cleaned.IndexOf('\n');
            if (firstNewLine > 0)
            {
                cleaned = cleaned.Substring(firstNewLine + 1);
                if (cleaned.EndsWith("```"))
                {
                    cleaned = cleaned.Substring(0, cleaned.Length - 3).Trim();
                }
            }
        }

        return string.IsNullOrWhiteSpace(cleaned) ? fallbackOriginal : cleaned;
    }

    public void Dispose()
    {
        _httpClient.Dispose();
        try
        {
            if (FoundryLocalManager.IsInitialized)
            {
                FoundryLocalManager.Instance.Dispose();
            }
        }
        catch
        {
            // 終了時の解放エラーは無視
        }
    }
}

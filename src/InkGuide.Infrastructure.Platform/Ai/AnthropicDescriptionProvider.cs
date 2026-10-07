using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using InkGuide.Infrastructure.Platform.Common;
using InkGuide.Infrastructure.Platform.Logging;
using InkGuide.Infrastructure.Platform.Secrets;
using Microsoft.Extensions.Logging;

namespace InkGuide.Infrastructure.Platform.Ai;

// Local seam: the canonical IDescriptionProvider port is owned by Application (see D-006).

/// <summary>AI describer status.</summary>
public enum AiStatus
{
    NotConfigured,
    ConsentRequired,
    Ready,
    BudgetExceeded,
    Error,
}

/// <summary>Bounded describe request (excerpt-capped, never Sensitive).</summary>
public sealed record DescribeRequest(
    string NodeId,
    string FileName,
    string? MimeType,
    string MetadataJson,
    string Excerpt,
    IReadOnlyList<string> SiblingNames,
    string? ProjectContext,
    bool Sensitive);

/// <summary>Validated describe result.</summary>
public sealed record DescribeResult(
    string Summary,
    string? Role,
    string? KindHint,
    IReadOnlyList<string> TagSuggestions,
    double Confidence);

/// <summary>Optional AI describer port (off by default, privacy-guarded).</summary>
public interface IDescriptionProvider
{
    Task<Result<DescribeResult>> DescribeAsync(DescribeRequest request, CancellationToken cancellationToken);

    Task<Result<AiStatus>> GetStatusAsync(CancellationToken cancellationToken);

    Task<Result> TestConnectionAsync(CancellationToken cancellationToken);
}

/// <summary>Options for the Anthropic describer.</summary>
public sealed class AnthropicOptions
{
    public bool Enabled { get; set; }

    public string Model { get; set; } = "claude-haiku-5-5";

    public int BudgetPerDay { get; set; } = 500;

    public int ExcerptBytes { get; set; } = 6 * 1024;

    public bool ConsentAcknowledged { get; set; }

    public DateTimeOffset? ConsentAtUtc { get; set; }

    public string StateDirectory { get; set; } = string.Empty;

    public string MessagesEndpoint { get; set; } = "https://api.anthropic.com/v1/messages";

    public string ModelsEndpoint { get; set; } = "https://api.anthropic.com/v1/models";

    public string AnthropicVersion { get; set; } = "2023-06-01";
}

/// <summary>
/// Anthropic-backed describer with privacy guards: off by default, consent + key required,
/// per-repo opt-out honored by callers, never Sensitive/binary/image, 6 KB excerpt cap,
/// secrets stripped, daily budget, strict JSON validation, harmless ai.test.
/// </summary>
public sealed class AnthropicDescriptionProvider : IDescriptionProvider, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly ISecretStore _secrets;
    private readonly AnthropicOptions _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<AnthropicDescriptionProvider> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;

    public AnthropicDescriptionProvider(HttpClient http, ISecretStore secrets, AnthropicOptions options, TimeProvider clock, ILogger<AnthropicDescriptionProvider> logger)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(secrets);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);
        _http = http;
        _secrets = secrets;
        _options = options;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>Secret-store key for the Anthropic API key.</summary>
    public static string ApiKeyName => "anthropic-api-key";

    /// <inheritdoc />
    public async Task<Result<DescribeResult>> DescribeAsync(DescribeRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        Result guard = await CheckGuardsAsync(cancellationToken).ConfigureAwait(false);
        if (!guard.IsSuccess)
        {
            return Result.Fail<DescribeResult>(guard.Failure!);
        }

        if (request.Sensitive)
        {
            return Result.Fail<DescribeResult>(OperationError.SensitiveBlocked("Sensitive nodes are never sent to AI."));
        }

        string? apiKey = await _secrets.GetAsync(ApiKeyName, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrEmpty(apiKey))
        {
            return Result.Fail<DescribeResult>(new OperationError("AI_NOT_CONFIGURED", "No Anthropic API key is stored."));
        }

        string excerpt = ClipToBytes(SecretScrubber.Scrub(request.Excerpt), _options.ExcerptBytes);
        string prompt = await BuildPromptAsync(request, excerpt, cancellationToken).ConfigureAwait(false);
        string body = JsonSerializer.Serialize(new
        {
            model = _options.Model,
            max_tokens = 300,
            messages = new[] { new { role = "user", content = prompt } },
        }, JsonOptions);

        Result<string> completion = await PostWithRetryAsync(_options.MessagesEndpoint, apiKey!, body, isTest: false, cancellationToken).ConfigureAwait(false);
        if (!completion.IsSuccess || completion.Value is null)
        {
            return Result.Fail<DescribeResult>(completion.Failure!);
        }

        Result<DescribeResult> parsed = ParseCompletion(completion.Value);
        if (!parsed.IsSuccess)
        {
            return parsed;
        }

        await RecordUsageAsync(cancellationToken).ConfigureAwait(false);
        return parsed;
    }

    /// <inheritdoc />
    public async Task<Result<AiStatus>> GetStatusAsync(CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return Result.Ok(AiStatus.NotConfigured);
        }

        string? apiKey = await _secrets.GetAsync(ApiKeyName, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrEmpty(apiKey))
        {
            return Result.Ok(AiStatus.NotConfigured);
        }

        if (!_options.ConsentAcknowledged)
        {
            return Result.Ok(AiStatus.ConsentRequired);
        }

        int used = await ReadUsageAsync(_clock.GetUtcNow(), cancellationToken).ConfigureAwait(false);
        if (used >= _options.BudgetPerDay)
        {
            return Result.Ok(AiStatus.BudgetExceeded);
        }

        return Result.Ok(AiStatus.Ready);
    }

    /// <inheritdoc />
    public async Task<Result> TestConnectionAsync(CancellationToken cancellationToken)
    {
        string? apiKey = await _secrets.GetAsync(ApiKeyName, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrEmpty(apiKey))
        {
            return Result.Fail(new OperationError("AI_NOT_CONFIGURED", "No Anthropic API key is stored."));
        }

        Result<string> probed = await PostWithRetryAsync(_options.ModelsEndpoint, apiKey!, string.Empty, isTest: true, cancellationToken).ConfigureAwait(false);
        return probed.IsSuccess ? Result.Ok() : Result.Fail(probed.Failure!);
    }

    private async Task<Result> CheckGuardsAsync(CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return Result.Fail(new OperationError("AI_NOT_CONFIGURED", "The AI describer is off."));
        }

        if (!_options.ConsentAcknowledged)
        {
            return Result.Fail(new OperationError("AI_CONSENT_REQUIRED", "Data-sharing consent is required."));
        }

        int used = await ReadUsageAsync(_clock.GetUtcNow(), cancellationToken).ConfigureAwait(false);
        if (used >= _options.BudgetPerDay)
        {
            PlatformLog.AiBudgetExhausted(_logger, used, _options.BudgetPerDay);
            return Result.Fail(new OperationError("AI_BUDGET_EXCEEDED", "Daily AI request budget is exhausted."));
        }

        return Result.Ok();
    }

    private async Task<Result<string>> PostWithRetryAsync(string url, string apiKey, string body, bool isTest, CancellationToken cancellationToken)
    {
        const int maxAttempts = 4;
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(isTest ? HttpMethod.Get : HttpMethod.Post, url);
                request.Headers.Add("x-api-key", apiKey);
                request.Headers.Add("anthropic-version", _options.AnthropicVersion);
                if (!isTest)
                {
                    request.Content = new StringContent(body, Encoding.UTF8, "application/json");
                }

                using HttpResponseMessage response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
                if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500)
                {
                    TimeSpan delay = RetryDelay(response, attempt);
                    PlatformLog.AiRequestFailed(_logger, attempt, ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture));
                    if (attempt < maxAttempts)
                    {
                        await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    return Result.Fail<string>(new OperationError("RATE_LIMITED", "Anthropic request was rate limited.", true));
                }

                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    return Result.Fail<string>(new OperationError("AI_NOT_CONFIGURED", "The Anthropic API key was rejected."));
                }

                if (!response.IsSuccessStatusCode)
                {
                    return Result.Fail<string>(OperationError.Provider("Anthropic request failed (" + ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture) + ")."));
                }

                string text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                return Result.Ok(text);
            }
            catch (HttpRequestException ex)
            {
                PlatformLog.AiRequestFailed(_logger, attempt, LogRedactor.Redact(ex.Message));
                if (attempt == maxAttempts)
                {
                    return Result.Fail<string>(OperationError.Provider("Anthropic request failed: network error.", true));
                }

                await Task.Delay(TimeSpan.FromMilliseconds(500 * attempt), cancellationToken).ConfigureAwait(false);
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                if (attempt == maxAttempts)
                {
                    return Result.Fail<string>(OperationError.Provider("Anthropic request timed out.", true));
                }

                await Task.Delay(TimeSpan.FromMilliseconds(500 * attempt), cancellationToken).ConfigureAwait(false);
            }
        }

        return Result.Fail<string>(OperationError.Internal("Unreachable retry state."));
    }

    private static TimeSpan RetryDelay(HttpResponseMessage response, int attempt)
    {
        if (response.Headers.RetryAfter?.Delta is TimeSpan delta && delta > TimeSpan.Zero)
        {
            return delta > TimeSpan.FromSeconds(60) ? TimeSpan.FromSeconds(60) : delta;
        }

        return TimeSpan.FromMilliseconds(Math.Min(8000, 500 * Math.Pow(2, attempt - 1)));
    }

    private static Result<DescribeResult> ParseCompletion(string responseBody)
    {
        string? text;
        try
        {
            using JsonDocument envelope = JsonDocument.Parse(responseBody);
            if (!envelope.RootElement.TryGetProperty("content", out JsonElement content) || content.GetArrayLength() == 0)
            {
                return Result.Fail<DescribeResult>(OperationError.Provider("Anthropic response had no content."));
            }

            text = content[0].TryGetProperty("text", out JsonElement textElement) ? textElement.GetString() : null;
        }
        catch (JsonException ex)
        {
            return Result.Fail<DescribeResult>(OperationError.Provider("Anthropic response was invalid: " + ex.Message, true));
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return Result.Fail<DescribeResult>(OperationError.Provider("Anthropic response had no text."));
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(text);
            JsonElement root = document.RootElement;
            if (!root.TryGetProperty("summary", out JsonElement summaryElement) || summaryElement.GetString() is not string summary || summary.Length == 0 || summary.Length > 2000)
            {
                return Result.Fail<DescribeResult>(OperationError.Provider("Anthropic JSON failed strict validation (summary)."));
            }

            string? role = root.TryGetProperty("role", out JsonElement roleElement) ? roleElement.GetString() : null;
            string? kindHint = root.TryGetProperty("kindHint", out JsonElement kindElement) ? kindElement.GetString() : null;
            var tags = new List<string>();
            if (root.TryGetProperty("tagSuggestions", out JsonElement tagsElement) && tagsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement tag in tagsElement.EnumerateArray())
                {
                    if (tag.GetString() is string name && name.Length > 0 && name.Length <= 64 && tags.Count < 8)
                    {
                        tags.Add(name);
                    }
                }
            }

            double confidence = 0.5;
            if (root.TryGetProperty("confidence", out JsonElement confidenceElement) && confidenceElement.ValueKind == JsonValueKind.Number)
            {
                confidence = Math.Clamp(confidenceElement.GetDouble(), 0.0, 1.0);
            }

            return Result.Ok(new DescribeResult(summary, role, kindHint, tags, confidence));
        }
        catch (JsonException ex)
        {
            return Result.Fail<DescribeResult>(OperationError.Provider("Anthropic JSON failed strict validation: " + ex.Message));
        }
    }

    private static async Task<string> BuildPromptAsync(DescribeRequest request, string excerpt, CancellationToken cancellationToken)
    {
        string template;
        try
        {
            using Stream? stream = typeof(AnthropicDescriptionProvider).Assembly.GetManifestResourceStream("InkGuide.Infrastructure.Platform.Ai.Prompts.describe.txt");
            if (stream is null)
            {
                template = "{excerpt}";
            }
            else
            {
                using var reader = new StreamReader(stream, Encoding.UTF8);
                template = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (IOException)
        {
            template = "{excerpt}";
        }

        return template
            .Replace("{fileName}", request.FileName, StringComparison.Ordinal)
            .Replace("{mimeType}", request.MimeType ?? "unknown", StringComparison.Ordinal)
            .Replace("{projectContext}", request.ProjectContext ?? "none", StringComparison.Ordinal)
            .Replace("{siblings}", string.Join(", ", request.SiblingNames.Count > 12 ? request.SiblingNames.Take(12) : request.SiblingNames), StringComparison.Ordinal)
            .Replace("{facts}", request.MetadataJson, StringComparison.Ordinal)
            .Replace("{excerpt}", excerpt, StringComparison.Ordinal);
    }

    private static string ClipToBytes(string value, int maxBytes)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        if (bytes.Length <= maxBytes)
        {
            return value;
        }

        int cut = maxBytes;
        while (cut > 0 && (bytes[cut] & 0xC0) == 0x80)
        {
            cut--;
        }

        return Encoding.UTF8.GetString(bytes, 0, cut);
    }

    private async Task<int> ReadUsageAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await ReadUsageInternalAsync(now, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task RecordUsageAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            DateTimeOffset now = _clock.GetUtcNow();
            int used = await ReadUsageInternalAsync(now, cancellationToken).ConfigureAwait(false);
            var record = new UsageRecord(now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), used + 1);
            Directory.CreateDirectory(_options.StateDirectory);
            await File.WriteAllTextAsync(UsagePath(), JsonSerializer.Serialize(record, JsonOptions), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<int> ReadUsageInternalAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        string path = UsagePath();
        if (!File.Exists(path))
        {
            return 0;
        }

        try
        {
            string json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            UsageRecord? record = JsonSerializer.Deserialize<UsageRecord>(json, JsonOptions);
            string today = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            return record is not null && string.Equals(record.Day, today, StringComparison.Ordinal) ? record.Used : 0;
        }
        catch (IOException)
        {
            return 0;
        }
        catch (UnauthorizedAccessException)
        {
            return 0;
        }
        catch (JsonException)
        {
            return 0;
        }
    }

    private string UsagePath() => Path.Combine(_options.StateDirectory, "ai-usage.json");

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _gate.Dispose();
        GC.SuppressFinalize(this);
    }

    private sealed record UsageRecord(string Day, int Used);
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using InkGuide.Bridge.Common;
using InkGuide.Bridge.Contracts;
using InkGuide.Infrastructure.Persistence;
using InkGuide.Infrastructure.Platform.Ai;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using InkGuide.App.Hosting;
namespace InkGuide.App.Bridge.Backend;

public sealed partial class InkGuideBridgeBackend : Infrastructure.Platform.ExternalEdit.IExternalChangeHandler
{
    public async Task<Result<IReadOnlyList<ExternalSessionDto>>> ListExternalSessionsAsync(CancellationToken cancellationToken)
    {
        string accountId = await ActiveAccountAsync(cancellationToken).ConfigureAwait(false);
        var sessions = await _external.ListSessionsAsync(cancellationToken).ConfigureAwait(false);
        if (!sessions.IsSuccess || sessions.Value is null)
        {
            return Result.Fail<IReadOnlyList<ExternalSessionDto>>(BackendResults.ToBridge(sessions.Failure!));
        }

        var list = new List<ExternalSessionDto>();
        foreach (var session in sessions.Value)
        {
            if (!string.Equals(session.AccountId, accountId, StringComparison.Ordinal))
            {
                continue;
            }

            list.Add(new ExternalSessionDto(session.SessionId, session.NodeId, session.FileName, session.StartedAtUtc.ToString("o")));
        }

        return Result.Ok<IReadOnlyList<ExternalSessionDto>>(list);
    }

    public async Task<Result<EmptyResult>> EndExternalSessionAsync(string id, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);
        var ended = await _external.EndSessionAsync(id, false, cancellationToken).ConfigureAwait(false);
        if (!ended.IsSuccess)
        {
            return Result.Fail<EmptyResult>(BackendResults.ToBridge(ended.Failure!));
        }

        await _host.EmitEventAsync("external.sessionChanged", id).ConfigureAwait(false);
        return Result.Ok(new EmptyResult());
    }

    public async Task HandleExternalChangeAsync(string sessionId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sessionId);
        var sessions = await _external.ListSessionsAsync(cancellationToken).ConfigureAwait(false);
        if (!sessions.IsSuccess || sessions.Value is null)
        {
            return;
        }

        var session = sessions.Value.FirstOrDefault(s => string.Equals(s.SessionId, sessionId, StringComparison.Ordinal));
        if (session is null)
        {
            return;
        }

        string accountId = await ActiveAccountAsync(cancellationToken).ConfigureAwait(false);
        Result<ResolvedProvider> resolved = await ResolveProviderAsync(accountId, cancellationToken).ConfigureAwait(false);
        if (!resolved.IsSuccess || resolved.Value is null)
        {
            return;
        }

        byte[] bytes;
        try
        {
            bytes = await File.ReadAllBytesAsync(session.LocalPath, cancellationToken).ConfigureAwait(false);
        }
        catch (IOException)
        {
            return;
        }
        catch (UnauthorizedAccessException)
        {
            return;
        }

        await using var stream = new MemoryStream(bytes, writable: false);
        var updated = await resolved.Value.Provider.UpdateContentAsync(session.NodeId, stream, session.BaseVersion, null, null, cancellationToken).ConfigureAwait(false);
        if (updated.IsSuccess && updated.Value is not null)
        {
            await MirrorNodeAsync(accountId, updated.Value, cancellationToken).ConfigureAwait(false);
            await TriggerAnalysisAsync(accountId, resolved.Value, session.NodeId, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (string.Equals(updated.Failure?.Code, "CONFLICT_VERSION", StringComparison.Ordinal))
        {
            var remote = await resolved.Value.Provider.GetAsync(session.NodeId, cancellationToken).ConfigureAwait(false);
            if (remote.IsSuccess && remote.Value is not null)
            {
                await RecordContentConflictAsync(accountId, resolved.Value, remote.Value, System.Text.Encoding.UTF8.GetString(bytes), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public async Task<Result<AiStatusDto>> GetAiStatusAsync(CancellationToken cancellationToken)
    {
        await SyncAiOptionsFromSettingsAsync(cancellationToken).ConfigureAwait(false);
        var status = await _ai.GetStatusAsync(cancellationToken).ConfigureAwait(false);
        if (!status.IsSuccess)
        {
            return Result.Fail<AiStatusDto>(BackendResults.ToBridge(status.Failure!));
        }

        int used = await AiUsedTodayAsync(cancellationToken).ConfigureAwait(false);
        return Result.Ok(new AiStatusDto(ToAiStatusString(status.Value), used, _aiOptions.BudgetPerDay, _aiOptions.Model));
    }

    public async Task<Result<EmptyResult>> SetAiKeyAsync(SetAiKeyRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.ApiKey))
        {
            return Result.Fail<EmptyResult>(BackendResults.InvalidParams("API key is empty."));
        }

        try
        {
            await _secrets.SetAsync(AnthropicDescriptionProvider.ApiKeyName, request.ApiKey.Trim(), cancellationToken).ConfigureAwait(false);
        }
        catch (IOException ex)
        {
            return Result.Fail<EmptyResult>(BackendResults.Internal("Could not store the API key: " + ex.Message));
        }
        catch (UnauthorizedAccessException ex)
        {
            return Result.Fail<EmptyResult>(BackendResults.Internal("Could not store the API key: " + ex.Message));
        }

        return Result.Ok(new EmptyResult());
    }

    public async Task<Result<EmptyResult>> ClearAiKeyAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _secrets.DeleteAsync(AnthropicDescriptionProvider.ApiKeyName, cancellationToken).ConfigureAwait(false);
        }
        catch (IOException ex)
        {
            return Result.Fail<EmptyResult>(BackendResults.Internal("Could not clear the API key: " + ex.Message));
        }
        catch (UnauthorizedAccessException ex)
        {
            return Result.Fail<EmptyResult>(BackendResults.Internal("Could not clear the API key: " + ex.Message));
        }

        return Result.Ok(new EmptyResult());
    }

    public async Task<Result<EmptyResult>> AcknowledgeDataSharingAsync(AcknowledgeSharingRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Acknowledged)
        {
            await _settings.SetAsync(BackendSettingsService.Keys.AiConsentAt, UtcNow(), cancellationToken).ConfigureAwait(false);
            AppLogMessages.BackendNote(_logger, "AI data-sharing consent recorded.");
        }
        else
        {
            await _settings.SetAsync(BackendSettingsService.Keys.AiConsentAt, string.Empty, cancellationToken).ConfigureAwait(false);
        }

        await SyncAiOptionsFromSettingsAsync(cancellationToken).ConfigureAwait(false);
        return Result.Ok(new EmptyResult());
    }

    public async Task<Result<EmptyResult>> TestAiAsync(CancellationToken cancellationToken)
    {
        await SyncAiOptionsFromSettingsAsync(cancellationToken).ConfigureAwait(false);
        var tested = await _ai.TestConnectionAsync(cancellationToken).ConfigureAwait(false);
        if (!tested.IsSuccess)
        {
            return Result.Fail<EmptyResult>(BackendResults.ToBridge(tested.Failure!));
        }

        return Result.Ok(new EmptyResult());
    }

    public async Task<Result<AiUsageDto>> GetAiUsageAsync(CancellationToken cancellationToken)
    {
        await SyncAiOptionsFromSettingsAsync(cancellationToken).ConfigureAwait(false);
        int used = await AiUsedTodayAsync(cancellationToken).ConfigureAwait(false);
        return Result.Ok(new AiUsageDto(used, _aiOptions.BudgetPerDay, DateTimeOffset.UtcNow.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)));
    }

    private async Task<int> AiUsedTodayAsync(CancellationToken cancellationToken)
    {
        try
        {
            string path = Path.Combine(_aiOptions.StateDirectory, "ai-usage.json");
            if (!File.Exists(path))
            {
                return 0;
            }

            string json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;
            string today = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            if (root.TryGetProperty("Day", out JsonElement day) && day.GetString() == today &&
                root.TryGetProperty("Used", out JsonElement used) && used.TryGetInt32(out int count))
            {
                return count;
            }

            return 0;
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

    private static string ToAiStatusString(AiStatus status)
    {
        return status switch
        {
            AiStatus.NotConfigured => "notConfigured",
            AiStatus.ConsentRequired => "consentRequired",
            AiStatus.BudgetExceeded => "budgetExceeded",
            AiStatus.Ready => "ready",
            _ => "error",
        };
    }
}

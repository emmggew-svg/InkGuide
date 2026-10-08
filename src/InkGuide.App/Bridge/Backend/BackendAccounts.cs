using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using InkGuide.Bridge.Common;
using InkGuide.Bridge.Contracts;
using InkGuide.Infrastructure.Persistence;
using InkGuide.Infrastructure.Persistence.Entities;
using InkGuide.Infrastructure.Storage.Abstractions;
using InkGuide.Infrastructure.Storage.GoogleDrive;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using InkGuide.App.Hosting;
namespace InkGuide.App.Bridge.Backend;

public sealed partial class InkGuideBridgeBackend
{
    private readonly object _connectLock = new();
    private readonly Dictionary<string, CancellationTokenSource> _connectFlows = new(StringComparer.Ordinal);

    public async Task<Result<IReadOnlyList<AccountDto>>> ListAccountsAsync(CancellationToken cancellationToken)
    {
        await EnsureLocalAccountAsync(cancellationToken).ConfigureAwait(false);
        await using InkGuideDbContext db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        // SQLite cannot ORDER BY DateTimeOffset: sort in memory.
        List<AccountEntity> rows = (await db.Accounts.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false))
            .OrderBy(a => a.CreatedAtUtc)
            .ToList();
        var list = new List<AccountDto>();
        foreach (AccountEntity row in rows)
        {
            list.Add(new AccountDto(row.Id, row.Email, row.DisplayName, row.IsActive, row.ReauthRequired, row.ScopeMode));
        }

        return Result.Ok<IReadOnlyList<AccountDto>>(list);
    }

    public async Task<Result<AccountDto>> ConnectAsync(ConnectRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var client = await _oauth.ResolveClientAsync(cancellationToken).ConfigureAwait(false);
        if (!client.IsSuccess || client.Value is null)
        {
            return Result.Fail<AccountDto>(new OperationError(
                "OAUTH_NOT_CONFIGURED",
                "No OAuth client is configured. Import one via accounts.importOAuthClient (see docs/GOOGLE_SETUP.md).",
                false,
                null));
        }

        string accountId = "google-" + Guid.NewGuid().ToString("N", System.Globalization.CultureInfo.InvariantCulture)[..12];
        string scope = request.FullDrive ? "FullDrive" : "AppFolderOnly";
        await UpsertAccountAsync(accountId, string.Empty, string.Empty, scope, true, false, cancellationToken).ConfigureAwait(false);
        await _settings.SetAsync(BackendSettingsService.Keys.ActiveAccountId, accountId, cancellationToken).ConfigureAwait(false);
        await _settings.SetAsync(BackendSettingsService.Keys.DriveScopeMode, scope, cancellationToken).ConfigureAwait(false);

        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lock (_connectLock)
        {
            _connectFlows[accountId] = cts;
        }

        // The browser + loopback exchange runs in the background; the UI learns
        // the outcome through account.stateChanged (or cancelConnect).
        _ = Task.Run(async () =>
        {
            try
            {
                var connected = await _oauth.ConnectAsync(accountId, request.FullDrive, cts.Token).ConfigureAwait(false);
                if (connected.IsSuccess && connected.Value is not null)
                {
                    await UpsertAccountAsync(accountId, connected.Value.Email, connected.Value.DisplayName, scope, true, false, CancellationToken.None).ConfigureAwait(false);
                    await _host.EmitEventAsync("account.stateChanged", accountId).ConfigureAwait(false);
                    // Kick off the initial index so the workspace fills without a manual sync.
                    try
                    {
                        _ = await _sync.ForAccount(accountId).SyncNowAsync(null, CancellationToken.None).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                    }
                    catch (Exception ex)
                    {
                        AppLogMessages.BackendNote(_logger, "Initial index after connect failed: " + ex.Message);
                    }
                }
                else
                {
                    await UpsertAccountAsync(accountId, string.Empty, string.Empty, scope, false, true, CancellationToken.None).ConfigureAwait(false);
                    await _host.EmitEventAsync("account.stateChanged", accountId).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                await _host.EmitEventAsync("account.stateChanged", accountId).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AppLogMessages.BackendNote(_logger, "Connect flow failed: " + ex.Message);
                await _host.EmitEventAsync("account.stateChanged", accountId).ConfigureAwait(false);
            }
            finally
            {
                lock (_connectLock)
                {
                    _connectFlows.Remove(accountId);
                }

                cts.Dispose();
            }
        }, CancellationToken.None);

        return Result.Ok(new AccountDto(accountId, string.Empty, string.Empty, true, true, scope));
    }

    public Task<Result<EmptyResult>> CancelConnectAsync(CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        lock (_connectLock)
        {
            foreach (CancellationTokenSource cts in _connectFlows.Values)
            {
                try
                {
                    cts.Cancel();
                }
                catch (ObjectDisposedException)
                {
                }
            }

            _connectFlows.Clear();
        }

        return Task.FromResult(Result.Ok(new EmptyResult()));
    }

    public async Task<Result<EmptyResult>> DisconnectAsync(string accountId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(accountId);
        if (!string.Equals(accountId, LocalAccountId, StringComparison.Ordinal))
        {
            var revoked = await _oauth.DisconnectAsync(accountId, cancellationToken).ConfigureAwait(false);
            if (!revoked.IsSuccess)
            {
                return Result.Fail<EmptyResult>(BackendResults.ToBridge(revoked.Failure!));
            }
        }

        await using InkGuideDbContext db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await _gate.ExecuteWriteAsync(async ct =>
        {
            AccountEntity? row = await db.Accounts.FindAsync(new object[] { accountId }, ct).ConfigureAwait(false);
            if (row is not null)
            {
                if (!string.Equals(accountId, LocalAccountId, StringComparison.Ordinal))
                {
                    db.Accounts.Remove(row);
                }
                else
                {
                    row.IsActive = false;
                    row.UpdatedAtUtc = _clock.GetUtcNow();
                }

                _ = await db.SaveChangesAsync(ct).ConfigureAwait(false);
            }
        }, cancellationToken).ConfigureAwait(false);
        return Result.Ok(new EmptyResult());
    }

    public async Task<Result<EmptyResult>> SetActiveAsync(string accountId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(accountId);
        await using InkGuideDbContext db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        bool found = false;
        await _gate.ExecuteWriteAsync(async ct =>
        {
            List<AccountEntity> rows = await db.Accounts.ToListAsync(ct).ConfigureAwait(false);
            foreach (AccountEntity row in rows)
            {
                bool active = string.Equals(row.Id, accountId, StringComparison.Ordinal);
                if (active)
                {
                    found = true;
                }

                row.IsActive = active;
                row.UpdatedAtUtc = _clock.GetUtcNow();
            }

            _ = await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);

        if (!found)
        {
            return Result.Fail<EmptyResult>(BackendResults.NotFound("Account '" + accountId + "' was not found."));
        }

        await _settings.SetAsync(BackendSettingsService.Keys.ActiveAccountId, accountId, cancellationToken).ConfigureAwait(false);
        return Result.Ok(new EmptyResult());
    }

    public async Task<Result<EmptyResult>> ImportOAuthClientAsync(ImportOAuthClientRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.ClientJson))
        {
            return Result.Fail<EmptyResult>(BackendResults.InvalidParams("ClientJson is empty."));
        }

        var imported = await _oauth.ImportClientAsync(request.ClientJson, cancellationToken).ConfigureAwait(false);
        if (!imported.IsSuccess)
        {
            return Result.Fail<EmptyResult>(BackendResults.ToBridge(imported.Failure!));
        }

        return Result.Ok(new EmptyResult());
    }

    public async Task<Result<OAuthStatusDto>> GetOAuthStatusAsync(CancellationToken cancellationToken)
    {
        string? env = Environment.GetEnvironmentVariable("INKGUIDE_GOOGLE_CLIENT_JSON");
        if (!string.IsNullOrWhiteSpace(env))
        {
            return Result.Ok(new OAuthStatusDto(true, "environment"));
        }

        try
        {
            string? stored = await _secrets.GetAsync("google-oauth-client", cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(stored))
            {
                return Result.Ok(new OAuthStatusDto(true, "imported"));
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return Result.Ok(new OAuthStatusDto(false, "none"));
    }

    public async Task<Result<WorkspaceDto>> GetWorkspaceAsync(CancellationToken cancellationToken)
    {
        string accountId = await ActiveAccountAsync(cancellationToken).ConfigureAwait(false);
        await using InkGuideDbContext db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        WorkspaceEntity? row = await db.Workspaces.FindAsync(new object[] { accountId }, cancellationToken).ConfigureAwait(false);
        if (row is null)
        {
            return Result.Fail<WorkspaceDto>(BackendResults.NotFound("Workspace is not ensured yet. Call workspace.ensure."));
        }

        return Result.Ok(new WorkspaceDto(row.Id, row.Name, row.ProviderFolderId));
    }

    public async Task<Result<WorkspaceDto>> EnsureWorkspaceAsync(CancellationToken cancellationToken)
    {
        string accountId = await ActiveAccountAsync(cancellationToken).ConfigureAwait(false);
        Result<ResolvedProvider> resolved = await ResolveProviderAsync(accountId, cancellationToken).ConfigureAwait(false);
        if (!resolved.IsSuccess || resolved.Value is null)
        {
            return Result.Fail<WorkspaceDto>(resolved.Failure!);
        }

        var root = await resolved.Value.Provider.GetRootAsync(cancellationToken).ConfigureAwait(false);
        if (!root.IsSuccess || root.Value is null)
        {
            return Result.Fail<WorkspaceDto>(BackendResults.ToBridge(root.Failure!));
        }

        var children = await resolved.Value.Provider.ListChildrenAsync(root.Value.Id, null, 1000, cancellationToken).ConfigureAwait(false);
        if (!children.IsSuccess || children.Value is null)
        {
            return Result.Fail<WorkspaceDto>(BackendResults.ToBridge(children.Failure!));
        }

        string? workspaceId = null;
        foreach (var child in children.Value.Items)
        {
            if (child.IsFolder && !child.Trashed && string.Equals(child.Name, _options.WorkspaceFolderName, StringComparison.Ordinal))
            {
                workspaceId = child.Id;
                break;
            }
        }

        if (workspaceId is null)
        {
            var created = await resolved.Value.Provider.CreateFolderAsync(root.Value.Id, _options.WorkspaceFolderName, cancellationToken).ConfigureAwait(false);
            if (!created.IsSuccess || created.Value is null)
            {
                return Result.Fail<WorkspaceDto>(BackendResults.ToBridge(created.Failure!));
            }

            workspaceId = created.Value.Id;
            await JournalAsync(accountId, "workspace.ensure", workspaceId, "Workspace ensured.", false, null, null, cancellationToken).ConfigureAwait(false);
        }

        // System folders (Inbox) live directly under the workspace.
        await EnsureFolderAsync(resolved.Value.Provider, workspaceId, InboxFolderName, cancellationToken).ConfigureAwait(false);

        Result<WorkspaceEntity> row = await EnsureWorkspaceRowAsync(accountId, workspaceId, _options.WorkspaceFolderName, cancellationToken).ConfigureAwait(false);
        if (!row.IsSuccess || row.Value is null)
        {
            return Result.Fail<WorkspaceDto>(row.Failure!);
        }

        return Result.Ok(new WorkspaceDto(row.Value.Id, row.Value.Name, row.Value.ProviderFolderId));
    }

    public async Task<Result<WorkspaceStatsDto>> GetWorkspaceStatsAsync(CancellationToken cancellationToken)
    {
        string accountId = await ActiveAccountAsync(cancellationToken).ConfigureAwait(false);
        await using InkGuideDbContext db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        int repos = await db.Repositories.AsNoTracking().CountAsync(r => r.AccountId == accountId, cancellationToken).ConfigureAwait(false);
        int nodes = await db.Nodes.AsNoTracking().CountAsync(n => n.AccountId == accountId && !n.Trashed, cancellationToken).ConfigureAwait(false);
        long bytes = await db.Nodes.AsNoTracking().Where(n => n.AccountId == accountId && !n.Trashed && !n.IsFolder).SumAsync(n => (long?)n.Size, cancellationToken).ConfigureAwait(false) ?? 0L;
        return Result.Ok(new WorkspaceStatsDto(repos, bytes, nodes));
    }

    private async Task EnsureLocalAccountAsync(CancellationToken cancellationToken)
    {
        string enabled = await _settings.GetAsync(BackendSettingsService.Keys.LocalProviderEnabled, cancellationToken).ConfigureAwait(false);
        bool on = _options.LocalProviderEnabled || string.Equals(enabled, "true", StringComparison.OrdinalIgnoreCase);
        if (!on)
        {
            return;
        }

        await UpsertAccountAsync(LocalAccountId, "local@localfolder", "LocalFolder", "AppFolderOnly", true, false, cancellationToken).ConfigureAwait(false);
    }

    private async Task UpsertAccountAsync(string id, string email, string display, string scope, bool active, bool reauth, CancellationToken cancellationToken)
    {
        await using InkGuideDbContext db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await _gate.ExecuteWriteAsync(async ct =>
        {
            AccountEntity? row = await db.Accounts.FindAsync(new object[] { id }, ct).ConfigureAwait(false);
            if (row is null)
            {
                row = new AccountEntity { Id = id, CreatedAtUtc = _clock.GetUtcNow() };
                db.Accounts.Add(row);
            }

            row.Email = email;
            row.DisplayName = display;
            row.ScopeMode = scope;
            row.ReauthRequired = reauth;
            row.UpdatedAtUtc = _clock.GetUtcNow();
            if (active)
            {
                List<AccountEntity> others = await db.Accounts.Where(a => a.Id != id).ToListAsync(ct).ConfigureAwait(false);
                foreach (AccountEntity other in others)
                {
                    other.IsActive = false;
                }

                row.IsActive = true;
            }

            _ = await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task MarkReauthIfNeededAsync(string accountId, string code, CancellationToken cancellationToken)
    {
        if (!string.Equals(code, "REAUTH_REQUIRED", StringComparison.Ordinal))
        {
            return;
        }

        await using InkGuideDbContext db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await _gate.ExecuteWriteAsync(async ct =>
        {
            AccountEntity? row = await db.Accounts.FindAsync(new object[] { accountId }, ct).ConfigureAwait(false);
            if (row is not null && !row.ReauthRequired)
            {
                row.ReauthRequired = true;
                row.UpdatedAtUtc = _clock.GetUtcNow();
                _ = await db.SaveChangesAsync(ct).ConfigureAwait(false);
            }
        }, cancellationToken).ConfigureAwait(false);
        await _host.EmitEventAsync("account.stateChanged", accountId).ConfigureAwait(false);
    }

    private static async Task<StorageItem?> EnsureFolderAsync(
        IStorageProvider provider,
        string parentId,
        string name,
        CancellationToken cancellationToken)
    {
        var list = await provider.ListChildrenAsync(parentId, null, 1000, cancellationToken).ConfigureAwait(false);
        if (list.IsSuccess && list.Value is not null)
        {
            foreach (var child in list.Value.Items)
            {
                if (child.IsFolder && !child.Trashed && string.Equals(child.Name, name, StringComparison.Ordinal))
                {
                    return child;
                }
            }
        }

        var created = await provider.CreateFolderAsync(parentId, name, cancellationToken).ConfigureAwait(false);
        return created.IsSuccess ? created.Value : null;
    }
}

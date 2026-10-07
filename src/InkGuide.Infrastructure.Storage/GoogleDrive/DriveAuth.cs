using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using InkGuide.Infrastructure.Storage.Common;
using InkGuide.Infrastructure.Storage.Logging;
using Microsoft.Extensions.Logging;

namespace InkGuide.Infrastructure.Storage.GoogleDrive;

/// <summary>DPAPI-backed secret storage seam (implemented by Platform's DpapiSecretStore in the host).</summary>
public interface ISecretBackend
{
    Task<Result<string?>> GetAsync(string key, CancellationToken cancellationToken);

    Task<Result> SetAsync(string key, string value, CancellationToken cancellationToken);

    Task<Result> DeleteAsync(string key, CancellationToken cancellationToken);

    Task<Result<IReadOnlyList<string>>> ListKeysAsync(string prefix, CancellationToken cancellationToken);
}

/// <summary>Per-account access-token source with refresh and re-auth signalling.</summary>
public interface IAccessTokenProvider
{
    Task<Result<string>> GetAccessTokenAsync(string accountId, CancellationToken cancellationToken);

    Task<Result<string>> RefreshAccessTokenAsync(string accountId, CancellationToken cancellationToken);
}

/// <summary>OAuth client configuration (never committed; env/file or runtime import).</summary>
public sealed record OAuthClientConfig(string ClientId, string ClientSecret);

/// <summary>Result of a completed connect flow.</summary>
public sealed record OAuthAccount(string AccountId, string Email, string DisplayName);

/// <summary>Options for Google OAuth.</summary>
public sealed class GoogleOAuthOptions
{
    /// <summary>Env var holding the path to the client_secret JSON (build-time/owner machine).</summary>
    public string ClientJsonEnvVar { get; set; } = "INKGUIDE_GOOGLE_CLIENT_JSON";

    /// <summary>Secret-backend key for the runtime-imported OAuth client.</summary>
    public string ImportedClientKey { get; set; } = "google-oauth-client";

    /// <summary>Secret-backend key prefix for per-account tokens.</summary>
    public string TokenKeyPrefix { get; set; } = "google-token-";

    /// <summary>Browser-flow timeout.</summary>
    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromMinutes(5);
}

/// <summary>
/// Installed-app OAuth through the system browser, loopback redirect on a random free
/// port, PKCE (S256). Tokens are stored DPAPI-protected via <see cref="ISecretBackend"/>.
/// </summary>
public sealed class GoogleOAuthService : IAccessTokenProvider
{
    public const string SetupDocPointer = "docs/GOOGLE_SETUP.md";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClientBridge _http;
    private readonly ISecretBackend _secrets;
    private readonly GoogleOAuthOptions _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<GoogleOAuthService> _logger;

    public GoogleOAuthService(HttpClientBridge http, ISecretBackend secrets, GoogleOAuthOptions options, TimeProvider clock, ILogger<GoogleOAuthService> logger)
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

    /// <summary>Resolves the OAuth client: env/file, then runtime import, else OAUTH_NOT_CONFIGURED.</summary>
    public async Task<Result<OAuthClientConfig>> ResolveClientAsync(CancellationToken cancellationToken)
    {
        string? envPath = Environment.GetEnvironmentVariable(_options.ClientJsonEnvVar);
        if (!string.IsNullOrWhiteSpace(envPath) && File.Exists(envPath))
        {
            try
            {
                string json = await File.ReadAllTextAsync(envPath, cancellationToken).ConfigureAwait(false);
                Result<OAuthClientConfig> parsed = ParseClientJson(json);
                if (parsed.IsSuccess)
                {
                    return parsed;
                }
            }
            catch (IOException)
            {
                // Fall through to the imported client.
            }
            catch (UnauthorizedAccessException)
            {
                // Fall through to the imported client.
            }
        }

        Result<string?> imported = await _secrets.GetAsync(_options.ImportedClientKey, cancellationToken).ConfigureAwait(false);
        if (imported.IsSuccess && !string.IsNullOrWhiteSpace(imported.Value))
        {
            return ParseClientJson(imported.Value!);
        }

        StorageLog.OAuthNotConfigured(_logger);
        return Result.Fail<OAuthClientConfig>(new OperationError("OAUTH_NOT_CONFIGURED", "No OAuth client is configured. Import one via accounts.importOAuthClient; see " + SetupDocPointer + "."));
    }

    /// <summary>Stores a runtime-imported OAuth client JSON (accounts.importOAuthClient).</summary>
    public async Task<Result> ImportClientAsync(string clientJson, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(clientJson);
        Result<OAuthClientConfig> parsed = ParseClientJson(clientJson);
        if (!parsed.IsSuccess)
        {
            return Result.Fail(parsed.Failure!);
        }

        return await _secrets.SetAsync(_options.ImportedClientKey, clientJson, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Runs the browser connect flow and stores tokens for the account.</summary>
    public async Task<Result<OAuthAccount>> ConnectAsync(string accountId, bool fullDrive, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(accountId);
        Result<OAuthClientConfig> client = await ResolveClientAsync(cancellationToken).ConfigureAwait(false);
        if (!client.IsSuccess || client.Value is null)
        {
            return Result.Fail<OAuthAccount>(client.Failure!);
        }

        string verifier = Base64Url(RandomNumberGenerator.GetBytes(64));
        string challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

        string state = Base64Url(RandomNumberGenerator.GetBytes(32));
        using var listener = new HttpListener();
        string redirectUri = StartLoopback(listener);
        string scope = fullDrive ? DriveConstants.ScopeDrive : DriveConstants.ScopeFile;
        string authUrl = DriveConstants.AuthEndpoint
            + "?client_id=" + Uri.EscapeDataString(client.Value.ClientId)
            + "&redirect_uri=" + Uri.EscapeDataString(redirectUri)
            + "&response_type=code"
            + "&scope=" + Uri.EscapeDataString(scope)
            + "&code_challenge=" + Uri.EscapeDataString(challenge)
            + "&code_challenge_method=S256"
            + "&access_type=offline"
            + "&prompt=consent"
            + "&state=" + Uri.EscapeDataString(state);

        try
        {
            _ = Process.Start(new ProcessStartInfo(authUrl) { UseShellExecute = true });
        }
        catch (InvalidOperationException ex)
        {
            return Result.Fail<OAuthAccount>(OperationError.Provider("Could not open the system browser: " + ex.Message));
        }

        Result<string> code = await WaitForCodeAsync(listener, state, cancellationToken).ConfigureAwait(false);
        if (!code.IsSuccess || code.Value is null)
        {
            return Result.Fail<OAuthAccount>(code.Failure!);
        }

        Result<TokenRecord> tokens = await ExchangeCodeAsync(client.Value, code.Value, verifier, redirectUri, cancellationToken).ConfigureAwait(false);
        if (!tokens.IsSuccess || tokens.Value is null)
        {
            return Result.Fail<OAuthAccount>(tokens.Failure!);
        }

        Result stored = await _secrets.SetAsync(TokenKey(accountId), JsonSerializer.Serialize(tokens.Value, JsonOptions), cancellationToken).ConfigureAwait(false);
        if (!stored.IsSuccess)
        {
            return Result.Fail<OAuthAccount>(stored.Failure!);
        }

        return Result.Ok(new OAuthAccount(accountId, string.Empty, string.Empty));
    }

    /// <inheritdoc />
    public async Task<Result<string>> GetAccessTokenAsync(string accountId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(accountId);
        Result<TokenRecord?> loaded = await LoadTokensAsync(accountId, cancellationToken).ConfigureAwait(false);
        if (!loaded.IsSuccess || loaded.Value is null)
        {
            return Result.Fail<string>(OperationError.AuthRequired("No tokens stored; connect the account first."));
        }

        if (loaded.Value.ExpiresAtUtc - _clock.GetUtcNow() > TimeSpan.FromMinutes(2))
        {
            return Result.Ok(loaded.Value.AccessToken);
        }

        return await RefreshAccessTokenAsync(accountId, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Result<string>> RefreshAccessTokenAsync(string accountId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(accountId);
        Result<OAuthClientConfig> client = await ResolveClientAsync(cancellationToken).ConfigureAwait(false);
        if (!client.IsSuccess || client.Value is null)
        {
            return Result.Fail<string>(client.Failure!);
        }

        Result<TokenRecord?> loaded = await LoadTokensAsync(accountId, cancellationToken).ConfigureAwait(false);
        if (!loaded.IsSuccess || loaded.Value is null || string.IsNullOrEmpty(loaded.Value.RefreshToken))
        {
            StorageLog.ReauthRequired(_logger, accountId);
            return Result.Fail<string>(OperationError.ReauthRequired("Stored grant is missing; reconnect the account."));
        }

        Dictionary<string, string> form = new(StringComparer.Ordinal)
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = loaded.Value.RefreshToken!,
            ["client_id"] = client.Value.ClientId,
            ["client_secret"] = client.Value.ClientSecret,
        };
        Result<TokenResponse> response = await PostTokenAsync(form, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccess || response.Value is null)
        {
            if (IsInvalidGrant(response.Failure))
            {
                StorageLog.ReauthRequired(_logger, accountId);
                return Result.Fail<string>(OperationError.ReauthRequired("Stored grant was revoked or expired; reconnect the account."));
            }

            return Result.Fail<string>(response.Failure!);
        }

        var merged = new TokenRecord(
            response.Value.AccessToken ?? string.Empty,
            string.IsNullOrEmpty(response.Value.RefreshToken) ? loaded.Value.RefreshToken : response.Value.RefreshToken,
            _clock.GetUtcNow().AddSeconds(Math.Max(60, response.Value.ExpiresIn)),
            loaded.Value.Scope);
        _ = await _secrets.SetAsync(TokenKey(accountId), JsonSerializer.Serialize(merged, JsonOptions), cancellationToken).ConfigureAwait(false);
        return Result.Ok(merged.AccessToken);
    }

    /// <summary>Revokes the grant (best effort) and deletes stored tokens.</summary>
    public async Task<Result> DisconnectAsync(string accountId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(accountId);
        Result<TokenRecord?> loaded = await LoadTokensAsync(accountId, cancellationToken).ConfigureAwait(false);
        if (loaded.IsSuccess && loaded.Value is not null && !string.IsNullOrEmpty(loaded.Value.RefreshToken))
        {
            try
            {
                using var content = new FormUrlEncodedContent(new Dictionary<string, string>(StringComparer.Ordinal) { ["token"] = loaded.Value.RefreshToken! });
                using HttpResponseMessage response = await _http.Client.PostAsync(DriveConstants.RevokeEndpoint, content, cancellationToken).ConfigureAwait(false);
                _ = response;
            }
            catch (HttpRequestException)
            {
                // Best effort: still delete local tokens.
            }
            catch (TaskCanceledException)
            {
                // Best effort: still delete local tokens.
            }
        }

        return await _secrets.DeleteAsync(TokenKey(accountId), cancellationToken).ConfigureAwait(false);
    }

    private string TokenKey(string accountId) => _options.TokenKeyPrefix + accountId;

    private async Task<Result<TokenRecord?>> LoadTokensAsync(string accountId, CancellationToken cancellationToken)
    {
        Result<string?> stored = await _secrets.GetAsync(TokenKey(accountId), cancellationToken).ConfigureAwait(false);
        if (!stored.IsSuccess)
        {
            return Result.Fail<TokenRecord?>(stored.Failure!);
        }

        if (string.IsNullOrWhiteSpace(stored.Value))
        {
            return Result.Ok<TokenRecord?>(null);
        }

        try
        {
            return Result.Ok<TokenRecord?>(JsonSerializer.Deserialize<TokenRecord>(stored.Value!, JsonOptions));
        }
        catch (JsonException ex)
        {
            return Result.Fail<TokenRecord?>(OperationError.Provider("Stored tokens are corrupt: " + ex.Message));
        }
    }

    private async Task<Result<TokenResponse>> PostTokenAsync(Dictionary<string, string> form, CancellationToken cancellationToken)
    {
        try
        {
            using var content = new FormUrlEncodedContent(form);
            using HttpResponseMessage response = await _http.Client.PostAsync(DriveConstants.TokenEndpoint, content, cancellationToken).ConfigureAwait(false);
            string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            TokenResponse? parsed;
            try
            {
                parsed = JsonSerializer.Deserialize<TokenResponse>(body, JsonOptions);
            }
            catch (JsonException ex)
            {
                return Result.Fail<TokenResponse>(OperationError.Provider("Token endpoint returned invalid JSON: " + ex.Message, response.StatusCode >= HttpStatusCode.InternalServerError));
            }

            if (!response.IsSuccessStatusCode || parsed is null || string.IsNullOrEmpty(parsed.AccessToken))
            {
                string errorCode = parsed?.Error ?? ((int)response.StatusCode).ToString(System.Globalization.CultureInfo.InvariantCulture);
                bool retryable = (int)response.StatusCode == 429 || (int)response.StatusCode >= 500;
                return Result.Fail<TokenResponse>(new OperationError("PROVIDER_ERROR", "Token request failed (" + errorCode + ").", retryable));
            }

            return Result.Ok(parsed);
        }
        catch (HttpRequestException ex)
        {
            return Result.Fail<TokenResponse>(OperationError.Provider("Token request failed: " + ex.Message, true));
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            return Result.Fail<TokenResponse>(OperationError.Provider("Token request timed out: " + ex.Message, true));
        }
    }

    private async Task<Result<TokenRecord>> ExchangeCodeAsync(OAuthClientConfig client, string code, string verifier, string redirectUri, CancellationToken cancellationToken)
    {
        Dictionary<string, string> form = new(StringComparer.Ordinal)
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["code_verifier"] = verifier,
            ["client_id"] = client.ClientId,
            ["client_secret"] = client.ClientSecret,
            ["redirect_uri"] = redirectUri,
        };
        Result<TokenResponse> response = await PostTokenAsync(form, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccess || response.Value is null)
        {
            return Result.Fail<TokenRecord>(response.Failure!);
        }

        var record = new TokenRecord(
            response.Value.AccessToken ?? string.Empty,
            response.Value.RefreshToken,
            _clock.GetUtcNow().AddSeconds(Math.Max(60, response.Value.ExpiresIn)),
            response.Value.Scope);
        return Result.Ok(record);
    }

    private static string StartLoopback(HttpListener listener)
    {
        int port = GetFreePort();
        string prefix = "http://127.0.0.1:" + port.ToString(System.Globalization.CultureInfo.InvariantCulture) + "/authorize/";
        listener.Prefixes.Add(prefix);
        listener.Start();
        return prefix.TrimEnd('/');
    }

    private static int GetFreePort()
    {
        using var socket = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        socket.Start();
        int port = ((IPEndPoint)socket.LocalEndpoint).Port;
        socket.Stop();
        return port;
    }

    private async Task<Result<string>> WaitForCodeAsync(HttpListener listener, string state, CancellationToken cancellationToken)
    {
        using var timeout = new CancellationTokenSource(_options.ConnectTimeout);
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            HttpListenerContext context = await listener.GetContextAsync().WaitAsync(linked.Token).ConfigureAwait(false);
            string? code = context.Request.QueryString["code"];
            string? returnedState = context.Request.QueryString["state"];
            string? error = context.Request.QueryString["error"];
            bool ok = error is null && !string.IsNullOrEmpty(code) && string.Equals(returnedState, state, StringComparison.Ordinal);
            string page = ok ? "OK: InkGuide received the authorization code. You can close this tab." : "FAIL: authorization was not completed.";
            byte[] bytes = Encoding.ASCII.GetBytes(page);
            context.Response.ContentType = "text/plain; charset=utf-8";
            context.Response.ContentLength64 = bytes.Length;
            await context.Response.OutputStream.WriteAsync(bytes.AsMemory(), linked.Token).ConfigureAwait(false);
            context.Response.Close();
            if (!ok)
            {
                return Result.Fail<string>(OperationError.AuthRequired("Authorization was not completed (" + (error ?? "state mismatch") + ")."));
            }

            return Result.Ok(code!);
        }
        catch (OperationCanceledException)
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                return Result.Fail<string>(OperationError.AuthRequired("Authorization timed out."));
            }

            throw;
        }
        finally
        {
            listener.Stop();
        }
    }

    private static Result<OAuthClientConfig> ParseClientJson(string json)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            JsonElement section = root.TryGetProperty("installed", out JsonElement installed) ? installed : root;
            if (!section.TryGetProperty("client_id", out JsonElement id) || !section.TryGetProperty("client_secret", out JsonElement secret))
            {
                return Result.Fail<OAuthClientConfig>(OperationError.Invalid("OAuth client JSON must contain installed.client_id and installed.client_secret."));
            }

            string? clientId = id.GetString();
            string? clientSecret = secret.GetString();
            if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
            {
                return Result.Fail<OAuthClientConfig>(OperationError.Invalid("OAuth client JSON must contain installed.client_id and installed.client_secret."));
            }

            return Result.Ok(new OAuthClientConfig(clientId!, clientSecret!));
        }
        catch (JsonException ex)
        {
            return Result.Fail<OAuthClientConfig>(OperationError.Invalid("OAuth client JSON is invalid: " + ex.Message));
        }
    }

    private static bool IsInvalidGrant(OperationError? failure)
    {
        return failure is not null && failure.Message.Contains("invalid_grant", StringComparison.Ordinal);
    }

    private static string Base64Url(byte[] bytes)
    {
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private sealed record TokenResponse(
        [property: System.Text.Json.Serialization.JsonPropertyName("access_token")] string? AccessToken,
        [property: System.Text.Json.Serialization.JsonPropertyName("refresh_token")] string? RefreshToken,
        [property: System.Text.Json.Serialization.JsonPropertyName("expires_in")] int ExpiresIn,
        [property: System.Text.Json.Serialization.JsonPropertyName("scope")] string? Scope,
        [property: System.Text.Json.Serialization.JsonPropertyName("error")] string? Error);

    private sealed record TokenRecord(string AccessToken, string? RefreshToken, DateTimeOffset ExpiresAtUtc, string? Scope);
}

/// <summary>Thin HttpClient holder so OAuth and provider share testable construction.</summary>
public sealed class HttpClientBridge
{
    public HttpClientBridge(HttpClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        Client = client;
    }

    public HttpClient Client { get; }
}

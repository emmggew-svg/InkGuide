using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace InkGuide.App.Bridge;

/// <summary>
/// Bridge envelope (MASTER_PROMPT Section 14.2). JSON, camelCase, enums as strings.
/// Transport: UI -&gt; host postMessage(jsonString); host -&gt; UI PostWebMessageAsJson.
/// </summary>
internal static class BridgeEnvelope
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string Response(string id, string resultJson)
    {
        return "{\"v\":1,\"id\":" + JsonSerializer.Serialize(id, Options)
            + ",\"type\":\"res\",\"result\":" + resultJson + "}";
    }

    public static string ErrorResponse(string id, BridgeError error)
    {
        var payload = new ErrorResponsePayload { V = 1, Id = id, Type = "res", Error = error };
        return JsonSerializer.Serialize(payload, Options);
    }

    public static string Event(string method, string paramsJson)
    {
        return "{\"v\":1,\"id\":\"" + Guid.NewGuid().ToString("N")
            + "\",\"type\":\"evt\",\"method\":" + JsonSerializer.Serialize(method, Options)
            + ",\"params\":" + paramsJson + "}";
    }

    public static bool TryParseRequest(string json, out BridgeRequest? request)
    {
        request = null;
        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            string type = GetString(root, "type") ?? string.Empty;
            string id = GetString(root, "id") ?? string.Empty;
            if (id.Length == 0 || (type != "req" && type != "cancel"))
            {
                return false;
            }

            string method = GetString(root, "method") ?? string.Empty;
            string paramsJson = "{}";
            if (root.TryGetProperty("params", out JsonElement p) || root.TryGetProperty("Params", out p))
            {
                paramsJson = p.GetRawText();
            }

            request = new BridgeRequest(id, type, method, paramsJson);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string? GetString(JsonElement root, string camelName)
    {
        if (root.TryGetProperty(camelName, out JsonElement el) && el.ValueKind == JsonValueKind.String)
        {
            return el.GetString();
        }

        return null;
    }

    private sealed class ErrorResponsePayload
    {
        public int V { get; set; }
        public string Id { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public BridgeError? Error { get; set; }
    }
}

/// <summary>Parsed UI-to-host request.</summary>
internal sealed record BridgeRequest(string Id, string Type, string Method, string ParamsJson);

/// <summary>Typed bridge error. Only codes cross the bridge for UI use (Appendix C).</summary>
internal sealed record BridgeError(string Code, string Message, bool Retryable, object? Details)
{
    public static BridgeError NotFound(string method) =>
        new("NOT_FOUND", "No handler serves method '" + method + "'.", false, null);

    public static BridgeError BackendMissing(string method) =>
        new("UNKNOWN", "Method '" + method + "' is not served by this host build (backend module not loaded).", false, null);

    public static BridgeError InvalidParams(string detail) =>
        new("INVALID_PARAMS", detail, false, null);

    public static BridgeError Internal(string detail) =>
        new("INTERNAL", detail, false, null);

    public static BridgeError Cancelled(string id) =>
        new("CANCELLED", "Request '" + id + "' was cancelled.", false, null);
}

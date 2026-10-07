using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace InkGuide.Bridge.Contracts;

/// <summary>Central JSON settings: camelCase wire format, case-insensitive reads.</summary>
public static class BridgeJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}

/// <summary>Envelope message types.</summary>
public static class BridgeMessageTypes
{
    public const string Req = "req";
    public const string Res = "res";
    public const string Evt = "evt";
    public const string Cancel = "cancel";
}

/// <summary>Bridge error payload (code for UI use; message is developer-oriented English).</summary>
public sealed record BridgeErrorDto(string Code, string Message, bool Retryable, IReadOnlyDictionary<string, string>? Details);

/// <summary>Versioned envelope (v1). Transport: UI→host postMessage, host→UI PostWebMessageAsJson.</summary>
public sealed record BridgeEnvelope(
    int V,
    string Id,
    string Type,
    string? Method,
    JsonElement? Params,
    JsonElement? Result,
    BridgeErrorDto? Error);

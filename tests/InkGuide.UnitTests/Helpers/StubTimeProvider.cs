using System;
using Xunit;

namespace InkGuide.UnitTests.Helpers;

/// <summary>Controllable time provider for deterministic tests.</summary>
public sealed class StubTimeProvider : TimeProvider
{
    private readonly DateTimeOffset _utcNow;

    /// <summary>Creates a provider frozen at a fixed instant.</summary>
    public StubTimeProvider(DateTimeOffset utcNow)
    {
        _utcNow = utcNow;
    }

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow() => _utcNow;
}

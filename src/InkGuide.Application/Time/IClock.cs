using System;

namespace InkGuide.Application.Time;

/// <summary>
/// Clock abstraction. Implementations are backed by <see cref="TimeProvider"/>
/// so tests can control time; production code uses <see cref="TimeProvider.System"/>.
/// </summary>
public interface IClock
{
    /// <summary>Current UTC time.</summary>
    DateTimeOffset UtcNow { get; }
}

/// <summary>Default <see cref="IClock"/> backed by a <see cref="TimeProvider"/>.</summary>
public sealed class SystemClock : IClock
{
    private readonly TimeProvider _timeProvider;

    /// <summary>Creates a clock over a time provider.</summary>
    public SystemClock(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        _timeProvider = timeProvider;
    }

    /// <summary>Creates a clock over <see cref="TimeProvider.System"/>.</summary>
    public static SystemClock Shared => new(TimeProvider.System);

    /// <inheritdoc />
    public DateTimeOffset UtcNow => _timeProvider.GetUtcNow();
}

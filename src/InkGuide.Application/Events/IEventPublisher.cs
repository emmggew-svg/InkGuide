using System;
using System.Threading;
using System.Threading.Tasks;
using InkGuide.Domain.Common;

namespace InkGuide.Application.Events;

/// <summary>A domain event published to interested subscribers (sync state, node changes, conflicts, ...).</summary>
/// <param name="Topic">Event topic (for example <c>node.changed</c>, <c>sync.stateChanged</c>).</param>
/// <param name="OccurredAtUtc">When the event occurred (UTC).</param>
/// <param name="PayloadJson">JSON payload. Null for payload-less events. Never contains secrets or file bytes.</param>
public sealed record DomainEvent(string Topic, DateTimeOffset OccurredAtUtc, string? PayloadJson);

/// <summary>Publishes domain events to subscribers.</summary>
public interface IEventPublisher
{
    /// <summary>Publishes one event.</summary>
    Task<Result<Unit, Failure>> PublishAsync(DomainEvent domainEvent, CancellationToken ct);
}

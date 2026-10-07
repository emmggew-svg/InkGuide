using System.Collections.Generic;

namespace InkGuide.Infrastructure.Persistence.Common;

/// <summary>Outbox operation envelope passed to the executor.</summary>
public sealed record OutboxPayload(string OpId, string AccountId, string OperationType, string PayloadJson, int Attempts);

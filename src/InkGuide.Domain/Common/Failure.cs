using System.Collections.Generic;
using System.ComponentModel;

namespace InkGuide.Domain.Common;

/// <summary>
/// Typed failure information for expected (non-exceptional) failures.
/// Only <see cref="Code"/> crosses layers for UI use; <see cref="Message"/>
/// is English and developer-oriented, never shown verbatim to end users.
/// Named Failure (rather than Error) because Error collides with a
/// reserved keyword in other .NET languages (CA1716).
/// </summary>
/// <param name="Code">Stable machine-readable code from the ErrorCodes catalog (Appendix C).</param>
/// <param name="Message">Developer-oriented English description. Never contains secrets.</param>
/// <param name="Retryable">Whether retrying the same operation may succeed.</param>
public sealed record Failure(string Code, [Localizable(false)] string Message, bool Retryable)
{
    /// <summary>Optional machine-readable details. Keys and values never contain secrets.</summary>
    public IReadOnlyDictionary<string, string> Details { get; init; } = new Dictionary<string, string>();

    /// <summary>Lifts this failure into a result of any value type.</summary>
    public Result<TValue, Failure> ToResult<TValue>() => Result.Failure<TValue>(this);
}

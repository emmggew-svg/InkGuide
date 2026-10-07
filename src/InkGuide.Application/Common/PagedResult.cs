using System.Collections.Generic;

namespace InkGuide.Application.Common;

/// <summary>One page of a cursor-paginated listing. Cursors are opaque strings.</summary>
/// <typeparam name="T">Item type.</typeparam>
/// <param name="Items">Items on this page.</param>
/// <param name="NextCursor">Cursor for the next page. Null when the listing is complete.</param>
/// <param name="TotalCount">Total item count when the source can provide it cheaply. Null otherwise.</param>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, string? NextCursor, long? TotalCount = null);

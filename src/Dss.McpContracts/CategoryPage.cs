using System;
using System.Collections.Generic;

namespace Dss.McpContracts;

/// <summary>
/// Page token is the last id of the current page. The next page keeps ids strictly greater than that token.
/// </summary>
public static class CategoryPage
{
    public readonly struct SliceResult
    {
        public SliceResult(long[] ids, long? nextPageToken)
        {
            Ids = ids;
            NextPageToken = nextPageToken;
        }

        public long[] Ids { get; }
        public long? NextPageToken { get; }
    }

    public static SliceResult Slice(IReadOnlyList<long> sortedIds, long? pageToken, int pageSize)
    {
        if (pageSize < 1)
            throw new ArgumentOutOfRangeException(nameof(pageSize));

        var start = 0;
        if (pageToken is long token)
        {
            while (start < sortedIds.Count && sortedIds[start] <= token)
                start++;
        }

        var available = sortedIds.Count - start;
        var count = available > pageSize ? pageSize : available;
        var ids = new long[count];
        for (var i = 0; i < count; i++)
            ids[i] = sortedIds[start + i];

        long? next = available > pageSize ? ids[count - 1] : null;
        return new SliceResult(ids, next);
    }
}

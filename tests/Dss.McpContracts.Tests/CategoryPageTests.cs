using Dss.McpContracts;
using Xunit;

namespace Dss.McpContracts.Tests;

public class CategoryPageTests
{
    [Fact]
    public void Token_is_last_id_of_this_page_not_first_of_the_next()
    {
        var slice = CategoryPage.Slice(new long[] { 10, 20, 30, 40 }, pageToken: null, pageSize: 2);

        Assert.Equal(new long[] { 10, 20 }, slice.Ids);
        Assert.Equal(20, slice.NextPageToken);
    }

    [Fact]
    public void Walking_pages_does_not_skip_the_boundary_id()
    {
        var ids = new long[] { 1, 2, 3, 4, 5 };
        var seen = new List<long>();
        long? token = null;
        var guard = 0;
        while (guard++ < 10)
        {
            var slice = CategoryPage.Slice(ids, token, pageSize: 2);
            seen.AddRange(slice.Ids);
            if (slice.NextPageToken is null)
                break;
            token = slice.NextPageToken;
        }

        Assert.Equal(ids, seen);
    }

    [Fact]
    public void Last_page_has_no_token()
    {
        var slice = CategoryPage.Slice(new long[] { 1, 2, 3 }, pageToken: 2, pageSize: 2);

        Assert.Equal(new long[] { 3 }, slice.Ids);
        Assert.Null(slice.NextPageToken);
    }
}

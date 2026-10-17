using Dss.McpContracts;
using Xunit;

namespace Dss.McpContracts.Tests;

public class BatchDryRunPolicyTests
{
    [Fact]
    public void At_most_20_without_a_batch_flag_is_live()
    {
        Assert.False(BatchDryRunPolicy.ForceDryRun(batchDryRun: null, operationCount: 20));
    }

    [Fact]
    public void Over_20_without_a_batch_flag_is_dry()
    {
        Assert.True(BatchDryRunPolicy.ForceDryRun(batchDryRun: null, operationCount: 21));
    }

    [Fact]
    public void Explicit_batch_false_stays_live_over_20()
    {
        Assert.False(BatchDryRunPolicy.ForceDryRun(batchDryRun: false, operationCount: 21));
    }

    [Fact]
    public void Explicit_batch_true_is_dry_even_for_one_operation()
    {
        Assert.True(BatchDryRunPolicy.ForceDryRun(batchDryRun: true, operationCount: 1));
    }
}

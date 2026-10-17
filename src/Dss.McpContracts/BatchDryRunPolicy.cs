namespace Dss.McpContracts;

/// <summary>
/// Batch create stays a dry run above <see cref="LiveCreateLimit"/> unless the batch flag is explicitly false.
/// An operation-level dryRun flag does not lift that cap.
/// </summary>
public static class BatchDryRunPolicy
{
    public const int LiveCreateLimit = 20;

    public static bool ForceDryRun(bool? batchDryRun, int operationCount)
    {
        if (batchDryRun == true)
            return true;
        return operationCount > LiveCreateLimit && batchDryRun != false;
    }
}

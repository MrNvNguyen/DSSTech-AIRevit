using System.Collections.Concurrent;
using Dss.McpContracts;

namespace Dss.McpHost;

internal sealed class McpWorkItem
{
    public McpWorkItem(string requestJson, TaskCompletionSource<string> responseTcs)
    {
        RequestJson = requestJson;
        ResponseTcs = responseTcs;
    }

    public string RequestJson { get; }
    public TaskCompletionSource<string> ResponseTcs { get; }
}

internal sealed class McpRequestQueue
{
    private readonly ConcurrentQueue<McpWorkItem> _queue = new();

    public void Enqueue(McpWorkItem item) => _queue.Enqueue(item);

    public bool TryDequeue(out McpWorkItem? item) => _queue.TryDequeue(out item);

    public void CompleteAllWithError(string code, string message)
    {
        while (_queue.TryDequeue(out var item))
            item.ResponseTcs.TrySetResult(JsonRpcWriter.ErrorNullId(code, message));
    }
}

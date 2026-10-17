using System.Collections.Concurrent;
using System.Threading;
using Dss.McpContracts;

namespace Dss.McpHost;

internal sealed class McpWorkItem
{
    private int _abandoned;

    public McpWorkItem(string requestJson, TaskCompletionSource<string> responseTcs)
    {
        RequestJson = requestJson;
        ResponseTcs = responseTcs;
    }

    public string RequestJson { get; }
    public TaskCompletionSource<string> ResponseTcs { get; }

    public bool IsAbandoned => Volatile.Read(ref _abandoned) != 0;

    public void Abandon() => Interlocked.Exchange(ref _abandoned, 1);
}

internal sealed class McpRequestQueue
{
    private readonly ConcurrentQueue<McpWorkItem> _queue = new();

    public void Enqueue(McpWorkItem item) => _queue.Enqueue(item);

    public bool TryDequeue(out McpWorkItem? item) => _queue.TryDequeue(out item);

    public bool IsEmpty => _queue.IsEmpty;

    public void CompleteAllWithError(string code, string message)
    {
        while (_queue.TryDequeue(out var item))
            item.ResponseTcs.TrySetResult(JsonRpcWriter.ErrorNullId(code, message));
    }
}

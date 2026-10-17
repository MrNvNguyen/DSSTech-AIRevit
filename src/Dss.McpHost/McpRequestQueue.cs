using Dss.McpContracts;

namespace Dss.McpHost;

internal sealed class McpWorkItem
{
    private const int Pending = 0;
    private const int Claimed = 1;
    private const int Abandoned = 2;

    private int _state;
    private long _deadlineUtcTicks;

    public McpWorkItem(string requestJson, TaskCompletionSource<string> responseTcs)
    {
        RequestJson = requestJson;
        ResponseTcs = responseTcs;
    }

    public string RequestJson { get; }
    public TaskCompletionSource<string> ResponseTcs { get; }

    public void ArmTimeout(TimeSpan timeout) =>
        _deadlineUtcTicks = DateTime.UtcNow.Add(timeout).Ticks;

    /// <summary>Caller holds the queue lock. True only when this call won execution.</summary>
    public bool TryClaim(DateTime utcNow)
    {
        if (_state != Pending)
            return false;
        if (_deadlineUtcTicks != 0 && utcNow.Ticks >= _deadlineUtcTicks)
        {
            _state = Abandoned;
            return false;
        }

        _state = Claimed;
        return true;
    }

    /// <summary>Caller holds the queue lock. True when the item will not run.</summary>
    public bool TryAbandon()
    {
        if (_state != Pending)
            return false;
        _state = Abandoned;
        return true;
    }
}

internal sealed class McpRequestQueue
{
    private readonly object _gate = new();
    private readonly Queue<McpWorkItem> _items = new();

    public void Enqueue(McpWorkItem item, TimeSpan timeout)
    {
        item.ArmTimeout(timeout);
        lock (_gate)
            _items.Enqueue(item);
    }

    public bool IsEmpty
    {
        get
        {
            lock (_gate)
                return _items.Count == 0;
        }
    }

    /// <summary>
    /// Takes the next item that is still allowed to run.
    /// Abandon and this claim share <see cref="_gate"/>, so a timeout cannot lose the race
    /// to a dequeue that already observed a clear flag.
    /// </summary>
    public bool TryDequeue(out McpWorkItem? item)
    {
        lock (_gate)
        {
            var now = DateTime.UtcNow;
            while (_items.Count > 0)
            {
                var next = _items.Dequeue();
                if (next.TryClaim(now))
                {
                    item = next;
                    return true;
                }

                next.ResponseTcs.TrySetResult(JsonRpcWriter.ErrorNullId("timeout", "MCP request timed out."));
            }

            item = null;
            return false;
        }
    }

    public void Abandon(McpWorkItem item)
    {
        lock (_gate)
            item.TryAbandon();
    }

    public void CompleteAllWithError(string code, string message)
    {
        lock (_gate)
        {
            while (_items.Count > 0)
            {
                var item = _items.Dequeue();
                item.TryAbandon();
                item.ResponseTcs.TrySetResult(JsonRpcWriter.ErrorNullId(code, message));
            }
        }
    }
}

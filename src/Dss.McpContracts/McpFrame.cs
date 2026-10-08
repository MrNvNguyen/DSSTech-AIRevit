using System;
using System.Buffers.Binary;
using System.Text;

namespace Dss.McpContracts;

public static class McpFrame
{
    public const int MaxPayloadBytes = 1024 * 1024;

    public static byte[] Encode(string json)
    {
        var payload = Encoding.UTF8.GetBytes(json);
        if (payload.Length > MaxPayloadBytes)
            throw new InvalidOperationException($"MCP frame exceeds {MaxPayloadBytes} bytes.");

        var frame = new byte[4 + payload.Length];
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(0, 4), payload.Length);
        payload.CopyTo(frame, 4);
        return frame;
    }

    public static bool TryReadFrame(FrameBuffer buffer, out string? json)
    {
        json = null;
        if (buffer.Count < 4)
            return false;

        var length = BinaryPrimitives.ReadInt32LittleEndian(buffer.Peek(4));
        if (length < 0 || length > MaxPayloadBytes)
            throw new InvalidOperationException($"Invalid MCP frame length: {length}.");

        if (buffer.Count < 4 + length)
            return false;

        buffer.Consume(4);
        var payload = buffer.Consume(length);
        json = Encoding.UTF8.GetString(payload);
        return true;
    }
}

public sealed class FrameBuffer
{
    private byte[] _data = Array.Empty<byte>();
    private int _count;

    public int Count => _count;

    public void Append(ReadOnlySpan<byte> chunk)
    {
        if (chunk.IsEmpty)
            return;

        EnsureCapacity(_count + chunk.Length);
        chunk.CopyTo(_data.AsSpan(_count));
        _count += chunk.Length;
    }

    public ReadOnlySpan<byte> Peek(int count)
    {
        if (count > _count)
            count = _count;
        return _data.AsSpan(0, count);
    }

    public byte[] Consume(int count)
    {
        if (count > _count)
            throw new InvalidOperationException("FrameBuffer underflow.");

        var result = new byte[count];
        Buffer.BlockCopy(_data, 0, result, 0, count);
        var remaining = _count - count;
        if (remaining > 0)
            Buffer.BlockCopy(_data, count, _data, 0, remaining);
        _count = remaining;
        return result;
    }

    public void Clear() => _count = 0;

    private void EnsureCapacity(int needed)
    {
        if (_data.Length >= needed)
            return;

        var size = Math.Max(256, _data.Length * 2);
        while (size < needed)
            size *= 2;
        Array.Resize(ref _data, size);
    }
}

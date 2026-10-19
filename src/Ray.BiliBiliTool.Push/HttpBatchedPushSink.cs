using System.Collections.Concurrent;
using System.Text;
using Serilog.Core;
using Serilog.Events;

namespace Ray.BiliBiliTool.Push;

/// <summary>
/// Minimal batched HTTP push sink used by the built-in push channels that have no upstream
/// Ray.Serilog sink package (Feishu bot, Bark, PushDeer). It queues log messages and sends
/// them as one aggregated HTTP request every interval; on dispose it flushes synchronously so
/// a test push completes before the logger is torn down.
///
/// Delivery is delegated to <paramref name="send"/>; configuration is applied through
/// <paramref name="isConfigured"/> (e.g. "webhook/token non-empty"), so an unconfigured channel
/// stays quiet instead of failing on every event.
/// </summary>
public sealed class HttpBatchedPushSink : ILogEventSink, IDisposable
{
    private const int MaxMessagesPerBatch = 40;
    private const int MaxMessageLength = 3500;
    private const int BatchIntervalSeconds = 10;

    private readonly Func<string, CancellationToken, Task> _send;
    private readonly Func<bool> _isConfigured;
    private readonly ConcurrentQueue<string> _queue = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly object _flushLock = new();
    private Thread? _worker;
    private volatile bool _disposed;

    public HttpBatchedPushSink(
        Func<string, CancellationToken, Task> send,
        Func<bool> isConfigured
    )
    {
        _send = send;
        _isConfigured = isConfigured;
        _worker = new Thread(WorkerLoop)
        {
            IsBackground = true,
            Name = $"{nameof(HttpBatchedPushSink)}-{GetHashCode():x}",
        };
        _worker.Start();
    }

    public void Emit(LogEvent logEvent)
    {
        if (_disposed || !_isConfigured())
            return;

        var line = logEvent.RenderMessage().Trim();
        if (string.IsNullOrWhiteSpace(line))
            return;
        if (line.Length > MaxMessageLength)
            line = line[..MaxMessageLength] + "…";

        _queue.Enqueue(line);
    }

    private void WorkerLoop()
    {
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    Drain();
                }
                catch
                {
                    // keep the loop alive; failures are reported by the send delegate
                }

                _cts.Token.WaitHandle.WaitOne(TimeSpan.FromSeconds(BatchIntervalSeconds));
            }
        }
        catch (OperationCanceledException)
        {
            // normal shutdown
        }
    }

    private void Drain()
    {
        if (_queue.IsEmpty || !_isConfigured())
            return;

        lock (_flushLock)
        {
            if (_queue.IsEmpty)
                return;

            var builder = new StringBuilder();
            var count = 0;
            while (count < MaxMessagesPerBatch && _queue.TryDequeue(out var line))
            {
                builder.AppendLine(line);
                count++;
            }
            if (count == 0)
                return;

            // 发送用 CancellationToken.None：Dispose 阶段已取消 _cts，但最终 flush 仍要
            // 把已入队的消息发出去，不能因取消而静默丢弃。
            _send(builder.ToString().TrimEnd(), CancellationToken.None).Wait(TimeSpan.FromSeconds(8));
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        try
        {
            _cts.Cancel();
        }
        catch
        {
            // ignore
        }

        try
        {
            Drain();
        }
        catch
        {
            // best effort final flush
        }

        _worker?.Join(TimeSpan.FromSeconds(4));
        _worker = null;
        _cts.Dispose();
    }
}

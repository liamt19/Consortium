using System.Text;
using System.Threading.Channels;

namespace Consortium.Misc;

public static class BatchedConsoleWriter
{
    private const int BatchSize = 64;
    private const int BatchDelayMs = 3;

    private static readonly Channel<string> _writeChannel = Channel.CreateUnbounded<string>(new() { SingleReader = true });
    private static readonly Task? _batchTask = Task.Run(BatchedOutputTaskProc);

    public static void Complete() => _writeChannel.Writer.Complete();
    public static void Write(string line) => _writeChannel.Writer.TryWrite(line);
    public static void WriteLine(string line) => _writeChannel.Writer.TryWrite(line + Environment.NewLine);

    private static async Task BatchedOutputTaskProc()
    {
        var buffer = new List<string>(BatchSize);
        var channelReader = _writeChannel.Reader;
        var sb = new StringBuilder(1024);

        while (await channelReader.WaitToReadAsync())
        {
            buffer.Clear();
            sb.Clear();

            channelReader.TryRead(out var first);
            buffer.Add(first);

            var deadline = DateTime.UtcNow.AddMilliseconds(BatchDelayMs);

            while (buffer.Count < BatchSize)
            {
                var remaining = deadline - DateTime.UtcNow;
                if (remaining <= TimeSpan.Zero)
                    break;

                using var cts = new CancellationTokenSource(remaining);
                try
                {
                    if (await channelReader.WaitToReadAsync(cts.Token))
                    {
                        while (buffer.Count < BatchSize && channelReader.TryRead(out var item))
                            buffer.Add(item);
                    }
                }
                catch (OperationCanceledException) { break; }
            }

            if (buffer.Count > 0)
            {
                sb.AppendJoin(string.Empty, buffer);
                Console.Write(sb.ToString());
            }
        }
    }

#if NO
    private static async Task AsyncOutputTaskProc()
    {
        await foreach (var line in _writeChannel.Reader.ReadAllAsync())
            Console.Write(line);
    }
#endif
#if ALSO_NO
    private static async Task BatchedOutputTaskProc()
    {
        var buffer = new List<string>(BatchSize);
        var delayCts = new CancellationTokenSource();
        var channelReader = _writeChannel.Reader;

        while (true)
        {
            // Read 1 line, start a timer. Flush when the timer expires or we have BatchSize lines.
            var first = await channelReader.ReadAsync();
            buffer.Add(first);

            await delayCts.CancelAsync();
            delayCts = new CancellationTokenSource();
            var delayTask = Task.Delay(TimeSpan.FromMilliseconds(BatchDelayMs), delayCts.Token);

            while (buffer.Count < BatchSize)
            {
                var readTask = channelReader.ReadAsync().AsTask();
                var readOrDelay = await Task.WhenAny(readTask, delayTask);

                if (readOrDelay == delayTask)
                    break;

                buffer.Add(await readTask);
            }

            await delayCts.CancelAsync();

            Console.WriteLine(string.Join(Environment.NewLine, buffer));
            buffer.Clear();
        }
    }
#endif

}

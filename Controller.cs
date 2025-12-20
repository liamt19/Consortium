using Consortium.UCI;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Channels;
using System.Xml.Linq;

namespace Consortium;

public class Controller
{
    private const bool ALWAYS_PRINT = false;
    private bool SyncByDepth = true;

    private List<Engine> Engines = [];
    private readonly ConcurrentDictionary<string, List<UciOutput>> ImmediateOutputData = [];
    private readonly ConcurrentDictionary<string, List<UciOutput>> InfoOutputData = [];
    private readonly ConcurrentDictionary<string, int> ReachedDepths = [];
    private readonly ConcurrentDictionary<string, int> DataCursors = [];

    private readonly Channel<(string Eng, string Line)> DataChannel = Channel.CreateUnbounded<(string, string)>();

    private Task? ReaderTask;
    private Task? WriterTask;

    private CancellationTokenSource ReaderTokenSource = new();
    private CancellationTokenSource WriterTokenSource = new();

    private readonly SemaphoreSlim DataReadSignal = new(0);

    public Controller()
    {
        LoadEngines();
        ResetOutputData(false);

        foreach (var eng in Engines)
        {
            eng.StartProcess();
        }

        StopTasks().Wait();
        StartTasks(true);
    }

    private void LoadEngines()
    {
        var cfg = Utils.EngineConfigs;
        cfg.Engines.ForEach(opt =>
        {
            Engines.Add(new Engine(opt, DataChannel));
        });
        SyncByDepth = cfg.SyncByDepth;
    }

    public void ProcessInput(string command)
    {
        if (Insights.IsBreakdownCommand(command))
        {
            Insights.BreakdownOf(InfoOutputData, command);
        }
        else
        {
            SendToAll(command);
        }
    }

    private void SendToAll(string command)
    {
        StopTasks().Wait();

        // Only "go" cmds are depth-sync'd, and only if SyncByDepth == true
        bool immediate = !(command.ToLower().StartsWith("go") && SyncByDepth);
        StartTasks(immediate);

        bool isStop = command.ToLower().StartsWith("stop");
        ResetOutputData(isStop);

        foreach (var eng in Engines)
        {
            eng.SendCommand(command);
        }
    }

    private void ResetOutputData(bool isStop)
    {
        ImmediateOutputData.Clear();
        foreach (var eng in Engines.Select(x => x.Name))
        {
            if (!ImmediateOutputData.TryAdd(eng, []))
                ImmediateOutputData[eng].Clear();
        }

        if (isStop)
            return;

        DataCursors.Clear();
        InfoOutputData.Clear();
        ReachedDepths.Clear();

        foreach (var eng in Engines.Select(x => x.Name))
        {
            if (!DataCursors.TryAdd(eng, 0))
                DataCursors[eng] = 0;

            if (!InfoOutputData.TryAdd(eng, []))
                InfoOutputData[eng].Clear();

            if (!ReachedDepths.TryAdd(eng, 0))
                ReachedDepths[eng] = 0;
        }
    }

    private void StartTasks(bool immediateWrite = true)
    {
        ReaderTokenSource.Dispose();
        ReaderTokenSource = new();

        WriterTokenSource.Dispose();
        WriterTokenSource = new();

        ReaderTask = Task.Run(() => ReaderTaskProc(ReaderTokenSource.Token));

        Func<CancellationToken, Task> writeProc = immediateWrite ? ImmediateOutputTaskProc : DepthSynchronizedOutputTaskProc;
        WriterTask = Task.Run(() => writeProc(WriterTokenSource.Token), WriterTokenSource.Token);
    }

    private async Task StopTasks()
    {
        if (ReaderTask != null && !ReaderTask.IsCompleted)
        {
            try
            {
                await ReaderTokenSource.CancelAsync();
                await ReaderTask;
            }
            catch (OperationCanceledException) { }
        }

        if (WriterTask != null && !WriterTask.IsCompleted)
        {
            try
            {
                await WriterTokenSource.CancelAsync();
                await WriterTask;
            }
            catch (OperationCanceledException) { }
        }
    }

    private async Task ReaderTaskProc(CancellationToken token)
    {
        await foreach (var (engine, line) in DataChannel.Reader.ReadAllAsync(token))
        {
            UciOutput uc = new(line);
            ImmediateOutputData[engine].Add(uc);

            if (uc.IsInfo)
                InfoOutputData[engine].Add(uc);

            if (uc.ShouldIncDepth && uc.Depth > ReachedDepths[engine])
                ReachedDepths[engine] = Math.Max(ReachedDepths[engine], uc.Depth);

            DataReadSignal.Release();
        }
    }

    private async Task DepthSynchronizedOutputTaskProc(CancellationToken token)
    {
        int printedDepth = 0;
        while (!token.IsCancellationRequested)
        {
            await DataReadSignal.WaitAsync(token);

            if (Engines.Any(x => ReachedDepths[x.Name] <= printedDepth))
                continue;

            printedDepth++;
            foreach (var eng in Engines.Select(x => x.Name))
            {
                if (!InfoOutputData.TryGetValue(eng, out var dataList))
                    continue;

                var outForDepth = dataList.Last(u => u.Depth == printedDepth);
                if (outForDepth.IsInfo && outForDepth.ShouldPrint)
                {
                    Log($"{FormatEngineName(eng)} >> {outForDepth}");
                }
            }
            Log();
        }
    }

    private async Task ImmediateOutputTaskProc(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            foreach (var eng in Engines.Select(x => x.Name))
            {
                if (!ImmediateOutputData.TryGetValue(eng, out var dataList))
                    continue;

                if (!DataCursors.TryGetValue(eng, out int cursor))
                    continue;

                while (cursor < dataList.Count)
                {
                    var uc = dataList[cursor];
                    if ((uc.IsPrintable && uc.ShouldPrint) || ALWAYS_PRINT)
                        Log($"{FormatEngineName(eng)} >> {uc}");

                    cursor++;
                }
                DataCursors[eng] = cursor;
            }

            await Task.Yield();
        }
    }

}

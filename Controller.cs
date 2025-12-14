using Consortium.UCI;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Channels;
using System.Xml.Linq;

namespace Consortium;

public class Controller
{
    private const int ThreadDelay = 10;

    private List<Engine> Engines = [];
    private ConcurrentDictionary<string, List<UciOutput>> ImmediateOutputData = [];
    private ConcurrentDictionary<string, List<UciOutput>> InfoOutputData = [];
    private ConcurrentDictionary<string, int> ReachedDepths = [];

    private readonly Channel<(string Eng, string Line)> DataChannel = Channel.CreateUnbounded<(string, string)>();

    private Task? ReaderTask;
    private Task? WriterTask;

    private CancellationTokenSource ReaderTokenSource = new();
    private CancellationTokenSource WriterTokenSource = new();

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
        var cfg = Utils.ReadConfig();
        cfg.ForEach(opt =>
        {
            Engines.Add(new Engine(opt, DataChannel));
        });
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

        bool immediate = !command.ToLower().StartsWith("go");
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

        InfoOutputData.Clear();
        ReachedDepths.Clear();

        foreach (var eng in Engines.Select(x => x.Name))
        {
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
            {
                ReachedDepths[engine] = Math.Max(ReachedDepths[engine], uc.Depth);
            }
        }
    }

    private async Task DepthSynchronizedOutputTaskProc(CancellationToken token)
    {
        int printedDepth = 0;
        while (!token.IsCancellationRequested)
        {
            await Task.Delay(ThreadDelay, token);

            if (Engines.Any(x => ReachedDepths[x.Name] <= printedDepth))
                continue;

            printedDepth++;
            foreach (var eng in Engines.Select(x => x.Name))
            {
                var outForDepth = InfoOutputData[eng].Last(u => u.Depth == printedDepth);
                if (outForDepth.IsInfo && outForDepth.ShouldPrint)
                {
                    Log($"{eng,8} >> {outForDepth}");
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
                if (!ImmediateOutputData.ContainsKey(eng))
                    continue;

                var dataList = ImmediateOutputData[eng];
                int i = dataList.FindIndex(u => u.ShouldPrint);
                if (i == -1)
                    continue;
                
                var uc = dataList[i];
                if (uc.IsInfo)
                {
                    Log($"{eng,8} >> {uc}");
                }
                else if (!UciOutput.IsBlacklisted(uc.Line))
                {
                    Log($"{eng,8} >> {uc.Line}");
                }

                dataList.RemoveAt(i);
            }

            await Task.Delay(ThreadDelay, token);
        }
    }
}

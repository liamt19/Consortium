using Consortium.UCI;
using System.Diagnostics;

namespace Consortium;

public class Controller
{
    private const int ThreadDelay = 10;

    public List<Engine> Engines { get; set; }

    public Dictionary<string, List<UciOutput>> OutputData;
    public Dictionary<string, int> ReachedDepths;

    private Task? ReaderTask;
    private Task? WriterTask;

    private CancellationTokenSource ReaderTokenSource;
    private CancellationTokenSource WriterTokenSource;

    //private CancellationToken ReaderCancellation;
    //private CancellationToken WriterCancellation;


    public Controller()
    {
        Engines = [];
        OutputData = [];
        ReachedDepths = [];

        ReaderTokenSource = new CancellationTokenSource();
        WriterTokenSource = new CancellationTokenSource();

        LoadEngines();

        ResetOutputData();

        //ReaderCancellation = new();
        //WriterCancellation = new();

        //ReaderTask = Task.Run(ReaderTaskProc, ReaderCancellation);
        //WriterTask = Task.Run(ImmediateOutputTaskProc, WriterCancellation);
        //WriterTask = Task.Run(DepthSynchronizedOutputTaskProc, WriterCancellation);
    }

    public void LoadEngines()
    {
        Utils.ReadConfig().ForEach(opt =>
        {
            Engines.Add(new Engine(opt));
        });
    }

    private void StartTasks(bool immediateWrite = true)
    {
        ReaderTask = Task.Run(() => ReaderTaskProc(ReaderTokenSource.Token));

        if (immediateWrite)
        {
            WriterTask = Task.Run(() => ImmediateOutputTaskProc(WriterTokenSource.Token));
        }
        else
        {
            WriterTask = Task.Run(() => DepthSynchronizedOutputTaskProc(WriterTokenSource.Token));
        }
    }

    private void StopTasks()
    {
        if (ReaderTask != null && !ReaderTask.IsCompleted)
        {
            ReaderTokenSource.Cancel();
            ReaderTask.Wait();
        }

        if (WriterTask != null && !WriterTask.IsCompleted)
        {
            WriterTokenSource.Cancel();
            WriterTask.Wait();
        }
    }

    public void SendToAll(string command)
    {
        StopTasks();

        bool immediate = !command.ToLower().StartsWith("go ");
        StartTasks(immediate);

        ResetOutputData();

        foreach (var eng in Engines)
        {
            eng.SendCommand(command);
        }
    }

    private void ResetOutputData()
    {
        OutputData.Clear();
        ReachedDepths.Clear();

        foreach (var eng in Engines)
        {
            if (OutputData.TryGetValue(eng.Name, out List<UciOutput>? v))
            {
                v.Clear();
                ReachedDepths[eng.Name] = 0;
            }
            else
            {
                OutputData.Add(eng.Name, []);
                ReachedDepths.Add(eng.Name, 0);
            }
        }
    }

    private async Task ReaderTaskProc(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            foreach (var eng in Engines)
            {
                var name = eng.Name;

                while (eng.OutputQueue.TryDequeue(out var outStr))
                {
                    UciOutput uc = new(outStr);
                    OutputData[name].Add(uc);

                    if (uc.ShouldPrint) 
                        Debug.WriteLine($"{name} >> {uc}");

                    if (uc.ShouldIncDepth && uc.Depth > ReachedDepths[name])
                    {
                        ReachedDepths[name] = Math.Max(ReachedDepths[name], uc.Depth);
                        Debug.WriteLine(string.Join(", ", Engines.Select(x => x.Name).Select(x => $"{x}={ReachedDepths[x]}")));
                    }
                }
            }

            await Task.Delay(ThreadDelay, token);
        }
    }

    private async Task DepthSynchronizedOutputTaskProc(CancellationToken token)
    {
        int printedDepth = 0;

        while (!token.IsCancellationRequested)
        {
            if (Engines.Any(x => ReachedDepths[x.Name] <= printedDepth))
                continue;

            printedDepth++;
            foreach (var eng in Engines.Select(x => x.Name))
            {
                var outForDepth = OutputData[eng].Last(u => u.IsInfo && u.Depth == printedDepth);
                if (outForDepth.ShouldPrint)
                {
                    Console.WriteLine($"{eng} >> {outForDepth}");
                }
            }

            Console.WriteLine();
            await Task.Delay(ThreadDelay, token);
        }
    }

    private async Task ImmediateOutputTaskProc(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            foreach (var eng in Engines.Select(x => x.Name))
            {
                int i = OutputData[eng].FindIndex(u => u.ShouldPrint);
                if (i == -1)
                    continue;

                var uc = OutputData[eng][i];
                Console.WriteLine($"{eng} >> {uc}");
                OutputData[eng].RemoveAt(i);
            }

            await Task.Delay(ThreadDelay, token);
        }
    }
}

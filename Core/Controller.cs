using Consortium.Misc;
using Consortium.UCI;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Channels;

namespace Consortium.Core;

public class Controller
{
    private bool SyncByDepth = true;
    private bool PrintAllOutput = false;
    private bool PrintRawUCI = false;

    private readonly List<Engine> _engines = [];
    private readonly ConcurrentDictionary<string, List<UciOutput>> _infoOutputData = [];
    private readonly ConcurrentDictionary<string, int> _reachedDepths = [];
    private readonly List<string> _rootPVGroups = [];

    private readonly Channel<(string Eng, UciOutput Line)> _dataChannel;

    private Task? _ioHandlerTask;
    private CancellationTokenSource _ioHandlerTokenSource = new();
    private OutputMode _outputMode;

    public Controller()
    {
        //AllowSynchronousContinuations perhaps?
        _dataChannel = Channel.CreateUnbounded<(string, UciOutput)>(new() { SingleReader = true });

        LoadEngines();
        ResetOutputData(false);

        StopTasks().Wait();
        StartTasks(true);

        StartAllEngines();
    }

    private void StartAllEngines()
    {
        Parallel.ForEach(_engines, eng =>
        {
            eng.StartProcess();
        });

        // separate these for formatting's sake
        Parallel.ForEach(_engines, eng =>
        {
            eng.SendUCIOpts();
        });
    }

    private void LoadEngines()
    {
        var cfg = Utils.EngineConfigs;
        cfg.Engines.ForEach(opt =>
        {
            _engines.Add(new Engine(opt, _dataChannel));
        });

        SyncByDepth = cfg.SyncByDepth;
        PrintAllOutput = cfg.PrintAllOutput;
        PrintRawUCI = cfg.PrintRawUCI;
    }

    public void TerminateProcesses()
    {
        RemoveKilledProcesses();

        SendToAll("stop");
        Task.WhenAll(_engines.Select(e => e.SendAndWait("quit", 250))).GetAwaiter().GetResult();
        Parallel.ForEach(_engines, eng => eng.Terminate());

        Thread.Sleep(100);
        StopTasks().Wait();
        try
        {
            _dataChannel.Writer.Complete();
            BatchedConsoleWriter.Complete();
        }
        catch (Exception) { }

    }

    public void ProcessInput(string command)
    {
        if (string.IsNullOrEmpty(command))
        {
            Log();
            return;
        }

        if (Insights.IsBreakdownCommand(command))
        {
            Insights.BreakdownOf(_infoOutputData, command);
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
        bool immediate = !(command.StartsWithIgnoreCase("go") && SyncByDepth);
        StartTasks(immediate);

        bool isStop = command.StartsWithIgnoreCase("stop");
        ResetOutputData(isStop);

        RemoveKilledProcesses();
        Parallel.ForEach(_engines, eng => eng.SendCommand(command));
        Log();
    }

    private void RemoveKilledProcesses()
    {
        _engines.RemoveAll(eng => eng.Proc is null || eng.Proc.HasExited);
    }

    private void ResetOutputData(bool isStop)
    {
        if (isStop)
            return;

        _infoOutputData.Clear();
        _reachedDepths.Clear();
        _rootPVGroups.Clear();

        foreach (var eng in _engines.Select(x => x.Name))
        {
            if (!_infoOutputData.TryAdd(eng, []))
                _infoOutputData[eng].Clear();

            if (!_reachedDepths.TryAdd(eng, 0))
                _reachedDepths[eng] = 0;
        }
    }

    private void StartTasks(bool immediateWrite)
    {
        _outputMode = immediateWrite ? OutputMode.Immediate : OutputMode.DepthSynchronized;

        _ioHandlerTokenSource.Dispose();
        _ioHandlerTokenSource = new();

        _ioHandlerTask = Task.Run(() => IOHandlerTaskProc(_ioHandlerTokenSource.Token));
    }

    private async Task StopTasks()
    {
        if (_ioHandlerTask?.IsCompleted == false)
        {
            try
            {
                await _ioHandlerTokenSource.CancelAsync();
                await _ioHandlerTask;
            }
            catch (OperationCanceledException) { }
        }

        _ioHandlerTask = null;
    }

    private async Task IOHandlerTaskProc(CancellationToken token)
    {
        int printedDepth = 0;
        var engineNames = _engines.Select(e => e.Name).ToList();
        var channelStream = _dataChannel.Reader.ReadAllAsync(token);
        await foreach (var (engine, uc) in channelStream)
        {
            // Immediate output
            if (_outputMode == OutputMode.Immediate)
            {
                if (PrintAllOutput || (uc.IsPrintable && uc.ShouldPrint))
                {
                    Log($"{FormatEngineName(engine)} >> {uc.ToString(PrintRawUCI)}");
                }

                continue;
            }

            // Depth-sync'd
            if (uc.IsInfo)
            {
                _infoOutputData[engine].Add(uc);

                if (uc.ShouldIncDepth)
                    _reachedDepths[engine] = Math.Max(_reachedDepths[engine], uc.Depth);

                if (engineNames.All(eng => _reachedDepths[eng] > printedDepth))
                {
                    printedDepth++;

                    var lastInfos = _infoOutputData.Select(x => (x.Key, x.Value.Last(u => u.Depth == printedDepth))).ToList();
                    var pvToGroup = GroupPVs(_rootPVGroups, lastInfos);

                    foreach (var eng in engineNames)
                    {
                        (int thisGroup, int ansiLen) = pvToGroup[eng];
                        var outForDepth = _infoOutputData[eng].Last(u => u.Depth == printedDepth);
                        Log($"{FormatEngineName(eng)} >> {outForDepth.FormatAnsi(thisGroup, ansiLen, PrintRawUCI)}");
                    }
                    Log();
                }
            }
        }
    }

}

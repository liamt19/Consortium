using Consortium.Core;
using Consortium.Core.Misc;

namespace Consortium.General;

public class GeneralController : Controller
{
    private readonly bool SyncByDepth;
    private readonly bool PrintAllOutput;
    private readonly bool PrintRawUCI;

    private readonly List<string> _rootPVGroups = [];

    private OutputMode _outputMode;

    public GeneralController()
    {
        SyncByDepth = Utils.EngineConfigs.SyncByDepth;
        PrintAllOutput = Utils.EngineConfigs.PrintAllOutput;
        PrintRawUCI = Utils.EngineConfigs.PrintRawUCI;
    }

    protected override void AfterEnginesStarted()
    {
        Parallel.ForEach(_engines, eng =>
        {
            eng.SendUCIOpts();
        });
    }

    public override void ProcessInput(string command)
    {
        if (string.IsNullOrEmpty(command))
        {
            Log();
            return;
        }

        if (Insights.IsBreakdownCommand(command))
        {
            Insights.BreakdownOf(_infoOutputData, command);
            return;
        }

        SendToAll(command);
    }


    protected override void ResetOutputData(string? command = null)
    {
        if (command.StartsWithIgnoreCase("stop"))
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

    protected override void StartTasks(string? command = null)
    {
        // Only "go" cmds are depth-sync'd, and only if SyncByDepth == true
        bool depthSynced = command is not null && command.StartsWithIgnoreCase("go") && SyncByDepth;

        _outputMode = depthSynced ? OutputMode.DepthSynchronized : OutputMode.Immediate;

        _ioHandlerTokenSource.Dispose();
        _ioHandlerTokenSource = new();

        _ioHandlerTask = Task.Run(() => IOHandlerTaskProc(_ioHandlerTokenSource.Token));
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

using Consortium.Core;
using Consortium.Core.Misc;
using Consortium.Core.UCI;
using System.Collections;
using System.Text;

namespace Consortium.General;

public class GeneralController : Controller
{
    private readonly bool SyncByDepth;
    private readonly bool OrderByTime;
    private readonly bool PrintAllOutput;
    private readonly bool PrintRawUCI;

    private readonly List<string> _rootPVGroups = [];
    private OutputMode _outputMode;

    public GeneralController()
    {
        SyncByDepth = Utils.EngineConfigs.SyncByDepth;
        OrderByTime = Utils.EngineConfigs.OrderByTime;
        PrintAllOutput = Utils.EngineConfigs.PrintAllOutput;
        PrintRawUCI = Utils.EngineConfigs.PrintRawUCI;

        var mpv = _engineToIndex.Select(name => $"{name.Key}={name.Value}");
        Log($"info string eng idx's -> {string.Join(", ", mpv)}");

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
            Insights.BreakdownOf(_depthBuckets, command);
            return;
        }

        if (FenRegex.IsMatch(command) && !command.StartsWithIgnoreCase("position fen")) {
            command = "position fen " + command.TrimStart();
        }

        SendToAll(command);
    }


    protected override void ResetOutputData(string? command = null)
    {
        if (command.StartsWithIgnoreCase("stop"))
            return;

        _rootPVGroups.Clear();

        if (_depthReached != null)
        {
            foreach (var bitarr in _depthReached)
            {
                bitarr.SetAll(false);
            }
        }

        if (_depthBuckets != null)
        {
            foreach (var bucket in _depthBuckets)
                bucket.Clear();
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

            int d = uc.Depth;
            if (!uc.ShouldIncDepth || d > MaxDepth)
                continue;

            int engIdx = _engineToIndex[engine];
            LogVerbose($"{FormatEngineName(engine)} >> {uc.ToString(true)} --> {engIdx,-3} = {_depthReached[d].Stringify(),-20}");

            if (!_depthReached[d][engIdx])
            {
                _depthReached[d][engIdx] = true;
                _depthBuckets[d].Add((engine, uc));

                // Barrier reached?
                if (_depthReached[d].HasAllSet())
                {
                    LogVerbose($"info string depth {d} barrier reached");
                    PrintOutputsForDepth(d);
                }
            }

        }
    }

    private void PrintOutputsForDepth(int printedDepth)
    {
        var infos = _depthBuckets[printedDepth];
        var pvToGroup = GroupPVs(infos);
        
        if (!OrderByTime)
        {
            infos = [.. infos.OrderBy(x => _engineToIndex[x.Item1])];
        }

        foreach ((string eng, UciOutput uc) in infos)
        {
            (int thisGroup, int ansiLen) = pvToGroup[eng];
            Log($"{FormatEngineName(eng)} >> {uc.FormatAnsi(thisGroup, ansiLen, PrintRawUCI)}");
        }
        Log();
    }

    public Dictionary<string, (int groupNum, int ansiLen)> GroupPVs(List<(string name, UciOutput uc)> outputs)
    {
        var dict = new Dictionary<string, (int groupNum, int ansiLen)>();

        var pvGroups = outputs.GroupBy(x => x.uc.PVFirst);
        foreach (var pv in pvGroups.Select(g => g.Key))
        {
            LogVerbose($"{pv} is group {_rootPVGroups.Count}");
            _rootPVGroups.AddIfMissing(pv);
        }
        
        var groups = pvGroups
            .Select(x => x.ToList())
            .OrderByDescending(g => g.Count)
            .ToList();

        foreach (var group in groups)
        {
            var tokens = group
                .Select(m => (m.name, pv: m.uc.PV.Split(' ')))
                .ToList();

            foreach (var (name, pv) in tokens)
            {
                int bestOverlap = 1;
                foreach (var (otherName, otherPv) in tokens)
                {
                    if (name == otherName) continue;
                    bestOverlap = Math.Max(bestOverlap, PrefixOverlap(pv, otherPv));
                }

                int gNum = _rootPVGroups.IndexOf(pv[0]);
                dict.Add(name, (gNum, bestOverlap));
            }
        }

        return dict;
    }

}

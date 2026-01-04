#define TEMP

using Consortium.Core;
using Consortium.Core.Misc;
using Consortium.Core.UCI;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Xml.Linq;

namespace Consortium.Democracy;

public class DemocracyController : Controller
{
    private readonly Stopwatch _goTimer = Stopwatch.StartNew();

    private readonly ConcurrentDictionary<string, string> _bestmoves = [];
    private readonly DemocracySelectionStrategy _selectionStrategy = DemocracySelectionStrategy.PreferPrevious;
    private string _previouslyUsedEngine;
    private HashSet<string> _previouslySelectedKeys = [];
    private IOBarrier _ioBarrier;

    private Engine? Leader => _engines.FirstOrDefault();

    public DemocracyController()
    {
        _ioBarrier = new IOBarrier(Utils.EngineConfigs.Engines.Count);
    }

    public override void ProcessInput(string command)
    {
        if (string.IsNullOrEmpty(command))
        {
            Log();
            return;
        }

        if (command.EqualsIgnoreCase("uci"))
        {
            var mpv = _engines.Select(e => e.Name).Select((name, idx) => $"{name}={idx + 1}");
            Log($"info string hashfull keys -> {string.Join(", ", mpv)}");
            Log($"info string strategy is {_selectionStrategy}");
        }

        SendToAll(command);
    }


    protected override void ResetOutputData(string? command = null)
    {
        _infoOutputData.Clear();
        _reachedDepths.Clear();
        _bestmoves.Clear();

        foreach (var eng in _engines.Select(x => x.Name))
        {
            if (!_infoOutputData.TryAdd(eng, []))
                _infoOutputData[eng].Clear();

            if (!_bestmoves.TryAdd(eng, "0000"))
                _bestmoves[eng] = "0000";

            if (!_reachedDepths.TryAdd(eng, -1))
                _reachedDepths[eng] = -1;
        }
    }

    protected override void DoSendCommand(string command, bool printSend = true)
    {
        if (command.StartsWithIgnoreCase("go"))
            _goTimer.Restart();

        if (command == "isready")
        {
            _ioBarrier.AddBarrier("isready", "readyok", () =>
            {
                Log($"readyok");
            });
        }

        Parallel.ForEach(_engines, eng =>
        {
            LogVerbose($"info string {eng} ---> sending {command}");
            eng.SendCommand(command, false);
            LogVerbose($"info string {eng} ------> sent {command}");
        });
    }

    protected override void StartTasks(string? command = null)
    {
        _ioHandlerTokenSource.Dispose();
        _ioHandlerTokenSource = new();

        _ioHandlerTask = Task.Run(() => IOHandlerTaskProc(_ioHandlerTokenSource.Token));
    }

    private async Task IOHandlerTaskProc(CancellationToken token)
    {
        int printedDepth = 0;
        var leaderName = Leader!.Name;
        var engineNames = _engines.Select(e => e.Name).ToList();
        var channelStream = _dataChannel.Reader.ReadAllAsync(token);
        await foreach (var (engine, uc) in channelStream)
        {
            _infoOutputData[engine].Add(uc);
            bool wasExpected = IOBarrier.IsBarrierable(uc.Line) && _ioBarrier.Arrive(engine, uc.Line);

            if (uc.ShouldIncDepth)
            {
                _reachedDepths[engine] = Math.Max(_reachedDepths[engine], uc.Depth);

                // Send a "info depth x score ..." once all engines have completed depth x
                if (engineNames.All(eng => _reachedDepths[eng] > printedDepth))
                {
                    printedDepth++;
                    Log(GetOutputForDepth(engineNames, printedDepth));
                }
            }

            if (uc.IsBestmove)
            {
#if TEMP
                LogVerbose($"info string {engine} ---> bm {uc.Bestmove}");
#endif
                _bestmoves[engine] = uc.Bestmove;
                if (engineNames.All(eng => _bestmoves[eng] != "0000"))
                {
                    string majorityChoice = GetBestmoveToSend();

                    Log($"bestmove {majorityChoice}");
                }
                continue;
            }
            
            // Print all non-uci related stuff from the leader
            if (!uc.IsInfo && engine == leaderName && !wasExpected)
            {
                Log(uc.Line);
            }

#if TEMP
            if (!uc.IsInfo && engine != leaderName && !wasExpected)
            {
                LogVerbose($"info string {engine} ---> {uc.Line}");
            }
#endif

#if NO
            // Print info string of depths of all engines
            if (uc.IsInfo && engine == leaderName)
            {
                PrintEngineStatus();
            }
#endif
        }
    }

    private string GetOutputForDepth(List<string> engNames, int printedDepth)
    {
        var lastInfos = _infoOutputData
            .Where(x => x.Value.Any(u => u.Depth == printedDepth))
            .Select(x => (x.Key, uc: x.Value.Last(u => u.Depth == printedDepth)))
            .ToList();

        // Most agreed-upon PV
        var bestGroup = lastInfos
            .GroupBy(x => x.uc.PV.Split(' ')[0])
            .Select(x => x.ToList())
            .MaxBy(g => g.Count);

        var groupToUse = _selectionStrategy switch
        {
            DemocracySelectionStrategy.Random => Random.Shared.Next(0, bestGroup.Count),
            DemocracySelectionStrategy.PreferLeader => Math.Max(0, bestGroup.FindIndex(x => x.Key == Leader.Name)),
            DemocracySelectionStrategy.PreferPrevious => Math.Max(0, bestGroup.FindIndex(x => x.Key == _previouslyUsedEngine)),
        };

        (var eng, var bestOutput) = bestGroup[groupToUse];
        _previouslyUsedEngine = eng;

        var engUsed = engNames.IndexOf(eng) + 1;
        var agreed = bestGroup.Count;

        var totalNodes = lastInfos.Select(x => (long)x.uc.Nodes).Sum();
        var time = (long)Math.Max(1.0, _goTimer.Elapsed.TotalMilliseconds);
        var nps = (long)(totalNodes / (time / 1000.0));

        var sb = new StringBuilder(256);
        sb.Append("info")
          .Append(" depth ").Append(bestOutput.Depth)
          .Append(" seldepth ").Append(bestOutput.SelDepth)
          .Append(" score ").Append(bestOutput.RawScore)
          .Append(" nodes ").Append(totalNodes)
          .Append(" time ").Append(time)
          .Append(" nps ").Append(nps)
          .Append(" hashfull ").Append(engUsed)
          .Append(" tbhits ").Append(agreed)
          .Append(" pv ").Append(bestOutput.PV);

        return sb.ToString();
    }

    private string GetBestmoveToSend()
    {
        var groups = _bestmoves
            .GroupBy(kvp => kvp.Value)
            .Select(g => new
            {
                Value = g.Key,
                Count = g.Count(),
                Keys = g.Select(kvp => kvp.Key).ToHashSet()
            })
            .ToList();

        int majorityGroup = groups.Max(g => g.Count);
        var tiedGroups = groups.Where(g => g.Count == majorityGroup).ToList();

        if (tiedGroups.Count == 1)
        {
            _previouslySelectedKeys = tiedGroups[0].Keys;
            return tiedGroups[0].Value;
        }

        if (_selectionStrategy == DemocracySelectionStrategy.PreferLeader)
        {
            var leaderSet = tiedGroups.FirstOrDefault(g => g.Keys.Contains(Leader.Name));
            if (leaderSet != null)
            {
                _previouslySelectedKeys = leaderSet.Keys;
                return leaderSet.Value;
            }
        }

        if (_selectionStrategy == DemocracySelectionStrategy.PreferPrevious)
        {
            var prevSet = tiedGroups.OrderByDescending(g => g.Keys.Intersect(_previouslySelectedKeys).Count()).First();
            _previouslySelectedKeys = prevSet.Keys;
            return prevSet.Value;
        }

        var randomGroup = groups[Random.Shared.Next(0, groups.Count)];

        _previouslySelectedKeys = randomGroup.Keys;
        return randomGroup.Value;
    }

    private void PrintEngineStatus()
    {
        var lastInfos = _reachedDepths.Select(x => $"{x.Key}={x.Value}");

        Log($"info string {string.Join(", ", lastInfos)}");
    }
}

#define TEMP

using Consortium.Core;
using Consortium.Core.Misc;
using Consortium.Core.UCI;
using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Threading.Channels;
using System.Xml.Linq;

namespace Consortium.Democracy;

public class DemocracyController : Controller
{
    private readonly Stopwatch _goTimer = Stopwatch.StartNew();

    private const DemocracySelectionStrategy _selectionStrategy = DemocracySelectionStrategy.PreferPrevious;
    private HashSet<string> _previouslySelectedKeys = [];
    private readonly IOBarrier _ioBarrier;

    private readonly Dictionary<string, int> _engineToIndex = [];

    private readonly List<UciOutput>[] _depthBuckets;
    private readonly BitArray[] _depthReached;

    private Engine? Leader => _engines.FirstOrDefault();

    public DemocracyController()
    {
        _ioBarrier = new IOBarrier(_engineCount);

        for (int i = 0; i < _engineCount; i++)
            _engineToIndex.Add(EngineConfigs.Engines[i].Name, i);

        _depthBuckets = [.. Enumerable.Range(0, MaxDepth + 1).Select(_ => new List<UciOutput>(_engineCount))];
        _depthReached = [.. Enumerable.Range(0, MaxDepth + 1).Select(_ => new BitArray(_engineCount))];
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
#if NO
        _infoOutputData.Clear();
        _reachedDepths.Clear();

        foreach (var eng in _engines.Select(x => x.Name))
        {
            if (!_infoOutputData.TryAdd(eng, []))
                _infoOutputData[eng].Clear();

            if (!_reachedDepths.TryAdd(eng, -1))
                _reachedDepths[eng] = -1;
        }
#endif
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
        if (Leader is null) return;

        int printedDepth = 0;
        var leaderName = Leader!.Name;
        Dictionary<string, string> bestmoves = [];

        var channelStream = _dataChannel.Reader.ReadAllAsync(token);
        await foreach (var (engine, uc) in channelStream)
        {
            bool wasExpected = IOBarrier.IsBarrierable(uc.Line) && _ioBarrier.Arrive(engine, uc.Line);

            if (uc.ShouldIncDepth)
            {
                int d = uc.Depth;
                if (uc.Depth <= MaxDepth)
                {
                    int engIdx = _engineToIndex[engine];
                    //_depthBuckets[d].Add(uc);
                    _depthBuckets[d][engIdx] = uc;
                    LogVerbose($"info string {engine}@{d} --> {engIdx} = {_depthReached[d].Stringify()}");
                    if (!_depthReached[d][engIdx])
                    {
                        _depthReached[d][engIdx] = true;

                        // Barrier reached?
                        if (_depthReached[d].HasAllSet())
                        {
                            printedDepth = d;
                            Log(GetOutputForDepth(printedDepth));
                        }
                    }
                }
            }

            if (uc.IsBestmove)
            {
#if TEMP
                LogVerbose($"info string {engine} ---> bm {uc.Bestmove}");
#endif
                bestmoves[engine] = uc.Bestmove;
                if (bestmoves.Count == _engineCount)
                {
                    Log($"bestmove {GetBestmoveToSend(bestmoves)}");
                }

                continue;
            }
            
            // Print all non-uci related stuff from the leader
            if (!uc.IsInfo && !wasExpected && engine == leaderName)
            {
                Log(uc.Line);
            }

#if TEMP
            if (!uc.IsInfo && !wasExpected && engine != leaderName)
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

#if MAYBE
    private async Task DepthAggregator(CancellationToken token)
    {
        int printedDepth = 0;
        Span<int> depthCounts = new int[_engineCount];
        var engineNames = _engines.Select(e => e.Name).ToList();

        await foreach (var (engine, uc) in _infoChannel.Reader.ReadAllAsync(token))
        {
            if (!uc.ShouldIncDepth) continue;

            if (uc.Depth > depthCounts[_engineToIndex[engine]])
            {
                depthCounts[_engineToIndex[engine]] = uc.Depth;

                // Send a "info depth x score ..." once all engines have completed depth x
                if (engineNames.All(eng => _reachedDepths[eng] > printedDepth))
                {
                    printedDepth++;
                    Log(GetOutputForDepth(engineNames, printedDepth));
                    depthCounts.Clear();
                }
            }
        }
    }

    private async Task MiscAggregator(CancellationToken token)
    {
        var leaderName = Leader!.Name;
        var engineNames = _engines.Select(e => e.Name).ToList();
        await foreach (var (engine, uc) in _infoChannel.Reader.ReadAllAsync(token))
        {
            if (uc.IsInfo)
                continue;

            bool wasExpected = IOBarrier.IsBarrierable(uc.Line) && _ioBarrier.Arrive(engine, uc.Line);

            // Print all non-uci related stuff from the leader
            if (engine == leaderName && !wasExpected)
            {
                Log(uc.Line);
            }

#if TEMP
            if (engine != leaderName && !wasExpected)
            {
                LogVerbose($"info string {engine} ---> {uc.Line}");
            }
#endif
        }
    }

    private async Task BestmoveAggregator(CancellationToken token)
    {
        Dictionary<string, string> bestmoves = [];
        await foreach (var (engine, uc) in _infoChannel.Reader.ReadAllAsync(token))
        {
            if (!uc.IsBestmove)
                continue;

#if TEMP
            LogVerbose($"info string {engine} ---> bm {uc.Bestmove}");
#endif
            bestmoves.TryAdd(engine, uc.Bestmove);
            if (bestmoves.Count == _engineCount)
            {
                Log($"bestmove {GetBestmoveToSend(bestmoves)}");
            }
        }
    }
#endif

    private string GetOutputForDepth(int printedDepth)
    {
        var infos = _depthBuckets[printedDepth];
        var bestGrouping = infos
            .GroupBy(x => x.PVFirst)
            .MaxBy(g => g.Count());

        // Most agreed-upon PV
        var bestOutput = bestGrouping.First();

        //(var eng, var bestOutput) = bestGroup[groupToUse];
        //_previouslyUsedEngine = eng;


        //var engUsed = engNames.IndexOf(eng) + 1;
        var engUsed = bestOutput.Hashfull;
        var agreed = bestGrouping.Count();

        ulong totalNodes = 0;
        foreach (var info in infos)
            totalNodes += info.Nodes;

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

    private string GetBestmoveToSend(Dictionary<string, string> bestmoves)
    {
        var groups = bestmoves
            .GroupBy(kvp => kvp.Value)
            .Select(g => new
            {
                Value = g.Key,
                Count = g.Count(),
                Keys = g.Select(kvp => kvp.Key).ToHashSet()
            })
            .ToList();

        var groupStrs = "{" + string.Join(", ", groups.Select(g => g.Value + ": " + g.Keys.Stringify())) + "}";
        Log($"info string groups are {groupStrs}");

        int majorityGroup = groups.Max(g => g.Count);
        var tiedGroups = groups.Where(g => g.Count == majorityGroup).ToList();

        if (tiedGroups.Count == 1)
        {
            _previouslySelectedKeys = tiedGroups[0].Keys;
            Log($"info string simple majority group is {_previouslySelectedKeys.Stringify()}");
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
            var newSet = tiedGroups.OrderByDescending(g => g.Keys.Intersect(_previouslySelectedKeys).Count()).First();
            Log($"info string prev group was {_previouslySelectedKeys.Stringify()}, now {newSet.Keys.Stringify()}");
            _previouslySelectedKeys = newSet.Keys;
            return newSet.Value;
        }

        var randomGroup = groups[Random.Shared.Next(0, groups.Count)];

        _previouslySelectedKeys = randomGroup.Keys;
        return randomGroup.Value;
    }

}

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

    private Engine? Leader => _engines.FirstOrDefault();

    public DemocracyController()
    {
        _ioBarrier = new IOBarrier(_engineCount);
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

        var leaderName = Leader!.Name;
        Dictionary<string, string> bestmoves = [];

        var channelStream = _dataChannel.Reader.ReadAllAsync(token);
        await foreach (var (engine, uc) in channelStream)
        {
            bool wasExpected = IOBarrier.IsBarrierable(uc.Line) && _ioBarrier.Arrive(engine, uc.Line);

            int d = uc.Depth;
            if (uc.ShouldIncDepth && d <= MaxDepth)
            {
                int engIdx = _engineToIndex[engine];
                _depthBuckets[d].Add((string.Empty, uc));
                LogVerbose($"info string {engine} --> {uc.Line} --> {engIdx,-3} = {_depthReached[d].Stringify(),-20}");
                if (!_depthReached[d][engIdx])
                {
                    _depthReached[d][engIdx] = true;

                    // Barrier reached?
                    if (_depthReached[d].HasAllSet())
                    {
                        Log(GetOutputForDepth(d));
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

        }
    }

    private string GetOutputForDepth(int printedDepth)
    {
        var infos = _depthBuckets[printedDepth].Select(x => x.Item2);
        var bestGrouping = infos
            .GroupBy(x => x.PVFirst)
            .MaxBy(g => g.Count());

        // Most agreed-upon PV
        var bestOutput = bestGrouping.First();

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

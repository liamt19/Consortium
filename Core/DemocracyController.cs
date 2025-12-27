using Consortium.Misc;
using Consortium.UCI;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Channels;
using System.Xml.Linq;

namespace Consortium.Core;

public class DemocracyController : Controller
{
    private readonly ConcurrentDictionary<string, string> _bestmoves = [];
    private Engine? Leader => _engines.FirstOrDefault();
    private readonly Stopwatch _goTimer = Stopwatch.StartNew();

    public DemocracyController()
    {

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
            Console.WriteLine($"info string multipv keys.values -> {string.Join(", ", mpv)}");
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

            if (!_reachedDepths.TryAdd(eng, 0))
                _reachedDepths[eng] = 0;
        }
    }

    protected override void DoSendCommand(string command, bool printSend = true)
    {
        if (command.StartsWithIgnoreCase("go"))
            _goTimer.Restart();

        //Parallel.ForEach(_engines, eng => eng.SendCommand(command, false));
        Parallel.ForEach(_engines, eng =>
        {
            eng.SendCommand(command, false);
            Console.WriteLine($"info string sent {command} -> {eng}");
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

            if (uc.ShouldIncDepth)
                _reachedDepths[engine] = Math.Max(_reachedDepths[engine], uc.Depth);

            // Send a "info depth x score ..." once all engines have completed depth x
            if (engineNames.All(eng => _reachedDepths[eng] > printedDepth))
            {
                printedDepth++;
                Console.WriteLine(GetOutputForDepth(engineNames, printedDepth));
            }

            if (uc.IsBestmove)
            {
                Console.WriteLine($"info string {engine} bm -> {uc.Bestmove}");
                _bestmoves[engine] = uc.Bestmove;
                if (engineNames.All(eng => _bestmoves[eng] != "0000"))
                {
                    string majorityChoice = _bestmoves.Values
                        .GroupBy(x => x)
                        .MaxBy(g => g.Count())
                        .Key;

                    Console.WriteLine($"bestmove {majorityChoice}");
                }
                continue;
            }
            else if (!uc.IsInfo && engine == leaderName)
            {
                Console.WriteLine(uc.Line);
            }
            else if (!uc.IsInfo)
            {
                Console.WriteLine($"info string got {uc.Line} from {engine}");
            }
            else if (engine == leaderName)
            {
                PrintEngineStatus();
            }
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

        var bestInfo = bestGroup[Random.Shared.Next(0, bestGroup.Count)];
        (var eng, var bestOutput) = bestInfo;

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
          .Append(" hashfull ").Append(bestOutput.Hashfull)
          .Append(" multipv ").Append(engUsed)
          .Append(" tbhits ").Append(agreed)
          .Append(" pv ").Append(bestOutput.PV);

        return sb.ToString();
    }

    private void PrintEngineStatus()
    {
        var lastInfos = _infoOutputData
            .Select(x =>
            {
                int d = -1;
                if (x.Value.Any(x => x.HasDepth))
                    d = x.Value.Last(x => x.HasDepth).Depth;
                return $"{x.Key}={d}";
            });

        Console.WriteLine($"info string {string.Join(", ", lastInfos)}");
    }
}

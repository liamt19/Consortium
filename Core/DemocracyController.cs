using Consortium.Misc;
using Consortium.UCI;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Channels;

namespace Consortium.Core;

public class DemocracyController : Controller
{
    private readonly ConcurrentDictionary<string, List<UciOutput>> _infoOutputData = [];
    private readonly ConcurrentDictionary<string, string> _bestmoves = [];
    private Engine? Leader => _engines.FirstOrDefault();

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

        SendToAll(command);
    }


    protected override void ResetOutputData(string? command = null)
    {
        if (command.StartsWithIgnoreCase("stop"))
            return;

        _infoOutputData.Clear();
        _bestmoves.Clear();

        foreach (var eng in _engines.Select(x => x.Name))
        {
            if (!_infoOutputData.TryAdd(eng, []))
                _infoOutputData[eng].Clear();

            if (!_bestmoves.TryAdd(eng, "0000"))
                _bestmoves[eng] = "0000";
        }
    }

    protected override void DoSendCommand(string command, bool printSend = true)
    {
        Parallel.ForEach(_engines, eng => eng.SendCommand(command, false));
    }

    protected override void StartTasks(string? command = null)
    {
        _ioHandlerTokenSource.Dispose();
        _ioHandlerTokenSource = new();

        _ioHandlerTask = Task.Run(() => IOHandlerTaskProc(_ioHandlerTokenSource.Token));
    }

    private async Task IOHandlerTaskProc(CancellationToken token)
    {
        var leaderName = Leader!.Name;
        var engineNames = _engines.Select(e => e.Name).ToList();
        var channelStream = _dataChannel.Reader.ReadAllAsync(token);
        await foreach (var (engine, uc) in channelStream)
        {
            if (uc.IsBestmove)
            {
                Debug.WriteLine($"bm from {engine} is {uc.Bestmove}");
                _bestmoves[engine] = uc.Bestmove;
                if (engineNames.All(eng => _bestmoves[eng] != "0000"))
                {
                    string majorityChoice = _bestmoves.Values
                        .GroupBy(x => x)
                        .MaxBy(g => g.Count())
                        .Key;

                    Console.WriteLine($"bestmove {majorityChoice}");
                }
            }
            else if (engine == leaderName)
            {
                Console.WriteLine(uc.Line);
            }
        }
    }

}

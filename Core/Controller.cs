using Consortium.Misc;
using Consortium.UCI;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Channels;

namespace Consortium.Core;

public abstract class Controller
{
    protected readonly List<Engine> _engines;
    protected readonly Channel<(string Eng, UciOutput Line)> _dataChannel;

    protected Task? _ioHandlerTask;
    protected CancellationTokenSource _ioHandlerTokenSource;

    protected Controller()
    {
        _engines = [];
        _dataChannel = Channel.CreateUnbounded<(string, UciOutput)>(new() { SingleReader = true }); //AllowSynchronousContinuations?
        _ioHandlerTokenSource = new();

        LoadEngines();
        ResetOutputData();

        StopTasks().Wait();
        StartTasks();

        StartAllEngines();
    }

    private void RemoveKilledProcesses() => _engines.RemoveAll(eng => eng.Proc is null || eng.Proc.HasExited);

    protected void StartAllEngines()
    {
        Parallel.ForEach(_engines, eng =>
        {
            eng.StartProcess();
        });

        AfterEnginesStarted();
    }

    protected virtual void AfterEnginesStarted() { }

    private void LoadEngines()
    {
        var cfg = Utils.EngineConfigs;
        cfg.Engines.ForEach(opt =>
        {
            _engines.Add(new Engine(opt, _dataChannel));
        });
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

    public virtual void ProcessInput(string command)
    {
        if (string.IsNullOrEmpty(command))
        {
            Log();
            return;
        }

        SendToAll(command);
    }

    protected void SendToAll(string command, bool printSend = true)
    {
        StopTasks().Wait();
        StartTasks(command);
        ResetOutputData(command);

        RemoveKilledProcesses();
        DoSendCommand(command, printSend);
    }

    protected virtual void DoSendCommand(string command, bool printSend = true)
    {
        Parallel.ForEach(_engines, eng => eng.SendCommand(command, printSend));
        Log();
    }


    protected abstract void ResetOutputData(string? command = null);
    protected abstract void StartTasks(string? command = null);

    protected async Task StopTasks()
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

}

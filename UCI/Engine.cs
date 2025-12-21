using System.Diagnostics;
using System.Threading.Channels;

namespace Consortium.UCI;

public class Engine
{
    public string FilePath { get; private set; }
    public string Name { get; private set; }
    public List<string> UCIOpts { get; private set; }
    public Dictionary<string, string> RemappedCmds { get; private set; }
    public Process Proc { get; private set; }

    private readonly Channel<(string Eng, UciOutput Line)> _dataChannel;
    private TaskCompletionSource<string>? _expectTcs;
    private Predicate<string>? _expectPredicate;

    public Engine(EngineRunOptions runOpts, Channel<(string Eng, UciOutput Line)> dataChannel)
    {
        Name = runOpts.Name;
        FilePath = runOpts.Path;
        UCIOpts = runOpts.Opts;
        RemappedCmds = [];

        runOpts.RemappedCmds.ForEach(cmd =>
        {
            var splits = cmd.Split(';');
            this.RemappedCmds.Add(splits[0], splits[1]);
        });

        _dataChannel = dataChannel;
    }

    public void StartProcess()
    {
        ProcessStartInfo info = new(FilePath)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardInput = true,
            CreateNoWindow = true
        };

        Proc = new()
        {
            StartInfo = info,
            EnableRaisingEvents = true
        };

        Proc.OutputDataReceived += OnOutputReceived;
        Proc.Exited += OnTerminated;
        Proc.Start();
        Proc.BeginOutputReadLine();
        Log($"{FormatEngineName(Name)} pid {Proc.Id}");
    }

    public async Task SendUCIOpts()
    {
        await SendAndExpect("uci", "uciok", 1000);
        await SendAndExpect("isready", "readyok");

        foreach (var opt in UCIOpts)
            SendCommand(opt);

        SendCommand("ucinewgame");

        await SendAndExpect("isready", "readyok");
    }

    private void OnTerminated(object? sender, EventArgs e) => Log($"{Name} terminated with code {Proc.ExitCode}");
    private void OnOutputReceived(object sender, DataReceivedEventArgs e)
    {
        if (e.Data == null) return;

        var uc = new UciOutput(e.Data);
        _dataChannel.Writer.TryWrite((Name, uc));
        if (_expectPredicate?.Invoke(e.Data) == true)
        {
            if (_expectTcs?.TrySetResult(e.Data) == false)
                throw new Exception($"_expectTcs.TrySetResult({e.Data}) failed??");
        }
    }

    public void SendCommand(string command)
    {
        Debug.Assert(Proc != null && !Proc.HasExited, "what");

        if (RemappedCmds.TryGetValue(command, out string? remapped))
            command = remapped;

        Log($"{FormatEngineName(Name)} << {command}");

        try
        {
            Proc.StandardInput.WriteLine(command);
            Proc.StandardInput.Flush();
        } catch { }
    }

    private async Task<string> SendAndExpect(string command, string expect, int timeoutMs)
    {
        return await SendAndExpect(command, expect, timeoutMs, null);
    }

    private async Task<string> SendAndExpect(string command, string expect, Action<string>? whenCompleted = null)
    {
        return await SendAndExpect(command, expect, 250, whenCompleted);
    }


    private async Task<string> SendAndExpect(string command, string expect, int timeoutMs, Action<string>? whenCompleted)
    {
        _expectPredicate = (r => expect.Equals(r));
        _expectTcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        SendCommand(command);

        using var timeoutCts = new CancellationTokenSource(timeoutMs);

        try
        {
            string result = await _expectTcs.Task.WaitAsync(timeoutCts.Token);
            whenCompleted?.Invoke(result);
            return result;
        }
        catch (OperationCanceledException e)
        {
            Log($"{Name} timed out waiting for '{expect}'!");
            return string.Empty;
        }
        finally
        {
            _expectPredicate = null;
            _expectTcs = null;
        }
    }

    public override string ToString() => Name;
}

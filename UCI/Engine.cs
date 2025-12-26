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

    public void Terminate()
    {
        try
        {
            Proc.StandardInput.Close();
            Proc.Kill();
        }
        catch { }
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
        Debug.Assert(Proc != null, "what");

        if (RemappedCmds.TryGetValue(command, out string? remapped))
            command = remapped;

        Log($"{FormatEngineName(Name)} << {command}");

        try
        {
            Proc.StandardInput.WriteLine(command);
            Proc.StandardInput.Flush();
        } catch { }
    }

    public async Task<string> SendAndWait(string command, int timeoutMs = 250)
    {
        return await SendAndExpect(command, (_ => true), timeoutMs, null);
    }

    private async Task<string> SendAndExpect(string command, string expect, int timeoutMs = 250, Action<string>? whenCompleted = null)
    {
        return await SendAndExpect(command, (r => expect.Equals(r)), timeoutMs, whenCompleted);
    }

    private async Task<string> SendAndExpect(string command, Predicate<string>? expect, int timeoutMs = 250, Action<string>? whenCompleted = null)
    {
        _expectPredicate = expect;
        _expectTcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        SendCommand(command);

        using var timeoutCts = new CancellationTokenSource(timeoutMs);

        try
        {
            string result = await _expectTcs.Task.WaitAsync(timeoutCts.Token);
            whenCompleted?.Invoke(result);
            return result;
        }
        catch (OperationCanceledException)
        {
            Debug.WriteLine($"{Name} timed out waiting for '{expect}'!");
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

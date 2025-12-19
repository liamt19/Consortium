using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading.Channels;

namespace Consortium.UCI;

public class Engine
{
    public string FilePath { get; private set; }
    public string Name { get; private set; }
    public List<string> UCIOpts { get; private set; }
    public Dictionary<string, string> RemappedCmds { get; private set; }
    public Process Proc { get; private set; }

    private Channel<(string Eng, string Line)> DataChannel;

    private bool UciOK = false;

    public Engine(EngineRunOptions runOpts, Channel<(string Eng, string Line)> dataChannel)
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

        DataChannel = dataChannel;
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

        SendUCIOpts().Wait();
    }


    private async Task SendUCIOpts()
    {
        SendCommand("uci");
        await WaitUntil(() => UciOK, 1500);

        SendCommand("isready");
        foreach (var opt in UCIOpts)
            SendCommand(opt);

        SendCommand("ucinewgame");
        SendCommand("isready");
    }

    private void OnTerminated(object? sender, EventArgs e) => Log($"{Name} terminated with code {Proc.ExitCode}");
    private void OnOutputReceived(object sender, DataReceivedEventArgs e)
    {
        if (e.Data != null)
        {
            DataChannel.Writer.TryWrite((Name, e.Data));
            if (e.Data.ToLower().StartsWith("uciok"))
                UciOK = true;
        }
    }

    public void SendCommand(string command)
    {
        Debug.Assert(Proc != null && !Proc.HasExited, "what");

        if (RemappedCmds.ContainsKey(command))
            command = RemappedCmds[command];

        Console.WriteLine($"{FormatEngineName(Name)} << {command}");

        try
        {
            Proc.StandardInput.WriteLine(command);
            Proc.StandardInput.Flush();
        } catch { }
    }

    public override string ToString() => Name;
}

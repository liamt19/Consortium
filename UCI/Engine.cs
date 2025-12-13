using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace Consortium.UCI;

public class Engine
{
    public string FilePath { get; private set; }
    public string Name { get; private set; }
    public List<string> UCIOpts { get; private set; }
    public Process Proc { get; private set; }

    public ConcurrentQueue<string> OutputQueue { get; set; }
    private bool IsReady;

    public Engine(EngineRunOptions runOpts)
    {
        Name = runOpts.Name;
        FilePath = runOpts.Path;
        UCIOpts = runOpts.Opts;

        IsReady = false;

        ProcessStartInfo info = new(FilePath)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardInput = true,
            CreateNoWindow = true
        };

        OutputQueue = [];

        Proc = new()
        {
            StartInfo = info,
            EnableRaisingEvents = true
        };

        Proc.OutputDataReceived += OnOutputReceived;
        Proc.Start();
        Proc.BeginOutputReadLine();

        SendUCIOpts();
    }

    private void SendUCIOpts()
    {
        SendCommand("uci");
        foreach (var opt in UCIOpts)
            SendCommand(opt);

        SendCommand("isready");
    }

    private void OnOutputReceived(object sender, DataReceivedEventArgs e)
    {
        if (e.Data != null)
        {
            OutputQueue.Enqueue(e.Data);
            
            if (e.Data.Equals("readyok", StringComparison.OrdinalIgnoreCase))
            {
                IsReady = true;
            }
        }
    }

    public void SendCommand(string command)
    {
        Debug.Assert(Proc != null && !Proc.HasExited, "what");

        Console.WriteLine($"{Name} >> {command}");

        Proc.StandardInput.WriteLine(command);
        Proc.StandardInput.Flush();
    }

    public override string ToString() => Name;
}

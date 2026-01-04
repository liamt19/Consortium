global using static Consortium.Core.Misc.Utils;
using Consortium.Core.Misc;
using System.Text;

namespace Consortium.Democracy;

internal class Program
{
    private static DemocracyController controller;
    public static CancellationTokenSource ShutdownToken = new();

    static void Main(string[] args)
    {
        AppDomain.CurrentDomain.ProcessExit += (s, e) => Terminate();

        Console.SetIn(new StreamReader(Console.OpenStandardInput(), Encoding.UTF8, false, 2048 * 4));
        Console.OutputEncoding = Encoding.UTF8;
        Console.CancelKeyPress += (s, e) =>
        {
            e.Cancel = true;
            ShutdownToken.Cancel();
        };

        controller = new DemocracyController();
        Task.Run(() => ShutdownManager.StartMonitoring(controller, ShutdownToken));

        while (!ShutdownToken.IsCancellationRequested)
        {
            string input = ReadConsoleLine();
            controller.ProcessInput(input);
        }

        Terminate();
    }

    private static void Terminate()
    {
        controller.TerminateProcesses();
    }
}
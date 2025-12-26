global using static Consortium.Misc.Utils;
using Consortium.Core;
using System.Text;

namespace Consortium;

internal class Program
{
    private static Controller controller;

    static void Main(string[] args)
    {
        CancellationTokenSource shutdownToken = new();
        AppDomain.CurrentDomain.ProcessExit += (s, e) => Terminate();

        Console.SetIn(new StreamReader(Console.OpenStandardInput(), Encoding.UTF8, false, 2048 * 4));
        Console.OutputEncoding = Encoding.UTF8;
        Console.CancelKeyPress += (s, e) =>
        {
            e.Cancel = true;
            shutdownToken.Cancel();
        };

        controller = new();
        while (!shutdownToken.IsCancellationRequested)
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
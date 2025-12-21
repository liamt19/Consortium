global using static Consortium.Misc.Utils;
using Consortium.Core;
using System.Text;

namespace Consortium;

internal class Program
{
    static void Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        InputLoop();
    }

    static void InputLoop()
    {
        Controller controller = new Controller();

        string input;
        do
        {
            input = ReadConsoleLine();
            controller.ProcessInput(input);
        } while (input?.ToLower() != "exit");
    }
}
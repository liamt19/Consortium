global using static Consortium.Utils;

namespace Consortium;

internal class Program
{
    static void Main(string[] args)
    {
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
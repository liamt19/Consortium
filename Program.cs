using System;

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
            input = Console.ReadLine();
            controller.SendToAll(input);
        } while (input?.ToLower() != "exit");
    }
}
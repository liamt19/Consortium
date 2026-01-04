
using Consortium.Core.UCI;
using System.Diagnostics;

namespace Consortium.Core.Misc;

public static class ShutdownManager
{
    public static async Task StartMonitoring(Controller controller, CancellationTokenSource token)
    {
        while (controller.GetEngines().Length == 0)
            await Task.Delay(1000);

        while (!token.IsCancellationRequested)
        {
            bool cancel = true;
            foreach (var eng in controller.GetEngines())
            {
                if (!eng.HasTerminated)
                {
                    cancel = false;
                    break;
                }
            }

            if (cancel)
            {
                token.Cancel();
            }

            await Task.Delay(1000);
        }

        Environment.Exit(0);
    }
}

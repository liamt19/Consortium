using Consortium.UCI;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;

namespace Consortium;

public static class Utils
{
    private static readonly Regex SetoptionRegex = new(@"^setoption name (.+) value (.+)$", RegexOptions.Compiled);
    public static List<EngineRunOptions> ReadConfig()
    {
        string json = File.ReadAllText("config.json");
        var cfg = JsonConvert.DeserializeObject<EngineConfig>(json) ?? throw new InvalidOperationException("Invalid config?");

        foreach (var eng in cfg.Engines)
        {
            foreach (var defaultOpt in cfg.DefaultOpts)
            {
                var match = SetoptionRegex.Match(defaultOpt);
                var optName = match.Groups[1].Value;
                if (!eng.Opts.Any(opt => SetoptionRegex.Match(opt) is { Success: true } m && m.Groups[1].Value == optName))
                {
                    eng.Opts.Add(defaultOpt);
                }
            }
        }

        return cfg.Engines;
    }

    public static void Log() => Log("");
    public static void Log(string s)
    {
        Console.WriteLine(s);
        Debug.WriteLine(s);
    }

    public static async Task WaitUntil(Func<bool> condition, int timeout = -1)
    {
        var timeoutTask = Task.Delay(timeout);
        var condTask = Task.Run(async () =>
        {
            while (!condition()) 
                await Task.Delay(25);
        });

        await Task.WhenAny(condTask, timeoutTask);
    }

}

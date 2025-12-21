using Consortium.UCI;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;

namespace Consortium.Misc;

public static class Utils
{
    public const bool PrintWithTimestamps = false;

    private static readonly Regex SetoptionRegex = new(@"^setoption name (.+) value (.+)$", RegexOptions.Compiled);

    private static EngineConfig? CachedEngineConfig = null;
    public static EngineConfig EngineConfigs => (CachedEngineConfig ??= ReadConfig());
    public static List<EngineRunOptions> EngineRunConfigs => EngineConfigs.Engines;

    private static EngineConfig ReadConfig()
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

        return cfg;
    }

    public static void Log() => Log("");
    public static void Log(string s)
    {
        if (PrintWithTimestamps)
            s = $"{RightNow} - {s}";

        BatchedConsoleWriter.WriteLine(s);
        Debug.WriteLine(s);
    }

    public static bool EqualsIgnoreCase(this string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    public static bool StartsWithIgnoreCase(this string a, string b) => a.StartsWith(b, StringComparison.OrdinalIgnoreCase);

    private static readonly long ProcStartTime = DateTimeOffset.Now.Ticks;
    public static long RightNow => (DateTimeOffset.Now.Ticks - ProcStartTime) / (TimeSpan.NanosecondsPerTick);

    public static string FormatEngineName(string name)
    {
        int nChars = EngineRunConfigs.Max(e => e.Name.Length);
        return name.PadLeft(nChars);
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


    public static string ReadConsoleLine()
    {
        string line = Console.ReadLine()!;
        if (Console.IsOutputRedirected)
            return line;

        Console.SetCursorPosition(0, Console.CursorTop - 1);
        Console.WriteLine(new string(' ', Console.WindowWidth));
        Console.WriteLine(line);
        Console.SetCursorPosition(0, Console.CursorTop);

        return line;
    }

}

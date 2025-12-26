using Consortium.UCI;
using Newtonsoft.Json;
using System.Diagnostics;
using System.Runtime.InteropServices;
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

    public static void Log() => Log(string.Empty);
    public static void Log(string s)
    {
        if (PrintWithTimestamps)
            s = $"{RightNow} - {s}";

        BatchedConsoleWriter.WriteLine(s);
        Debug.WriteLine(s);
    }

    public static bool EqualsIgnoreCase(this string? a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    public static bool StartsWithIgnoreCase(this string? a, string b) => a?.StartsWith(b, StringComparison.OrdinalIgnoreCase) == true;

    private static readonly long ProcStartTime = DateTimeOffset.Now.Ticks;
    public static long RightNow => (DateTimeOffset.Now.Ticks - ProcStartTime) / (TimeSpan.NanosecondsPerTick);

    public static string FormatEngineName(string name)
    {
        int nChars = EngineRunConfigs.Max(e => e.Name.Length);
        return name.PadLeft(nChars);
    }


    public static string? ReadConsoleLine()
    {
        string line = Console.ReadLine();
        if (Console.IsOutputRedirected || line is null)
            return line;

        Console.SetCursorPosition(0, Console.CursorTop - 1);
        Console.WriteLine(new string(' ', Console.WindowWidth));
        Console.WriteLine(line);
        Console.SetCursorPosition(0, Console.CursorTop);

        return line;
    }

    public static readonly bool HasAnsi = CheckAnsi();
    private static bool CheckAnsi()
    {
        if (Console.IsOutputRedirected)
            return false;

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return true;

        //  Windows 11
        return (Environment.OSVersion.Version.Build >= 22000);
    }

    public static Dictionary<string, (int groupNum, int ansiLen)> GroupPVs(List<string> rootPVGroups, List<(string name, UciOutput uc)> outputs)
    {
        var dict = new Dictionary<string, (int groupNum, int ansiLen)>();

        var groups = outputs.GroupBy(x => x.uc.PV.Split(' ')[0])
            .Select(x => x.ToList())
            .OrderByDescending(g => g.Count)
            .ToList();

        foreach (var pv in outputs.Select(x => x.uc.PV.Split(' ')[0]))
        {
            if (!rootPVGroups.Contains(pv))
            {
                rootPVGroups.Add(pv);
            }
        }


        foreach (var group in groups)
        {
            var tokens = group
                .Select(m => (m.name, pv: m.uc.PV.Split(' ')))
                .ToList();

            foreach (var (name, pv) in tokens)
            {
                int bestOverlap = 1;

                foreach (var (_, otherPv) in tokens)
                {
                    if (ReferenceEquals(pv, otherPv))
                        continue;

                    bestOverlap = Math.Max(bestOverlap, PrefixOverlap(pv, otherPv));
                }

                int gNum = rootPVGroups.IndexOf(pv[0]);
                dict.Add(name, (gNum, bestOverlap));
            }
        }

        return dict;
    }

    private static int PrefixOverlap(string[] a, string[] b)
    {
        int i = 0;
        for (; i < Math.Min(a.Length, b.Length); i++)
            if (a[i] != b[i])
                break;

        return i;
    }


    private static readonly List<int> ANSI_GROUPS = [161, 10, 12, 11, 13, 14, 160, 166, 128, 172, 214, 112, 122, 81];
    public static string AnsiFormatForGroup(string s, int group) => ToAnsi(s, ANSI_GROUPS[Math.Min(group, ANSI_GROUPS.Count)]);
    public static string ToAnsi(string s, int code = 8) => $"\u001b[38;5;{code}m{s}\u001b[0m";
}

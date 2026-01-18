
using Consortium.Core.Misc;
using Consortium.Core.UCI;
using Newtonsoft.Json;
using System.Collections;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace Consortium.Core.Misc;

public static class Utils
{
    public const bool PrintWithTimestamps = false;

    private static readonly Regex SetoptionRegex = new(@"^setoption name (.+) value (.+)$", RegexOptions.Compiled);
    public static readonly Regex FenRegex = new(@"(?:[rnbqkpRNBQKP1-8]+\/){7}[rnbqkpRNBQKP1-8]+ [wb] (?:-|K?Q?k?q?) (?:-|[a-h][36]) \d* *\d*");

    public static int EngineCount => EngineConfigs.Engines.Count;

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
        BatchedConsoleWriter.WriteLine(s);
        Debug.WriteLine(s);
    }

    [Conditional("LOG_VERBOSE")]
    public static void LogVerbose(string s)
    {
        Log(s);
    }

    public static bool EqualsIgnoreCase(this string? a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    public static bool StartsWithIgnoreCase(this string? a, string b) => a?.StartsWith(b, StringComparison.OrdinalIgnoreCase) == true;

    public static void AddIfMissing<T>(this List<T> list, T what) where T : notnull
    {
        if (!list.Contains(what))
            list.Add(what);
    }

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


    public static int PrefixOverlap(string[] a, string[] b)
    {
        int i = 0;
        for (; i < Math.Min(a.Length, b.Length); i++)
        {
            if (a[i] != b[i])
                break;
        }

        return i;
    }

    public static string Stringify(this BitArray arr)
    {
        StringBuilder sb = new();
        sb.Append('[');
        for (int i = 0; i < arr.Length; i++)
        {
            sb.Append(arr[i] ? '1' : '0');
        }
        sb.Append(']');
        return sb.ToString();
    }

    public static string Stringify<T>(this HashSet<T> set) where T : notnull
    {
        var arr = set.ToArray();
        StringBuilder sb = new();
        sb.Append('[');
        for (int i = 0; i < arr.Length; i++)
        {
            sb.Append(arr[i].ToString());
            if (i != (arr.Length - 1))
                sb.Append(", ");
        }
        sb.Append(']');
        return sb.ToString();
    }

    public static string StringifyGroups(Dictionary<string, (int, int)> dict)
    {
        var arr = dict.Select(kv => $"({kv.Key}={kv.Value.Item1}/{kv.Value.Item2})").ToArray();
        return $"[{string.Join(", ", arr)}]";
    }


    private static readonly List<int> ANSI_GROUPS = [161, 10, 12, 11, 13, 14, 160, 166, 128, 172, 214, 112, 122, 81];
    public static string AnsiFormatForGroup(string s, int group) => ToAnsi(s, ANSI_GROUPS[Math.Min(group, ANSI_GROUPS.Count)]);
    public static string ToAnsi(string s, int code = 8) => $"\u001b[38;5;{code}m{s}\u001b[0m";
}

using Consortium.Misc;
using System.Text.RegularExpressions;

namespace Consortium.UCI;

public readonly struct UciOutput
{
    private const int NUM_PV_MOVES = 16;

    private static readonly Regex ScoreRegex = new(@" score (\w+) (-?\d+)", RegexOptions.Compiled);
    private static readonly Regex DepthRegex = new(@" depth (\d+)", RegexOptions.Compiled);
    private static readonly Regex SelDepthRegex = new(@" seldepth (\d+)", RegexOptions.Compiled);
    private static readonly Regex NodesRegex = new(@" nodes (\d+)", RegexOptions.Compiled);
    private static readonly Regex TimeRegex = new(@" time (\d+)", RegexOptions.Compiled);
    private static readonly Regex PVRegex = new(@" pv (.+)", RegexOptions.Compiled);
    private static readonly Regex BestmoveRegex = new(@"bestmove (\S+)", RegexOptions.Compiled);

    public static bool IsBlacklisted(string str)
    {
        if (str.StartsWithIgnoreCase("option name "))
            return true;

        if (str.StartsWithIgnoreCase("id "))
            return true;

        return false;
    }

    public readonly string Line { get; }
    public UciOutput(string line)
    {
        Line = line;
    }

    public bool IsInfo => Line.StartsWith("info ") && !Line.StartsWith("info string");
    public bool IsPrintable => IsInfo || !IsBlacklisted(Line);
    public bool IsBound => Line.Contains("upperbound") || Line.Contains("lowerbound");
    public bool IsCurrMove => Line.Contains("currmove");
    public bool IsBestmove => Line.Contains("bestmove");
    public bool HasSelDepth => SelDepth > 0;
    public bool ShouldPrint => !IsBound && !IsCurrMove;
    public bool ShouldIncDepth => IsInfo && !IsBound && !IsCurrMove;

    public string Score
    {
        get
        {
            Match match = ScoreRegex.Match(Line);
            if (match.Success)
            {
                var units = match.Groups[1].Value.Contains("mate") ? "#" : "cp ";
                var num = int.Parse(match.Groups[2].Value);
                return $"{units}{num}";
            }

            return "???";
        }
    }

    public int Depth => Line != null && DepthRegex.Match(Line) is { Success: true } m ? int.Parse(m.Groups[1].Value) : 0;
    public int SelDepth => Line != null && SelDepthRegex.Match(Line) is { Success: true } m ? int.Parse(m.Groups[1].Value) : 0;
    public ulong Nodes => Line != null && NodesRegex.Match(Line) is { Success: true } m ? ulong.Parse(m.Groups[1].Value) : 0;
    public ulong Time => Line != null && TimeRegex.Match(Line) is { Success: true } m ? ulong.Parse(m.Groups[1].Value) : 0;
    public string PV => Line != null && PVRegex.Match(Line) is { Success: true } m ? m.Groups[1].Value : "";
    public string Bestmove => Line != null && BestmoveRegex.Match(Line) is { Success: true } m ? m.Groups[1].Value : Line;

    private string GetMoves(int n, int skip = 0)
    {
        var moves = PV.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (skip >= moves.Length || n <= 0)
            return string.Empty;

        var slice = moves.Skip(skip).Take(n - skip);
        return string.Join(' ', slice);
    }

    private string DepthStr => $"D: {Depth,3}";
    private string SelDepthStr => $"/{(HasSelDepth ? SelDepth.ToString() : string.Empty),-3}";
    private string ScoreStr => $"S: {Score,9}";
    private string NodeStr => $"N: {Nodes,11}";
    private string TimeStr => $"T: {Time,8}";
    private string PVStr => $"M: {GetMoves(NUM_PV_MOVES)}";

    public override string ToString() => ToString(false);
    public string ToString(bool rawUCI)
    {
        if (!IsInfo || rawUCI)
            return Line;

        List<string> strs =
        [
            DepthStr + SelDepthStr,
            ScoreStr,
            NodeStr,
            TimeStr,
            PVStr
        ];

        return string.Join("  ", strs);
    }

    public string FormatAnsi(int group, int ansiLen, bool rawUCI = false)
    {
        if (!HasAnsi)
            return ToString(rawUCI);

        if (!IsInfo || rawUCI)
            return Line;

        var ansiMoves = Math.Min(NUM_PV_MOVES, ansiLen);
        var pvGrp = $"M: {AnsiFormatForGroup(GetMoves(ansiMoves), group)}";
        var pvRest = GetMoves(NUM_PV_MOVES, ansiMoves);

        List<string> strs =
        [
            DepthStr + SelDepthStr,
            ScoreStr,
            NodeStr,
            TimeStr,
            pvGrp,
            pvRest
        ];

        return string.Join(" ", strs);
    }
}

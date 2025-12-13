using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Consortium.UCI;

public partial struct UciOutput(string line)
{
    private static readonly Regex ScoreRegex = new(@" score (\w+) (-?\d+)", RegexOptions.Compiled);
    private static readonly Regex DepthRegex = new(@" depth (\d+)", RegexOptions.Compiled);
    private static readonly Regex SelDepthRegex = new(@" seldepth (\d+)", RegexOptions.Compiled);
    private static readonly Regex NodesRegex = new(@" nodes (\d+)", RegexOptions.Compiled);
    private static readonly Regex TimeRegex = new(@" time (\d+)", RegexOptions.Compiled);
    private static readonly Regex PVRegex = new(@" pv (.+)", RegexOptions.Compiled);

    public string Line { get; } = line;
    public bool IsInfo => Line.StartsWith("info ") && !Line.StartsWith("info string");
    public bool IsBound => Line.Contains("upperbound") || Line.Contains("lowerbound");
    public bool IsCurrMove => Line.Contains("currmove");

    public bool HasSelDepth => SelDepth > 0;

    public bool ShouldPrint => IsInfo && !IsBound && !IsCurrMove;
    public bool ShouldIncDepth => IsInfo && !IsBound && !IsCurrMove;

    public string Score
    {
        get
        {
            Match match = ScoreRegex.Match(Line);
            if (match.Success)
            {
                var units = match.Groups[1].Value.Contains("mate") ? "#" : "";
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

    private string GetMoves(int n)
    {
        var moves = PV.Split(' ');
        if (moves.Length <= n)
            return PV;
        return string.Join(' ', moves, 0, n);
    }

    private string DepthStr => $"D: {Depth,3}";
    private string SelDepthStr => $"/{(HasSelDepth ? SelDepth.ToString() : string.Empty),-3}";
    private string ScoreStr => $"S: {Score,6}";
    private string NodeStr => $"N: {Nodes,12}";
    private string TimeStr => $"T: {Time,8}";
    private string PVStr => $"M: {GetMoves(8)}";

    public override string ToString()
    {
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
}

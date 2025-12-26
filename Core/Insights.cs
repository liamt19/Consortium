using Consortium.Misc;
using Consortium.UCI;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;

namespace Consortium.Core;

public static class Insights
{
    private const char SEPARATOR = ';';

    public const string FIELD_NODES = "Nodes";
    public const string FIELD_SCORE = "Score";
    public const string FIELD_SELDEPTH = "SelDepth";
    public const string FIELD_BRANCHING = "Branching";

    private const int MATE_OFFSET = 33000;

    public static bool IsBreakdownCommand(string command)
    {
        return command.StartsWithIgnoreCase("breakdown");
    }

    public static void BreakdownOf(ConcurrentDictionary<string, List<UciOutput>> dict, string field = FIELD_SCORE)
    {
        field = field.Trim().ToLower();
        if (field.StartsWith("breakdown "))
            field = field["breakdown ".Length..].Trim();

        field = field switch
        {
            "nodes" => FIELD_NODES,
            "score" => FIELD_SCORE,
            "seldepth" => FIELD_SELDEPTH,
            "branching" => FIELD_BRANCHING,
            _ => field
        };

        PrintBreakdown(dict, field);
    }

    private static void PrintBreakdown(ConcurrentDictionary<string, List<UciOutput>> dict, string field = FIELD_SCORE)
    {
        var keys = dict.Keys.ToList();
        keys.Insert(0, "depth");
        Console.Write(string.Join(SEPARATOR, keys));
        Console.WriteLine();

        var outpList = dict.Values.Select(x => x.Where(y => !y.IsBound && !y.IsCurrMove).ToList()).ToList();
        
        var flat = outpList.SelectMany(x => x).Where(x => !x.IsBound && !x.IsCurrMove);
        if (!flat.Any())
            return;

        int maxDepth = flat.Max(x => x.Depth);

        for (int depth = 1; depth <= maxDepth; depth++)
        {
            Console.Write($"{depth}{SEPARATOR}");
            for (int eng = 0; eng < outpList.Count; eng++)
            {
                var thisOutp = outpList[eng];
                string outStr = "";

                //  No output at this depth for this eng
                if (!thisOutp.Any(x => (x.IsInfo && x.Depth == depth)))
                {
                    outStr = "";
                    goto Skip;
                }

                var outp = thisOutp.First(x => (x.IsInfo && x.Depth == depth));
                var prevOutp = thisOutp.FirstOrDefault(x => (x.IsInfo && x.Depth == (depth - 1)), outp);

                outStr = outp.Score;
                if (field == FIELD_NODES)
                {
                    outStr = outp.Nodes.ToString();
                }
                else if (field == FIELD_SCORE)
                {
                    outStr = outp.Score;
                    outStr = outStr.Replace("cp ", "");
                    if (outStr.Contains("mate") || outStr.Contains("#"))
                    {
                        outStr = outStr.Replace("mate", "").Replace("#", "");
                        int v = int.Parse(outStr);
                        if (v > 0)
                            outStr = (v + MATE_OFFSET).ToString();
                        else
                            outStr = (v - MATE_OFFSET).ToString();
                    }
                }
                else if (field == FIELD_SELDEPTH)
                {
                    outStr = outp.SelDepth.ToString();
                }
                else if (field == FIELD_BRANCHING)
                {
                    outStr = ((double)outp.Nodes / prevOutp.Nodes).ToString("0.0000");
                }

                Skip:
                Console.Write(outStr);
                if (eng != outpList.Count - 1)
                    Console.Write(SEPARATOR);
            }

            Console.WriteLine();
        }

        Console.WriteLine();
    }
}

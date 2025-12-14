using Consortium.UCI;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;

namespace Consortium;

public static class Insights
{
    public const string FIELD_NODES = "Nodes";
    public const string FIELD_SCORE = "Score";
    public const string FIELD_SELDEPTH = "SelDepth";
    public const string FIELD_BRANCHING = "Branching";

    private const int MATE_OFFSET = 33000;

    public static bool IsBreakdownCommand(string command)
    {
        return command.ToLower().StartsWith("breakdown");
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
        Console.Write(string.Join(" ", keys));

        var outpList = dict.Values.Select(x => x.Where(y => !y.IsBound && !y.IsCurrMove).ToList()).ToList();
        int maxDepth = outpList.SelectMany(x => x).Where(x => !x.IsBound && !x.IsCurrMove).Max(x => x.Depth);

        for (int depth = 0; depth <= maxDepth; depth++)
        {
            Console.Write($"{depth} ");
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
                    Console.Write(" ");
            }

            Console.WriteLine();
        }

    }

    public static void PrintBreakdown(List<UciOutput> outputs, string field = FIELD_SCORE)
    {
        Console.WriteLine($"depth {field}");
        int maxDepth = outputs.Max(x => x.Depth);
        for (int i = 0; i <= maxDepth; i++)
        {
            var outp = outputs[i];

            Console.Write($"{i} ");
            var outpField = field switch
            {
                FIELD_NODES => outp.Nodes.ToString(),
                FIELD_SCORE => outp.Score,
                FIELD_SELDEPTH => outp.SelDepth.ToString(),
                _ => FIELD_SCORE
            };
            Console.WriteLine(outpField);
        }
    }
}

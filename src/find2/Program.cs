using System;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("find2.Tests")]

namespace find2;

internal sealed class Program
{
    private static void Main(string[] args)
    {
        var arguments = ExpressionMatch.Build(args);

        if (arguments.DebugOptions.HasFlag(DebugOptions.Stat))
        {
            PrintBufferStats(arguments);
            return;
        }

        // TODO: Add optional output buffering option.
        // Might help performance on high output options by reducing system calls.
        // Add bench mark to determine performance of this. Perhaps have an automatic enablement.
        using var find = new Find(arguments);
        find.Matched += (_, fullPath) => Console.WriteLine(fullPath);
        find.Run();
    }

    private static void PrintBufferStats(FindArguments arguments)
    {
        var stats = BufferSizeStatistic.Create(arguments);
        Console.WriteLine($"""
            Stats of {stats.Count} directories:
            * Min: {stats.Min}
            * Max: {stats.Max}
            * Average: {stats.Average}
            * Mean: {stats.Mean}
            """);
    }
}
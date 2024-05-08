using System;
using System.IO;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("find2.Tests")]

namespace find2;

internal sealed class Program
{
    private static void Main(string[] args)
    {
        Run(args, Console.Out);
    }

    internal static void Run(string[] args, TextWriter target)
    {
        var arguments = ExpressionMatch.Build(args);

        if (arguments.DebugOptions.HasFlag(DebugOptions.Stat))
        {
            PrintBufferStats(arguments, target);
            return;
        }

        // TODO: Add optional output buffering option.
        // Might help performance on high output options by reducing system calls.
        // Add bench mark to determine performance of this. Perhaps have an automatic enablement.
        // Keep in mind that Matched can be called from multiple threads.
        using var find = new Find(arguments);
        var terminator = arguments.Print0 ? '\0' : '\n';
        find.Matched += (_, fullPath) =>
        {
            // TODO: TextWriter does this with 2 writes, but this requires atomic operations.
            // We could test a ThreadLocal string buffer, would need to test the performance differences.
            target.Write($"{fullPath}{terminator}");
        };
        find.Run();
    }

    private static void PrintBufferStats(FindArguments arguments, TextWriter target)
    {
        var stats = BufferSizeStatistic.Create(arguments);
        target.WriteLine($"""
            Stats of {stats.Count} directories:
            * Min: {stats.Min}
            * Max: {stats.Max}
            * Average: {stats.Average}
            * Mean: {stats.Mean}
            """);
    }
}
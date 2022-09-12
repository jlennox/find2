using System;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("find2.Tests")]

namespace find2
{
    internal sealed class Program
    {
        private static void Main(string[] args)
        {
            var arguments = ExpressionMatch.Build(args);
            using var find = new Find(arguments);
            find.Matched += (_, fullPath) => Console.WriteLine(fullPath);
            find.Run();
        }
    }
}

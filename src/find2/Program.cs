using System;
using System.IO;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("find2.Tests")]

namespace find2
{
    internal sealed class Program
    {
        private static void Main(string[] args)
        {
            var dir = Path.GetFullPath(args[1]);

            Console.WriteLine("Root:" + dir);
            var find = new Find(args[0], new[] { dir });

            find.Match += match => Console.WriteLine("!!Found " + match);

            find.Run();
        }
    }
}

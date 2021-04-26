using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace find2.Tests
{
    public class Tests
    {
        private static string GetGnuFindPath()
        {
            var gnuFindPath = Environment.GetEnvironmentVariable("gnu_find_path");
            if (gnuFindPath != null) return gnuFindPath;

            var pathString = Environment.GetEnvironmentVariable("path");

            return pathString?.Split(';')
                // We want to skip over the find.exe in system32, because Window's
                // built in find is a different command. We're searching for one
                // installed by cygwin or the like.
                .Where(path => !string.Equals(Path.GetFileName(path), "system32",
                    StringComparison.InvariantCultureIgnoreCase))
                .Select(path => Path.Combine(path, "find.exe"))
                .FirstOrDefault(File.Exists);
        }

        private static readonly string _gnuFindPath = GetGnuFindPath();

        private static void RunTest(
            string args,
            params FindTestPath[] files)
        {
            using var test = new FindTest(files);
            var found = new List<string>();

            var find = new Find(ExpressionMatch.Build($"{test.Root} {args}".Split(' ')));
            find.Match += (_, fullPath) => {
                lock (found)
                {
                    found.Add(fullPath);
                }
            };
            find.Run();

            CollectionAssert.AreEquivalent(test.Expected, found);

            if (_gnuFindPath == null)
            {
                throw new Exception("Unable to locate GNU find.");
            }

            using var findProc = new Process {
                StartInfo = new ProcessStartInfo {
                    FileName = _gnuFindPath,
                    Arguments = $"{test.Root} {args}",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                }
            };

            findProc.Start();

            var findOutput = findProc.StandardOutput.ReadToEnd()
                // Find very annoyingly returns results such as:
                // C:\Users\joe\AppData\Local\Temp\pzltuf3d.fz3/sub dir2/Im_found
                // This is a behavior we intentionally do not mimic.
                .Replace('/', '\\')
                .Trim()
                .Split('\n');

            var a = string.Join('\n', findOutput.OrderBy(t => t));
            var b = string.Join('\n', found.OrderBy(t => t));

            CollectionAssert.AreEquivalent(findOutput, found);
        }

        [Test]
        public void TestSingleItemAtRootIsFound()
        {
            const string foundItem = "Im_found";

            RunTest($"-name {foundItem}",
                FindTestPath.ExpectedFile(foundItem),
                FindTestPath.File("Im not found")
            );
        }

        [Test]
        public void TestMultipleItemsWithSomeDepth()
        {
            const string foundItem = "Im_found";

            RunTest($"-name {foundItem}",
                FindTestPath.ExpectedFile("sub dir1", foundItem),
                FindTestPath.File("sub dir1", "also filename"),
                FindTestPath.ExpectedFile("sub dir2", foundItem),
                FindTestPath.ExpectedFile(foundItem)
            );
        }

        [Test]
        public void EmptyRecursivelyReturnsAllFiles()
        {
            const string foundItem = "Im_found";

            RunTest($"",
                FindTestPath.ExpectedDir(""),
                FindTestPath.ExpectedDir("sub dir1"),
                FindTestPath.ExpectedDir("sub dir2"),
                FindTestPath.ExpectedFile("sub dir1", foundItem),
                FindTestPath.ExpectedFile("sub dir1", "also filename"),
                FindTestPath.ExpectedFile("sub dir2", foundItem),
                FindTestPath.ExpectedFile(foundItem)
            );
        }
    }
}
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
        // Consider: packing find in with the test project.
        private static string GetGnuFindPath()
        {
            var gnuFindPath = Environment.GetEnvironmentVariable("gnu_find_path");
            if (gnuFindPath != null) return gnuFindPath;

            var pathString = Environment.GetEnvironmentVariable("path");
            if (pathString == null) throw new Exception("Unable to read path string.");

            var findPath = pathString.Split(';')
                // We want to skip over the find.exe in system32, because Window's
                // built in find is a different command. We're searching for one
                // installed by cygwin or the like.
                .Where(path => !string.Equals(Path.GetFileName(path), "system32",
                    StringComparison.InvariantCultureIgnoreCase))
                .Select(path => Path.Combine(path, "find.exe"))
                .FirstOrDefault(File.Exists);

            if (findPath == null) throw new Exception("Unable to locate GNU `find`.");
            return findPath;
        }

        private static readonly string _gnuFindPath = GetGnuFindPath();

        private static void RunTest(string args, params FindTestPath[] files)
        {
            using var test = new FindTest(files);
            var foundDefault = new List<string>();
            var foundDotnet = new List<string>();

            var combinedArgs = $"{test.Root} {args}".Trim();

            var find = new Find(ExpressionMatch.Build(combinedArgs.Split(' ')));
            find.Match += (_, fullPath) => {
                lock (foundDefault)
                {
                    foundDefault.Add(fullPath);
                }
            };
            find.Run();

            var findDotnet = new Find(ExpressionMatch.Build($"--engine dotnet {combinedArgs}".Split(' ')));
            findDotnet.Match += (_, fullPath) => {
                lock (foundDefault)
                {
                    foundDotnet.Add(fullPath);
                }
            };
            findDotnet.Run();

            CollectionAssert.AreEquivalent(test.Expected, foundDefault, "Failed default engine test vs expectations");
            CollectionAssert.AreEquivalent(test.Expected, foundDotnet, "Failed dotnet engine test vs expectations");

            if (_gnuFindPath == null)
            {
                throw new Exception("Unable to locate GNU find.");
            }

            using var findProc = new Process {
                StartInfo = new ProcessStartInfo {
                    FileName = _gnuFindPath,
                    Arguments = combinedArgs,
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
            var b = string.Join('\n', foundDefault.OrderBy(t => t));

            CollectionAssert.AreEquivalent(findOutput, foundDefault, "Failed default engine test vs GNU find");
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

        [Test]
        public void TypeCanDetectDirectoriesVsFiles()
        {
            const string foundItem = "Im_found";

            RunTest("-type d",
                FindTestPath.ExpectedDir(""),
                FindTestPath.ExpectedDir("sub dir1"),
                FindTestPath.ExpectedDir("sub dir2"),
                FindTestPath.File("sub dir1", foundItem),
                FindTestPath.File("sub dir1", "also filename"),
                FindTestPath.File("sub dir2", foundItem),
                FindTestPath.File(foundItem)
            );

            RunTest("-type f",
                FindTestPath.Dir(""),
                FindTestPath.Dir("sub dir1"),
                FindTestPath.Dir("sub dir2"),
                FindTestPath.ExpectedFile("sub dir1", foundItem),
                FindTestPath.ExpectedFile("sub dir1", "also filename"),
                FindTestPath.ExpectedFile("sub dir2", foundItem),
                FindTestPath.ExpectedFile(foundItem)
            );
        }

        [Test]
        public void Size()
        {
            RunTest("-size 5c -type f",
                FindTestPath.Dir(""),
                FindTestPath.Dir("sub dir1"),
                FindTestPath.Dir("sub dir2"),
                FindTestPath.ExpectedFile(5, "sub dir1", "another file"),
                FindTestPath.ExpectedFile(5, "sub dir1", "also filename"),
                FindTestPath.File(6, "sub dir2", "another file2"),
                FindTestPath.File(4, "another file3"),
                FindTestPath.ExpectedFile(5, "another file4")
            );

            // `-size -1048576c` and `-size -1M` behave very different from each other due to how rounding works.
            // `-1M` will only match files that are 0 bytes, because all others are rounded up to `1M`, while the
            // `c` suffix has no rounding.
            RunTest($"-size -{1024 * 1024}c -type f",
                FindTestPath.Dir(""),
                FindTestPath.Dir("sub dir1"),
                FindTestPath.Dir("sub dir2"),
                FindTestPath.ExpectedFile(1, "sub dir1", "1 byte file"),
                FindTestPath.ExpectedFile(2, "sub dir1", "2 byte file"),
                FindTestPath.File(1024 * 1024 + 1, "sub dir1", "1mb and 1"),
                FindTestPath.File(1024 * 1024, "sub dir2", "exactly 1mb"),
                FindTestPath.ExpectedFile(1024 * 1024 - 1, "sub dir2", "1mb minus 1"),
                FindTestPath.File(1024 * 1024 + 2, "1mb and 2 in root dir")
            );

            RunTest("-size -1M -type f",
                FindTestPath.Dir(""),
                FindTestPath.Dir("sub dir1"),
                FindTestPath.Dir("sub dir2"),
                FindTestPath.ExpectedFile(0, "sub dir1", "1 byte file"),
                FindTestPath.ExpectedFile(0, "sub dir1", "2 byte file"),
                FindTestPath.File(1024 * 1024 + 1, "sub dir1", "1mb and 1"),
                FindTestPath.File(1024 * 1024, "sub dir2", "exactly 1mb"),
                FindTestPath.File(1024 * 1024 - 1, "sub dir2", "1mb minus 1"),
                FindTestPath.File(1024 * 1024 + 2, "1mb and 2 in root dir")
            );

            RunTest("-size +1M -type f",
                FindTestPath.Dir(""),
                FindTestPath.Dir("sub dir1"),
                FindTestPath.Dir("sub dir2"),
                FindTestPath.File(0, "sub dir1", "1 byte file"),
                FindTestPath.File(0, "sub dir1", "2 byte file"),
                FindTestPath.ExpectedFile(1024 * 1024 + 1, "sub dir1", "1mb and 1"),
                FindTestPath.File(1024 * 1024, "sub dir2", "exactly 1mb"),
                FindTestPath.File(1024 * 1024 - 1, "sub dir2", "1mb minus 1"),
                FindTestPath.ExpectedFile(1024 * 1024 + 2, "1mb and 2 in root dir")
            );
        }

        [Test]
        public void MinDepth()
        {
            RunTest("-mindepth 0",
                FindTestPath.ExpectedDir(""),
                FindTestPath.ExpectedFile("root file"),
                FindTestPath.ExpectedDir("sub dir1"),
                FindTestPath.ExpectedDir("sub dir2"),
                FindTestPath.ExpectedFile("sub dir1", "another file"),
                FindTestPath.ExpectedFile("sub dir2", "another file4")
            );

            RunTest("-mindepth 1",
                FindTestPath.Dir(""),
                FindTestPath.ExpectedFile("root file"),
                FindTestPath.ExpectedDir("sub dir1"),
                FindTestPath.ExpectedDir("sub dir2"),
                FindTestPath.ExpectedFile("sub dir1", "another file"),
                FindTestPath.ExpectedFile("sub dir2", "another file4")
            );

            RunTest("-mindepth 2",
                FindTestPath.Dir(""),
                FindTestPath.File("root file"),
                FindTestPath.Dir("sub dir1"),
                FindTestPath.Dir("sub dir2"),
                FindTestPath.ExpectedFile("sub dir1", "another file"),
                FindTestPath.ExpectedFile("sub dir2", "another file4")
            );
        }

        [Test]
        public void MaxDepth()
        {
            RunTest("-maxdepth 0",
                FindTestPath.ExpectedDir(""),
                FindTestPath.File("root file"),
                FindTestPath.Dir("sub dir1"),
                FindTestPath.Dir("sub dir2"),
                FindTestPath.File("sub dir1", "another file"),
                FindTestPath.File("sub dir2", "another file4")
            );

            RunTest("-maxdepth 1",
                FindTestPath.ExpectedDir(""),
                FindTestPath.ExpectedFile("root file"),
                FindTestPath.ExpectedDir("sub dir1"),
                FindTestPath.ExpectedDir("sub dir2"),
                FindTestPath.File("sub dir1", "another file"),
                FindTestPath.File("sub dir2", "another file4")
            );

            RunTest("-maxdepth 2",
                FindTestPath.ExpectedDir(""),
                FindTestPath.ExpectedFile("root file"),
                FindTestPath.ExpectedDir("sub dir1"),
                FindTestPath.ExpectedDir("sub dir2"),
                FindTestPath.ExpectedFile("sub dir1", "another file"),
                FindTestPath.ExpectedFile("sub dir2", "another file4")
            );
        }

        [Test]
        public void Mmin()
        {
            var testfileset = new[] {
                FindTestPath.ExpectedDir(""),
                FindTestPath.ExpectedFile("New file"),
                new FindTestPath
                {
                    Path = "Old file",
                    FileType = FindTestPathType.File,
                    LastWriteTimeUtc = DateTime.UtcNow - TimeSpan.FromMinutes(10),
                }
            };

            RunTest("-mmin -5", testfileset);

            using var referenceFile = TempFile.Create();
            File.SetLastWriteTimeUtc(referenceFile, DateTime.UtcNow - TimeSpan.FromMinutes(5));
            RunTest($"-newer {referenceFile}", testfileset);
        }

        [Test]
        public void Amin()
        {
            var testfileset = new[] {
                FindTestPath.ExpectedDir(""),
                FindTestPath.ExpectedFile("New file"),
                new FindTestPath
                {
                    Path = "Old file",
                    FileType = FindTestPathType.File,
                    LastAccessTimeUtc = DateTime.UtcNow - TimeSpan.FromMinutes(10),
                }
            };

            RunTest("-amin -5", testfileset);

            using var referenceFile = TempFile.Create();
            File.SetLastWriteTimeUtc(referenceFile, DateTime.UtcNow - TimeSpan.FromMinutes(5));
            RunTest($"-anewer {referenceFile}", testfileset);
        }
    }
}
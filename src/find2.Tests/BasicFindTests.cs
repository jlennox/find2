using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace find2.Tests;

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

    private static void RunTestStdoutCompared(string args, params FindTestPath[] files)
    {
        RunTest(args.Split(' '), true, files);
    }

    private static void RunTestStdoutCompared(string[] args, params FindTestPath[] files)
    {
        RunTest(args, true, files);
    }

    private static void RunTest(string[] args, params FindTestPath[] files)
    {
        RunTest(args, false, files);
    }

    private static void RunTest(string args, params FindTestPath[] files)
    {
        RunTest(args.Split(' '), false, files);
    }

    private static void RunTest(string[] args, bool compareStdOut, FindTestPath[] files)
    {
        using var test = new FindTest(files);

        var combinedArgs = new List<string> { test.Root };
        combinedArgs.AddRange(args);

        string[] GetResults(string? engine)
        {
            var findArgs = new List<string>();
            if (engine != null)
            {
                findArgs.AddRange(["--engine", engine]);
            }
            findArgs.AddRange(combinedArgs);
            var findResults = new List<string>();
            using var findDotnet = new Find(ExpressionMatch.Build(findArgs.ToArray()));
            findDotnet.Matched += (_, fullPath) => {
                lock (findResults)
                {
                    findResults.Add(fullPath);
                }
            };
            findDotnet.Run();
            return findResults.ToArray();
        }

        var foundDefault = GetResults(null);
        var foundDotnet = GetResults("dotnet");

        CollectionAssert.AreEquivalent(test.Expected, foundDefault, "Failed default engine test vs expectations");
        CollectionAssert.AreEquivalent(test.Expected, foundDotnet, "Failed dotnet engine test vs expectations");

        if (_gnuFindPath == null)
        {
            throw new Exception("Unable to locate GNU find.");
        }

        var cliArguments = string.Join(' ', combinedArgs.Select(t => t.Contains(' ') ? $"\"{t}\"" : t));
        using var findProc = new Process {
            StartInfo = new ProcessStartInfo {
                FileName = _gnuFindPath,
                Arguments = cliArguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
            }
        };

        findProc.Start();

        var rawGnuFindOutput = findProc.StandardOutput.ReadToEnd()
            // Find very annoyingly returns results such as:
            // C:\Users\joe\AppData\Local\Temp\pzltuf3d.fz3/sub dir2/Im_found
            // This is a behavior we intentionally do not mimic.
            .Replace('/', '\\')
            .Trim();

        if (compareStdOut)
        {
            void CompareOutput(IEnumerable<string> args)
            {
                var rawProcessOutput = new StringWriter();
                Program.Run(combinedArgs.ToArray(), rawProcessOutput);
                var output = rawProcessOutput.ToString().Trim();
                if (rawGnuFindOutput != output)
                {
                    Console.Error.WriteLine("rawGnuFindOutput:");
                    Console.Error.WriteLine(rawGnuFindOutput);
                    Console.Error.WriteLine($"\n\nactual ({string.Join(" ", args)}):");
                    Console.Error.WriteLine(output);
                }
                Assert.AreEqual(rawGnuFindOutput, output);
            }

            CompareOutput(combinedArgs);
            CompareOutput(["--engine", "dotnet", ..combinedArgs]);

            return;
        }

        var gnuFindOutput = rawGnuFindOutput.Split('\n');
        var a = string.Join('\n', gnuFindOutput.OrderBy(t => t));
        var b = string.Join('\n', foundDefault.OrderBy(t => t));

        CollectionAssert.AreEquivalent(gnuFindOutput, foundDefault, "Failed default engine test vs GNU find");
    }

    private const string _foundItem = "Im_Found";

    [Test]
    public void TestSingleItemAtRootIsFound()
    {
        RunTest($"-name {_foundItem}",
            FindTestPath.ExpectedFile(_foundItem),
            FindTestPath.File("Im not found")
        );
    }

    [Test]
    public void TestMultipleItemsWithSomeDepth()
    {
        RunTest($"-name {_foundItem}",
            FindTestPath.ExpectedFile("sub dir1", _foundItem),
            FindTestPath.File("sub dir1", "also filename"),
            FindTestPath.ExpectedFile("sub dir2", _foundItem),
            FindTestPath.ExpectedFile(_foundItem)
        );
    }

    [Test]
    [TestCase("-name", _foundItem)]
    [TestCase("-name", "*Found")]
    [TestCase("-name", "Im*")]
    [TestCase("-name", "Im_*Found")]
    [TestCase("-name", "Im*und")]
    [TestCase("-iname", "im*und")]
    [TestCase("-regex", ".*Im.*und")]
    [TestCase("-regex", ".*Im...und")]
    [TestCase("-iregex", ".*im...und")]
    public void TestNamePatterns(string command, string pattern)
    {
        RunTest([command, pattern],
            FindTestPath.ExpectedFile("sub dir1", _foundItem),
            FindTestPath.File("sub dir1", "also filename"),
            FindTestPath.ExpectedFile("sub dir2", _foundItem),
            FindTestPath.ExpectedFile(_foundItem)
        );
    }

    [TestCase("-path", "*sub dir1*")]
    [TestCase("-ipath", "*sub DIR1*")]
    public void TestPathPatterns(string command, string pattern)
    {
        RunTest([command, pattern],
            FindTestPath.ExpectedDir("sub dir1"),
            FindTestPath.ExpectedFile("sub dir1", _foundItem),
            FindTestPath.ExpectedFile("sub dir1", "also filename"),
            FindTestPath.Dir("sub dir2"),
            FindTestPath.File("sub dir2", _foundItem),
            FindTestPath.File(_foundItem)
        );
    }

    [Test]
    public void EmptyRecursivelyReturnsAllFiles()
    {
        RunTest($"",
            FindTestPath.ExpectedDir(""),
            FindTestPath.ExpectedDir("sub dir1"),
            FindTestPath.ExpectedDir("sub dir2"),
            FindTestPath.ExpectedFile("sub dir1", _foundItem),
            FindTestPath.ExpectedFile("sub dir1", "also filename"),
            FindTestPath.ExpectedFile("sub dir2", _foundItem),
            FindTestPath.ExpectedFile(_foundItem)
        );
    }

    [Test]
    public void TypeCanDetectDirectoriesVsFiles()
    {
        RunTest("-type d",
            FindTestPath.ExpectedDir(""),
            FindTestPath.ExpectedDir("sub dir1"),
            FindTestPath.ExpectedDir("sub dir2"),
            FindTestPath.File("sub dir1", _foundItem),
            FindTestPath.File("sub dir1", "also filename"),
            FindTestPath.File("sub dir2", _foundItem),
            FindTestPath.File(_foundItem)
        );

        RunTest("-type f",
            FindTestPath.Dir(""),
            FindTestPath.Dir("sub dir1"),
            FindTestPath.Dir("sub dir2"),
            FindTestPath.ExpectedFile("sub dir1", _foundItem),
            FindTestPath.ExpectedFile("sub dir1", "also filename"),
            FindTestPath.ExpectedFile("sub dir2", _foundItem),
            FindTestPath.ExpectedFile(_foundItem)
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
                Expected = false,
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
                Expected = false,
            }
        };

        RunTest("-amin -5", testfileset);

        using var referenceFile = TempFile.Create();
        File.SetLastWriteTimeUtc(referenceFile, DateTime.UtcNow - TimeSpan.FromMinutes(5));
        RunTest($"-anewer {referenceFile}", testfileset);
    }

    [Test]
    public void Print0()
    {
        RunTestStdoutCompared($"-name {_foundItem} -print0",
            FindTestPath.ExpectedFile("sub dir1", _foundItem),
            FindTestPath.File("sub dir1", "also filename"),
            FindTestPath.ExpectedFile("sub dir2", _foundItem),
            FindTestPath.ExpectedFile(_foundItem)
        );
    }

    [Test]
    public void Empty()
    {
        RunTest("-empty",
            FindTestPath.Dir(""),
            FindTestPath.Dir("sub dir1"),
            FindTestPath.Dir("sub dir2"),
            FindTestPath.ExpectedDir("an empty sub dir"),
            FindTestPath.ExpectedFile(0, "sub dir1", "an empty file"),
            FindTestPath.File(5, "sub dir1", "file with 5 bytes"),
            FindTestPath.File(6, "sub dir2", "file with 6 bytes"),
            FindTestPath.File(4, "file with 4 bytes"),
            FindTestPath.ExpectedFile(0, "another empty file")
        );
    }

    [Test]
    // FIXME: Arg. For some reason directories using GNU find report different access times.
    // There's a bunch of things to test regarding time formatting. Single digit days of the
    // month for example. Write out separate AsciiDateTime tests.
    [TestCase("%a %t %p\\n")]
    // FIXME: Our %s returns 0 on directories, but that's not what GNU find does.
    [TestCase("%P %p %s\\n")]
    [TestCase("%u %U\\n")]
    [TestCase("%u %U\\n")]
    [TestCase(@"123:\123 10:\10 50:\50 73:\73\n")]
    [TestCase(@"should print S44: \12344")]
    [TestCase("\\a\\b\\f\\n\\r\\t\\v\\\\")]
    public void PrintF(string format)
    {
        RunTestStdoutCompared(["-type", "f", "-printf", format],
            // FindTestPath.ExpectedDir(""),
            FindTestPath.ExpectedFile(123123123, "some random file"),
            FindTestPath.ExpectedFile(20, "another random file"),
            FindTestPath.ExpectedFile("some random file 2")
        );
    }

    [Test]
    [TestCase("HIklM")]
    [TestCase("prST+X")]
    // TODO: Fix this case.
    // [TestCase("Z")]
    [TestCase("aAbBc")]
    [TestCase("dDFhj")]
    [TestCase("mUwW")]
    [TestCase("xyY")]
    public void PrintFDateTimes(string directives)
    {
        foreach (var command in "ABCT")
        {
            var formatString = string.Join(' ', directives.Select(t => $"{command}{t}:%{command}{t}"));
            RunTestStdoutCompared(["-type", "f", "-printf", formatString],
                // FindTestPath.ExpectedDir(""),
                FindTestPath.ExpectedFile(123123123, "some random file"),
                FindTestPath.ExpectedFile(20, "another random file"),
                FindTestPath.ExpectedFile("some random file 2")
            );
        }
    }
}
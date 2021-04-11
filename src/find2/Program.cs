using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;

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

    internal struct QueuedDir
    {
        public string ParentDirectory { get; set; }
        public string[] Paths { get; set; }
        public string Path { get; set; }
    }

    internal sealed class Find
    {
        public int ThreadCount = 8;

        private readonly ConcurrentQueue<QueuedDir> _queuedDir = new();
        private readonly ConcurrentQueue<string[]> _directories = new();
        private readonly string _match;
        private readonly CancellationTokenSource _cts = new();
        private readonly AutoResetEvent _mre = new(false);
        private int _waiting = 0;
        private readonly Thread[] _threads;
        private readonly AutoResetEvent _completedEvent = new(false);

        public event Action<string> Match;

        public Find(string match, IEnumerable<string> rootDirectories)
        {
            _match = match;

            _queuedDir.Enqueue(new QueuedDir
            {
                Paths = rootDirectories.ToArray()
            });

            _directories.Enqueue(rootDirectories.ToArray());
            _mre.Set();

            _threads = Enumerable.Range(0, ThreadCount)
                .Select(t => new Thread(SearchImplementation) {
                    Name = nameof(SearchImplementation),
                    IsBackground = true
                }).ToArray();
        }

        public void Run()
        {
            foreach (var thread in _threads)
            {
                thread.Start();
            }

            while (_waiting < ThreadCount)
            {
                _completedEvent.WaitOne();
            }

            _cts.Cancel();
        }

        private void SearchImplementation()
        {
            using FileSearch<WindowsFileEntry> search = WindowsFileSearch.IsSupported()
                ? new WindowsFileSearch()
                : new DotNetFileSearch();

            search.Initialize();

            var match = _match;
            var emptyStrings = new[] { "" };
            var dirsToCheck = new Stack<QueuedDir>();
            var subdirs = new List<string>();

            while (!_cts.IsCancellationRequested)
            {
                if (!_queuedDir.TryDequeue(out var newDirs))
                {
                    if (Interlocked.Increment(ref _waiting) == ThreadCount)
                    {
                        _completedEvent.Set();
                    }

                    _mre.WaitOne();
                    Interlocked.Decrement(ref _waiting);
                    continue;
                }

                dirsToCheck.Push(newDirs);

                var hasAddedSubdirs = false;

                while (dirsToCheck.Count > 0)
                {
                    var dir = dirsToCheck.Pop();
                    foreach (var pathx in dir.Paths ?? emptyStrings)
                    {
                        var path = dir.Paths == null
                            ? dir.Path
                            : pathx;

                        var results = search.GetContents(path);

                        while (results.MoveNext())
                        {
                            var entry = results.Current;
                            var innerHasAddedSubdirs = hasAddedSubdirs;

                            if (entry.IsDirectory)
                            {
                                if (!innerHasAddedSubdirs)
                                {
                                    hasAddedSubdirs = true;
                                    dirsToCheck.Push(new QueuedDir
                                    {
                                        Path = Path.Combine(path, entry.Name)
                                    });
                                }
                                else
                                {
                                    subdirs.Add(Path.Combine(path, entry.Name));
                                }
                            }
                            else
                            {
                                if (entry.Name == match)
                                {
                                    var fullPath = Path.Combine(path, entry.Name);
                                    Match?.Invoke(fullPath);
                                }
                            }

                            if (subdirs.Count > 0)
                            {
                                _queuedDir.Enqueue(new QueuedDir
                                {
                                    ParentDirectory = path,
                                    Paths = subdirs.ToArray()
                                });
                                subdirs.Clear();
                                _mre.Set();
                            }
                        }
                    }
                }
            }
        }
    }
}

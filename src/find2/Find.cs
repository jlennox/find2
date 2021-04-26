using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace find2
{
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
        private readonly FindArguments _arguments;
        private readonly CancellationTokenSource _cts = new();
        private readonly AutoResetEvent _availableWorkEvent = new(false);
        private readonly Thread[] _threads;
        private readonly AutoResetEvent _noWorkSignal = new(false);
        private int _noWorkWaitingCount = 0;

        public event Action<WindowsFileEntry, string> Match;

        public Find(FindArguments arguments)
        {
            _arguments = arguments;

            _queuedDir.Enqueue(new QueuedDir
            {
                Paths = new[] { arguments.Root }
            });

            _availableWorkEvent.Set();

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

            while (_noWorkWaitingCount < ThreadCount)
            {
                _noWorkSignal.WaitOne();
            }

            _cts.Cancel();
        }

        private void SearchImplementation()
        {
            using FileSearch<WindowsFileEntry> search = WindowsFileSearch.IsSupported()
                ? new WindowsFileSearch()
                : new DotNetFileSearch();

            search.Initialize();

            var match = _arguments.Match;
            var emptyStrings = new[] { "" };
            var dirsToCheck = new Stack<QueuedDir>();
            var subdirs = new List<string>();

            while (!_cts.IsCancellationRequested)
            {
                if (!_queuedDir.TryDequeue(out var newDirs))
                {
                    if (Interlocked.Increment(ref _noWorkWaitingCount) == ThreadCount)
                    {
                        _noWorkSignal.Set();
                    }

                    _availableWorkEvent.WaitOne();
                    Interlocked.Decrement(ref _noWorkWaitingCount);
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
                                if (match == null || match(entry))
                                {
                                    var fullPath = Path.Combine(path, entry.Name);
                                    Match?.Invoke(entry, fullPath);
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
                                _availableWorkEvent.Set();
                            }
                        }
                    }
                }
            }
        }
    }
}
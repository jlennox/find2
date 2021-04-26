using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using find2.Interop;

namespace find2
{
    internal sealed class Find : IDisposable
    {
        private readonly int _threadCount = Kernel32.NumPhysicalCores;
        private readonly ConcurrentQueue<QueuedDir> _queuedDir = new();
        private readonly FindArguments _arguments;
        private readonly CancellationTokenSource _cts = new();
        private readonly AutoResetEvent _availableWorkEvent = new(false);
        private readonly Thread[] _threads;
        private readonly ManualResetEvent _noWorkSignal = new(false);
        private int _noWorkWaitingCount = 0;

        // TODO: Move to ctor
        public event Action<WindowsFileEntry, string>? Match;

        private readonly struct QueuedDir
        {
            public string[]? Paths { get; init; }
            public string? Path { get; init; }
        }

        public Find(FindArguments arguments)
        {
            _arguments = arguments;

            _queuedDir.Enqueue(new QueuedDir
            {
                Paths = new[] { arguments.Root }
            });

            _availableWorkEvent.Set();

            _threads = Enumerable.Range(0, _threadCount)
                .Select(t => new Thread(SearchWorker) {
                    Name = $"{nameof(SearchWorker)}-{t}",
                    IsBackground = true
                }).ToArray();
        }

        public void Run()
        {
            // This feels sloppy.
            {
                var match = _arguments.Match;
                var rootEntry = new WindowsFileEntry {
                    IsDirectory = true,
                    Name = _arguments.Root
                };

                if (match == null || match(rootEntry))
                {
                    Match?.Invoke(rootEntry, _arguments.Root);
                }
            }

            foreach (var thread in _threads)
            {
                thread.Start();
            }

            _noWorkSignal.WaitOne();
            _cts.Cancel();

            foreach (var thread in _threads)
            {
                if (!thread.Join(TimeSpan.FromSeconds(10)))
                {
                    throw new Exception("Was unable to join thread. This is a code bug.");
                }
            }
        }

        private void SearchWorker()
        {
            using FileSearch<WindowsFileEntry> search = WindowsFileSearch.IsSupported()
                ? new WindowsFileSearch()
                : new DotNetFileSearch();

            search.Initialize();

            var match = _arguments.Match;
            var emptyPathsPlaceholder = new[] { "" };
            var dirsToCheck = new Stack<QueuedDir>();
            var subdirs = new List<string>();

            const int noWorkSignalIndex = 0;
            var waitEvents = new WaitHandle[] { _noWorkSignal, _availableWorkEvent };

            // Empties the local running list of subdirectories and signals there's work available.
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            void FlushSubdirs()
            {
                _queuedDir.Enqueue(new QueuedDir
                {
                    Paths = subdirs.ToArray()
                });
                subdirs.Clear();
                _availableWorkEvent.Set();
            }

            // Main work loop. Exists when all work is completed.
            while (!_cts.IsCancellationRequested)
            {
                // Attempt to perform the cheaper dequeue first instead of a possible system call via
                // [_availableWorkEvent] to check for work. Even if [_availableWorkEvent triggers, it's possible there
                // was a race that we lost and re-enter the wait.
                if (!_queuedDir.TryDequeue(out var newDirs))
                {
                    // If all threads are now waiting, signal to the controlling thread and other workers that all work
                    // has completed.
                    if (Interlocked.Increment(ref _noWorkWaitingCount) == _threadCount)
                    {
                        _noWorkSignal.Set();
                        return;
                    }

                    // Wait for new work to arrive or for the work completion signal to be sent.
                    if (WaitHandle.WaitAny(waitEvents) == noWorkSignalIndex)
                    {
                        return;
                    }

                    Interlocked.Decrement(ref _noWorkWaitingCount);
                    continue;
                }

                var hasAddedSubdirs = false;

                // [dirsToCheck] is the locally managed work queue. Each work item has the opportunity to add to it.
                dirsToCheck.Push(newDirs);

                while (dirsToCheck.Count > 0)
                {
                    var dir = dirsToCheck.Pop();
                    foreach (var pathx in dir.Paths ?? emptyPathsPlaceholder)
                    {
                        // This is super sloppy. Basically, sometimes we've got a single path, sometimes we've got an
                        // array of paths. We don't want to allocate an array for the single path to avoid unneeded
                        // GC overhead.
                        var path = dir.Paths == null
                            ? dir.Path!
                            : pathx;

                        var results = search.GetContents(path);

                        while (results.MoveNext())
                        {
                            var entry = results.Current;
                            var innerHasAddedSubdirs = hasAddedSubdirs;

                            if (entry.IsDirectory)
                            {
                                // The first additional work we come across, add it to this worker's internal queue
                                // instead of to the global queue. This prevents the need of costly thread
                                // synchronization via [_availableWorkEvent] and [_queuedDir].
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

                            if (match == null || match(entry))
                            {
                                var fullPath = Path.Combine(path, entry.Name);
                                Match?.Invoke(entry, fullPath);
                            }

                            // This code is weird and needs proof that it's doing anything productive.
                            // * It adds multiple paths to theoretically reduce thread synchronization.
                            // * It splits the work into smaller chunks to help split the workload up among the
                            //   different threads in more pathological cases. The number to split at is guessed atm.
                            // * This could use an array instead of a List if this indeed stays fixed after there's
                            //   proven improvements from this.
                            if (subdirs.Count >= 10)
                            {
                                FlushSubdirs();
                            }
                        }

                        // Flush any remaining to the worker threads.
                        if (subdirs.Count > 0)
                        {
                            FlushSubdirs();
                        }
                    }
                }
            }
        }

        public void Dispose()
        {
            _cts.TryDispose();
            _availableWorkEvent.TryDispose();
            _noWorkSignal.TryDispose();
        }
    }
}
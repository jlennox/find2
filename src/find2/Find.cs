using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using find2.Interop;

namespace find2;

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
    public event Action<IFileEntry, string>? Matched;

    private readonly struct QueuedDir
    {
        public string[]? Paths { get; init; }
        public string? Path { get; init; }
        public int Depth { get; init; }
    }

    public Find(FindArguments arguments)
    {
        _arguments = arguments;

        if (arguments.ThreadCount.HasValue)
        {
            _threadCount = arguments.ThreadCount.Value;
        }

        _queuedDir.Enqueue(new QueuedDir
        {
            Paths = new[] { arguments.Root },
            Depth = 1,
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
            var minDepth = _arguments.MinDepth;
            var rootEntry = new DotnetFileEntry(_arguments.Root, true);

            if (match(rootEntry) && (!minDepth.HasValue || 0 >= minDepth))
            {
                Matched?.Invoke(rootEntry, _arguments.Root);
            }
        }

        // Special case this so the more complex code doesn't need to jump through hoops to support the oddity.
        if (_arguments.MaxDepth == 0) return;

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
        using var search = _arguments.GetSearch();

        search.Initialize();

        var match = _arguments.Match;
        var maxDepth = _arguments.MaxDepth;
        var minDepth = _arguments.MinDepth;
        var emptyPathsPlaceholder = new[] { "" };
        var dirsToCheck = new Stack<QueuedDir>();
        var subdirs = new List<string>();

        const int noWorkSignalIndex = 0;
        var waitEvents = new WaitHandle[] { _noWorkSignal, _availableWorkEvent };

        // Empties the local running list of subdirectories and signals there's work available.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void FlushSubdirs(int depth)
        {
            _queuedDir.Enqueue(new QueuedDir
            {
                Paths = subdirs.ToArray(),
                Depth = depth + 1
            });
            subdirs.Clear();
            _availableWorkEvent.Set();
        }

        // Main work loop. Exits when all work is completed.
        while (!_cts.IsCancellationRequested)
        {
            // Attempt to perform the cheaper dequeue first instead of a possible system call via
            // [_availableWorkEvent] to check for work. Even if [_availableWorkEvent] triggers, it's possible there
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

                // It's possible to lose the [TryDequeue] race. So try again.
                continue;
            }

            var hasAddedSubdirs = false;

            // [dirsToCheck] is the locally managed work queue. Each work item has the opportunity to add to it.
            dirsToCheck.Push(newDirs);

            while (dirsToCheck.Count > 0)
            {
                var dir = dirsToCheck.Pop();
                var maxDepthCheckPasses = !maxDepth.HasValue || dir.Depth < maxDepth;
                var minDepthCheckPasses = !minDepth.HasValue || dir.Depth >= minDepth;

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

                        if (entry.IsDirectory && maxDepthCheckPasses)
                        {
                            // The first additional work we come across, add it to this worker's internal queue
                            // instead of to the global queue. This prevents the need of costly thread
                            // synchronization via [_availableWorkEvent] and [_queuedDir].
                            if (!innerHasAddedSubdirs)
                            {
                                hasAddedSubdirs = true;
                                dirsToCheck.Push(new QueuedDir
                                {
                                    Path = Path.Combine(path, entry.Name),
                                    Depth = dir.Depth + 1
                                });
                            }
                            else
                            {
                                subdirs.Add(Path.Combine(path, entry.Name));
                            }
                        }

                        if (match(entry) && minDepthCheckPasses)
                        {
                            var fullPath = Path.Combine(path, entry.Name);
                            Matched?.Invoke(entry, fullPath);
                        }

                        // This code is weird and needs proof that it's doing anything productive.
                        // * It adds multiple paths to theoretically reduce thread synchronization.
                        // * It splits the work into smaller chunks to help split the workload up among the
                        //   different threads in more pathological cases. The number to split at is guessed atm.
                        // * This could use an array instead of a List if this indeed stays fixed after there's
                        //   proven improvements from this.
                        if (subdirs.Count >= 10)
                        {
                            FlushSubdirs(dir.Depth);
                        }
                    }

                    // Flush any remaining to the worker threads.
                    if (subdirs.Count > 0)
                    {
                        FlushSubdirs(dir.Depth);
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
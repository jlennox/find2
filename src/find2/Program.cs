using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace find2
{
    internal class Program
    {
        private static void Main(string[] args)
        {
            var dir = Path.GetFullPath(args[1]);

            Console.WriteLine("Root:" + dir);
            var find = new Find(args[0], new[] { dir });
            find.Run();
        }
    }

    struct QueuedDir
    {
        public IntPtr ParentDirectory { get; set; }
        public string[] Paths { get; set; }
        public string Path { get; set; }
    }

    struct QueuedDirString
    {
        public string ParentDirectory { get; set; }
        public string[] Paths { get; set; }
        public string Path { get; set; }
    }

    internal unsafe class Find
    {
        private const int _threadCount = 8;
        private readonly ConcurrentQueue<QueuedDir> _queuedDirs = new();
        private readonly ConcurrentQueue<QueuedDirString> _queuedDirStrings = new();
        private readonly ConcurrentQueue<string[]> _directories = new();
        private readonly string _match;
        private readonly CancellationTokenSource _cts = new();
        private readonly AutoResetEvent _mre = new(false);
        private int _waiting = 0;
        private readonly Thread[] _threads;
        private readonly AutoResetEvent _completedEvent = new(false);

        public Find(string match, IEnumerable<string> rootDirectories)
        {

            _match = match;

            _queuedDirs.Enqueue(new QueuedDir
            {
                Paths = rootDirectories.ToArray()
            });

            _directories.Enqueue(rootDirectories.ToArray());
            _mre.Set();

            _threads = Enumerable.Range(0, _threadCount)
                .Select(t => new Thread(SearchImplementation) {
                    Name = nameof(DirectoryThread),
                    IsBackground = true
                }).ToArray();
        }

        public void Run()
        {
            foreach (var thread in _threads)
            {
                thread.Start();
            }

            while (_waiting < _threadCount)
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

            var match = _match;
            var emptyStrings = new[] { "" };
            var dirsToCheck = new Stack<QueuedDirString>();
            var subdirs = new List<string>();

            while (!_cts.IsCancellationRequested)
            {
                if (!_queuedDirStrings.TryDequeue(out var newDirs))
                {
                    if (Interlocked.Increment(ref _waiting) == _threadCount)
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

                        var max = 1;

                        var results = search.GetContents(path);

                        var passNumber = 0;

                        while (results.MoveNext())
                        {
                            var entry = results.Current;

                            if (++passNumber > max)
                            {
                                max = passNumber;
                                //Console.WriteLine("New max:" + max);
                            }

                            var innerHasAddedSubdirs = hasAddedSubdirs;

                            if (entry.IsDirectory)
                            {
                                if (!innerHasAddedSubdirs)
                                {
                                    hasAddedSubdirs = true;
                                    dirsToCheck.Push(new QueuedDirString
                                    {
                                        Path = entry.Name
                                    });
                                }
                                else
                                {
                                    subdirs.Add(entry.Name);
                                }
                            }
                            else
                            {
                                if (entry.Name == match)
                                {
                                    Console.WriteLine("!!Found:" + Path.Combine(path, entry.Name));
                                }
                            }


                            if (subdirs.Count > 0)
                            {
                                _queuedDirStrings.Enqueue(new QueuedDirString
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

        private void DirectoryThreadNT()
        {
            var match = _match;
            var emptyStrings = new[] { "" };
            var pageSize = Environment.SystemPageSize * 2;
            var bufferSize = pageSize * 100;
            var buffer = Marshal.AllocHGlobal(bufferSize);
            var dirsToCheck = new Stack<QueuedDir>();
            var subdirs = new List<string>();
            var max = 1;
            while (!_cts.IsCancellationRequested)
            {
                if (!_queuedDirs.TryDequeue(out var newDirs))
                {
                    if (Interlocked.Increment(ref _waiting) == _threadCount)
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

                        var dirHandle = dir.ParentDirectory == IntPtr.Zero
                            ? NtDll.CreateDirectoryHandle(path)
                            : NtDll.NtCreateDirectoryHandle(path, dir.ParentDirectory);

                        if (dirHandle == IntPtr.Zero)
                        {
                            Console.WriteLine("!!Error:" + dir.Path);
                            continue;
                        }

                        var passNumber = 0;

                        var usableBufferSize = pageSize;

                        while (true)
                        {
                            // Grow the buffer on additional passes.
                            // NtQueryDirectoryFile has costs that scale by page count due to ProbeForWrite.
                            // If everything fits in a single pass then the better off it all is.
                            usableBufferSize = Math.Min(usableBufferSize * 2, bufferSize);

                            var status = NtDll.NtQueryDirectoryFile(
                                dirHandle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero,
                                out _, buffer, (uint)usableBufferSize,
                                FILE_INFORMATION_CLASS.FileDirectoryInformation,
                                BOOLEAN.FALSE, null, BOOLEAN.FALSE);

                            if (status == StatusOptions.STATUS_NO_MORE_FILES)
                            {
                                break;
                            }

                            if (++passNumber > max)
                            {
                                max = passNumber;
                                //Console.WriteLine("New max:" + max);
                            }

                            var entry = (FILE_DIRECTORY_INFORMATION*)buffer;
                            var innerHasAddedSubdirs = hasAddedSubdirs;

                            while (entry != null)
                            {
                                if (entry->IsDirectoryEntry())
                                {
                                    entry = FILE_DIRECTORY_INFORMATION.GetNextInfo(entry);
                                    continue;
                                }

                                var filename = new string(entry->FileName);

                                if ((entry->FileAttributes & FileAttributes.Directory) != 0)
                                {
                                    if (!innerHasAddedSubdirs)
                                    {
                                        hasAddedSubdirs = true;
                                        dirsToCheck.Push(new QueuedDir
                                        {
                                            ParentDirectory = dirHandle,
                                            Path = filename
                                        });
                                    }
                                    else
                                    {
                                        subdirs.Add(filename);
                                    }
                                }
                                else
                                {
                                    if (filename == match)
                                    {
                                        Console.WriteLine("!!Found:" + Path.Combine(path, filename));
                                    }
                                }

                                entry = FILE_DIRECTORY_INFORMATION.GetNextInfo(entry);
                            }

                            if (subdirs.Count > 0)
                            {
                                _queuedDirs.Enqueue(new QueuedDir
                                {
                                    ParentDirectory = dirHandle,
                                    Paths = subdirs.ToArray()
                                });
                                subdirs.Clear();
                                _mre.Set();
                            }
                        }

                        // lol?
                        //NtDll.CloseHandle(dirHandle);
                    }
                }
            }

            Marshal.FreeHGlobal(buffer);
        }

        private void DirectoryThread()
        {
            var match = _match;
            const int bufferSize = 1024 * 1024 * 10;
            var buffer = Marshal.AllocHGlobal(bufferSize);
            var dirsToCheck = new Stack<string>();
            var subdirs = new List<string>();
            while (!_cts.IsCancellationRequested)
            {
                if (!_directories.TryDequeue(out var newDirs))
                {
                    if (Interlocked.Increment(ref _waiting) == _threadCount)
                    {
                        _completedEvent.Set();
                    }

                    _mre.WaitOne();
                    Interlocked.Decrement(ref _waiting);
                    continue;
                }

                foreach (var dir in newDirs) {
                    dirsToCheck.Push(dir);
                }

                var hasAddedSubdirs = false;

                while (dirsToCheck.Count > 0)
                {
                    var dir = dirsToCheck.Pop();
                    var dirHandle = NtDll.CreateDirectoryHandle(dir);
                    var status = NtDll.NtQueryDirectoryFile(
                        dirHandle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero,
                        out var statusBlock,
                        buffer, (uint)bufferSize,
                        FILE_INFORMATION_CLASS.FileDirectoryInformation,
                        BOOLEAN.FALSE, null, BOOLEAN.FALSE);

                    NtDll.CloseHandle(dirHandle);

                    var entry = (FILE_DIRECTORY_INFORMATION *)buffer;
                    // Blindly skip "." and ".." (may God help us all)
                    entry = FILE_DIRECTORY_INFORMATION.GetNextInfo(entry);
                    entry = FILE_DIRECTORY_INFORMATION.GetNextInfo(entry);

                    var innerHasAddedSubdirs = hasAddedSubdirs;

                    while (entry != null)
                    {
                        var filename = new string(entry->FileName);

                        if ((entry->FileAttributes & FileAttributes.Directory) != 0)
                        {
                            var fullDir = Path.Combine(dir, filename);
                            if (!innerHasAddedSubdirs)
                            {
                                hasAddedSubdirs = true;
                                dirsToCheck.Push(fullDir);
                            }
                            else
                            {
                                subdirs.Add(fullDir);
                            }
                        }
                        else
                        {
                            if (filename == match)
                            {
                                Console.WriteLine("!!Found:" + Path.Combine(dir, filename));
                            }
                        }

                        entry = FILE_DIRECTORY_INFORMATION.GetNextInfo(entry);
                    }

                    if (subdirs.Count > 0)
                    {
                        _directories.Enqueue(subdirs.ToArray());
                        subdirs.Clear();
                        _mre.Set();
                    }
                }
            }

            Marshal.FreeHGlobal(buffer);
        }

        private void DirectoryThreadDotNet()
        {
            var match = _match;
            string[] dirs = null;
            while (!_cts.IsCancellationRequested)
            {
                if (dirs == null)
                {
                    if (!_directories.TryDequeue(out var newDirs))
                    {
                        if (Interlocked.Increment(ref _waiting) == _threadCount)
                        {
                            _completedEvent.Set();
                        }

                        _mre.WaitOne();
                        Interlocked.Decrement(ref _waiting);
                        continue;
                    }

                    dirs = newDirs;
                }

                var dirsToCheck = dirs;
                dirs = null;

                foreach (var dir in dirsToCheck)
                {
                    var files = Directory.GetFiles(dir);

                    foreach (var file in files)
                    {
                        if (file.EndsWith(match))
                        {
                            Console.WriteLine("!!Found:" + file);
                        }
                    }

                    var subDirs = Directory.GetDirectories(dir);

                    if (subDirs.Length > 0)
                    {
                        if (_waiting == 0 && dirs == null)
                        {
                            dirs = subDirs;
                        }
                        else
                        {
                            _directories.Enqueue(subDirs);
                            _mre.Set();
                        }
                    }
                }
            }
        }
    }
}

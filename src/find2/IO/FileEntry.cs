using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using find2.Interop;

namespace find2.IO;

internal interface IFileEntry
{
    public bool IsDirectory { get; }
    public string Name { get; }
    public DateTime LastAccessTime { get; }
    public DateTime LastWriteTime { get; }
    public DateTime CreationTime { get; }
    public DateTime ChangeTime { get; }
    public long Size { get; }
    public string FullPath { get; }

    // NOTE: The abstraction here isn't exactly the best, because there's:
    // - DotnetFileEntry (Windows)
    // - DotnetFileEntry (POSIX)
    // - WindowsFileEntry.
    // We may want to have two DotnetFileEntry and drop these default implementations.
    public string OwnerUsername { get => FileInfoExtra.SystemInstance.GetOwnerUsername(this) ?? ""; }
    public int OwnerUserID { get => FileInfoExtra.SystemInstance.OwnerUserID(this) ?? 0; }

    public bool IsEmpty()
    {
        if (IsDirectory)
        {
            // TODO: This is extremely unoptimized. Have an abstract "IsDirectoryEmpty" for each type.
            return Directory.GetFiles(FullPath).Length == 0 &&
                Directory.GetDirectories(FullPath).Length == 0;
        }

        return Size == 0;
    }
}

internal sealed unsafe class WindowsFileEntry : IFileEntry
{
    public bool IsDirectory { get; private set; }
    public string Name { get; private set; }
    public DateTime LastAccessTime { get; private set; }
    public DateTime LastWriteTime { get; private set; }
    public DateTime CreationTime { get; private set; }
    public DateTime ChangeTime { get; private set; }
    public long Size { get; private set; }

    public string FullPath { get => _fullPath ??= Path.Combine(_directory!, Name); }

    private string? _fullPath;
    private string? _directory;

    // Because `IFileEntry` implementations are classes, they're reused to avoid excessive GC overhead.
    public void Set(string directory, FILE_DIRECTORY_INFORMATION* entry)
    {
        Name = new string(entry->FileName);
        IsDirectory = (entry->FileAttributes & FileAttributes.Directory) != 0;
        LastAccessTime = entry->LastAccessTime.ToDateTime();
        LastWriteTime = entry->LastWriteTime.ToDateTime();
        CreationTime = entry->CreationTime.ToDateTime();
        ChangeTime = entry->ChangeTime.ToDateTime();
        Size = entry->EndOfFile;
        _fullPath = null;
        _directory = directory;
    }
}

internal sealed class DotnetFileEntry : IFileEntry
{
    public bool IsDirectory { get; private set; }
    public string Name { get; private set; }
    public DateTime LastAccessTime => _fileInfo.Value.LastAccessTimeUtc;
    public DateTime LastWriteTime => _fileInfo.Value.LastWriteTimeUtc;
    public DateTime CreationTime => _fileInfo.Value.CreationTimeUtc;
    // HACK: Arg... there's no dotnet method of reading last change time.
    public DateTime ChangeTime => _fileInfo.Value.LastWriteTimeUtc;
    public long Size => IsDirectory ? 0 : _fileInfo.Value.Length;
    public string FullPath { get; private set; }

    // TODO: Benchmark that making this lazy is actually worthwhile.
    private Lazy<FileInfo> _fileInfo;

    public DotnetFileEntry() { }
    public DotnetFileEntry(string path, bool isDirectory)
    {
        Set(path, isDirectory);
    }

    public void Set(string path, bool isDirectory)
    {
        Name = Path.GetFileName(path);
        IsDirectory = isDirectory;
        FullPath = path;
        _fileInfo = new Lazy<FileInfo>(GetFileInfo, LazyThreadSafetyMode.None);
    }

    private FileInfo GetFileInfo() => new(FullPath);
}

internal abstract class FileSearch : IDisposable
{
    public abstract void Initialize();
    public abstract IEnumerator<IFileEntry> GetContents(string directory);
    public abstract void Dispose();
}
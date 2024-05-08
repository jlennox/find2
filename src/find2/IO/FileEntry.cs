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
    public long Size { get; }
}

internal sealed unsafe class WindowsFileEntry : IFileEntry
{
    public bool IsDirectory { get; private set; }
    public string Name { get; private set; }
    public DateTime LastAccessTime { get; private set; }
    public DateTime LastWriteTime { get; private set; }
    public long Size { get; private set; }

    // Because `IFileEntry` implementations are classes, they're reused to avoid excessive GC overhead.
    public void Set(FILE_DIRECTORY_INFORMATION* entry)
    {
        Name = new string(entry->FileName);
        IsDirectory = (entry->FileAttributes & FileAttributes.Directory) != 0;
        LastAccessTime = entry->LastAccessTime.ToDateTime();
        LastWriteTime = entry->LastWriteTime.ToDateTime();
        Size = entry->EndOfFile;
    }
}

internal sealed class DotnetFileEntry : IFileEntry
{
    public bool IsDirectory { get; private set; }
    public string Name { get; private set; }
    public DateTime LastAccessTime => _fileInfo.Value.LastAccessTimeUtc;
    public DateTime LastWriteTime => _fileInfo.Value.LastWriteTimeUtc;
    public long Size => IsDirectory ? 0 : _fileInfo.Value.Length;

    private string _fullPath;
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
        _fullPath = path;
        _fileInfo = new Lazy<FileInfo>(GetFileInfo, LazyThreadSafetyMode.None);
    }

    private FileInfo GetFileInfo() => new(_fullPath);
}

internal abstract class FileSearch : IDisposable
{
    public abstract void Initialize();
    public abstract IEnumerator<IFileEntry> GetContents(string directory);
    public abstract void Dispose();
}
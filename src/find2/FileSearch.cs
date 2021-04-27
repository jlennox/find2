using System;
using System.Collections.Generic;
using System.IO;
using find2.Interop;

namespace find2
{
    internal interface IFileEntry
    {
        public bool IsDirectory { get; }
        string Name { get; }
    }

    internal readonly unsafe struct WindowsFileEntry : IFileEntry
    {
        public bool IsDirectory { get; init; }
        public string Name { get; init; }
        public DateTime LastAccessTime { get; init; }
        public DateTime LastWriteTime { get; init; }
        public long Size { get; init; }

        public WindowsFileEntry(FILE_DIRECTORY_INFORMATION* entry)
        {
            Name = new string(entry->FileName);
            IsDirectory = (entry->FileAttributes & FileAttributes.Directory) != 0;
            LastAccessTime = entry->LastAccessTime.ToDateTime();
            LastWriteTime = entry->LastWriteTime.ToDateTime();
            Size = entry->EndOfFile;
        }

        public WindowsFileEntry(string path)
        {
            Name = Path.GetFileName(path);
            var fileinfo = new FileInfo(path);
            IsDirectory = (fileinfo.Attributes & FileAttributes.Directory) != 0;
            LastAccessTime = fileinfo.LastAccessTimeUtc;
            LastWriteTime = fileinfo.LastWriteTimeUtc;
            Size = IsDirectory ? 0 : fileinfo.Length; // make this lazy!
        }
    }

    internal abstract class FileSearch<T> : IDisposable
        where T : IFileEntry
    {
        public abstract void Initialize();
        public abstract IEnumerator<WindowsFileEntry> GetContents(string directory);
        public abstract void Dispose();
    }
}
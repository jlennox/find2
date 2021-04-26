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
        public DateTime LastAccessed { get; init; }
        public long FileSize { get; init; }

        public WindowsFileEntry(FILE_DIRECTORY_INFORMATION* entry)
        {
            IsDirectory = (entry->FileAttributes & FileAttributes.Directory) != 0;
            Name = new string(entry->FileName);
            LastAccessed = entry->LastAccessTime.ToDateTime();
            FileSize = entry->EndOfFile;
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
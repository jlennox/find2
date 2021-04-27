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
            Name = new string(entry->FileName);
            IsDirectory = (entry->FileAttributes & FileAttributes.Directory) != 0;
            LastAccessed = entry->LastAccessTime.ToDateTime();
            FileSize = entry->EndOfFile;
        }

        public WindowsFileEntry(string path)
        {
            Console.WriteLine("Checking for:" + path);
            Name = Path.GetFileName(path);
            var fileinfo = new FileInfo(path);
            IsDirectory = (fileinfo.Attributes & FileAttributes.Directory) != 0;
            LastAccessed = fileinfo.LastAccessTimeUtc;
            FileSize = IsDirectory ? 0 : fileinfo.Length; // make this lazy!
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
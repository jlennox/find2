using System;
using System.Collections.Generic;

namespace find2
{
    internal interface IFileEntry
    {
        public bool IsDirectory { get; }
        string Name { get; }
    }

    internal struct WindowsFileEntry : IFileEntry
    {
        public bool IsDirectory { get; }
        public string Name { get; }

        public WindowsFileEntry(bool isDirectory, string name)
        {
            IsDirectory = isDirectory;
            Name = name;
        }
    }

    internal abstract class FileSearch<T> : IDisposable
        where T : IFileEntry
    {
        public abstract void Initialize();
        public abstract IEnumerator<IFileEntry> GetContents(string directory);
        public abstract void Dispose();
    }
}
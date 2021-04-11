using System;
using System.Collections.Generic;
using System.IO;

namespace find2.Tests
{
    internal enum FindTestPathType
    {
        Directory,
        File
    }

    internal struct FindTestPath
    {
        public FindTestPathType FileType { get; set; }
        public string Path { get; set; }

        public static FindTestPath File(params string[] paths)
        {
            return new() { FileType = FindTestPathType.File, Path = System.IO.Path.Combine(paths) };
        }

        public static FindTestPath Dir(params string[] paths)
        {
            return new() { FileType = FindTestPathType.Directory, Path = System.IO.Path.Combine(paths) };
        }

        internal void Create(string root)
        {
            var name = System.IO.Path.Combine(root, Path);
            switch (FileType)
            {
                case FindTestPathType.Directory:
                    Directory.CreateDirectory(name);
                    break;
                case FindTestPathType.File:
                    var dir = System.IO.Path.GetDirectoryName(name);
                    Directory.CreateDirectory(dir);
                    System.IO.File.WriteAllBytes(name, Array.Empty<byte>());
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
    }

    // TODO: Error handling.
    // TODO: Method of normalizing path separators.
    internal sealed class FindTest : IDisposable
    {
        public readonly string Root;

        public FindTest(IEnumerable<FindTestPath> files)
        {
            Root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(Root);

            foreach (var file in files)
            {
                file.Create(Root);
            }
        }

        public string Combine(params string[] paths)
        {
            return Path.Combine(Root, Path.Combine(paths));
        }

        public void Dispose()
        {
            Directory.Delete(Root, true);
        }
    }
}
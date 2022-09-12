using System;
using System.Collections.Generic;
using System.IO;

namespace find2.Tests;

internal enum FindTestPathType
{
    Directory,
    File
}

internal struct FindTestPath
{
    public FindTestPathType FileType { get; init; }
    public string Path { get; init; }
    public bool Expected { get; init; }
    public DateTime? LastAccessTimeUtc { get; init; }
    public DateTime? LastWriteTimeUtc { get; init; }
    public DateTime? CreationTimeUtc { get; init; }
    public long Size { get; init; }

    public static FindTestPath File(long size, params string[] paths)
    {
        return new() { Size = size, FileType = FindTestPathType.File, Path = System.IO.Path.Combine(paths) };
    }

    public static FindTestPath ExpectedFile(long size, params string[] paths)
    {
        return new() { Size = size, FileType = FindTestPathType.File, Path = System.IO.Path.Combine(paths), Expected = true };
    }

    public static FindTestPath File(params string[] paths) => File(0, paths);
    public static FindTestPath ExpectedFile(params string[] paths) => ExpectedFile(0, paths);

    public static FindTestPath Dir(params string[] paths)
    {
        return new() { FileType = FindTestPathType.Directory, Path = System.IO.Path.Combine(paths) };
    }

    public static FindTestPath ExpectedDir(params string[] paths)
    {
        return new() { FileType = FindTestPathType.Directory, Path = System.IO.Path.Combine(paths), Expected = true };
    }

    internal void Create(string root)
    {
        var name = System.IO.Path.Combine(root, Path);
        switch (FileType)
        {
            case FindTestPathType.Directory:
                Directory.CreateDirectory(name);
                if (LastAccessTimeUtc.HasValue) Directory.SetLastAccessTimeUtc(name, LastAccessTimeUtc.Value);
                if (LastWriteTimeUtc.HasValue) Directory.SetLastWriteTimeUtc(name, LastWriteTimeUtc.Value);
                if (CreationTimeUtc.HasValue) Directory.SetCreationTimeUtc(name, CreationTimeUtc.Value);
                break;
            case FindTestPathType.File:
                var dir = System.IO.Path.GetDirectoryName(name);
                Directory.CreateDirectory(dir!);
                System.IO.File.WriteAllBytes(name, new byte[Size]);

                if (LastAccessTimeUtc.HasValue) System.IO.File.SetLastAccessTimeUtc(name, LastAccessTimeUtc.Value);
                if (LastWriteTimeUtc.HasValue) System.IO.File.SetLastWriteTimeUtc(name, LastWriteTimeUtc.Value);
                if (CreationTimeUtc.HasValue) System.IO.File.SetCreationTimeUtc(name, CreationTimeUtc.Value);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(FileType), FileType, "Unknown FileType.");
        }
    }
}

// TODO: Error handling.
// TODO: Method of normalizing path separators.
internal sealed class FindTest : IDisposable
{
    public readonly string Root;
    public readonly List<string> Expected = new();

    public FindTest(IEnumerable<FindTestPath> files)
    {
        Root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(Root);

        foreach (var file in files)
        {
            file.Create(Root);

            if (file.Expected) Expected.Add(Combine(file.Path));
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

internal struct TempFile : IDisposable
{
    public string Path { get; init; }

    public static implicit operator string(TempFile d) => d.Path;

    public static TempFile Create()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.IO.Path.GetTempFileName());
        File.WriteAllBytes(path, Array.Empty<byte>());

        return new TempFile {
            Path = path
        };
    }

    public void Dispose()
    {
        try
        {
            File.Delete(Path);
        }
        catch { }
    }

    public override string ToString() => Path;
}
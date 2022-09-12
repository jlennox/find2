using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System;
using find2.Interop;

namespace find2;

internal sealed class BufferSizeStatistic
{
    public int Min { get; init; }
    public int Max { get; init; }
    public int Mean { get; init; }
    public int Average { get; init; }
    public int Count { get; init; }

    private BufferSizeStatistic(List<int> dirSizes)
    {
        var sorted = dirSizes.OrderByDescending(t => t).ToArray();
        if (sorted.Length == 0) return;

        Count = sorted.Length;
        Min = sorted.Last();
        Max = sorted.First();
        Average = sorted.Sum() / sorted.Length;
        Mean = sorted[sorted.Length / 2];
    }

    public static BufferSizeStatistic Create(FindArguments arguments)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new BufferSizeStatistic(WindowsFileBufferSizeStatistic.GetDirectorySizes(arguments));
        }

        throw new Exception("Operating system not supported for BufferSizeStatistic");
    }
}

internal unsafe static class WindowsFileBufferSizeStatistic
{
    public static List<int> GetDirectorySizes(FindArguments arguments)
    {
        var bufferSize = 10 * 1024 * 1024;
        var buffer = Marshal.AllocHGlobal(bufferSize);
        var directories = new Stack<string>(new[] { arguments.Root });
        var span = new Span<byte>((void*)buffer, bufferSize);
        ReadOnlySpan<byte> searchPattern = new byte[] { 0xC7, 0xC7, 0xC7, 0xC7, 0xC7, 0xC7, 0xC7, 0xC7, 0xC7 };

        // Ya, buffering all of these up into a list instead of using a proper histogram isn't the best, but this
        // is a diagnotic level code path.
        var dirSizes = new List<int>();

        while (directories.Count > 0)
        {
            var directory = directories.Pop();
            span.Fill(0xC7);
            var handle = NtDll.CreateDirectoryHandle(directory);
            if (handle == NtDll.INVALID_HANDLE_VALUE)
            {
                Console.WriteLine("Couldn't open " + directory);
                continue;
            }

            var status = NtDll.NtQueryDirectoryFile(
                handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero,
                out _, buffer, (uint)bufferSize,
                FILE_INFORMATION_CLASS.FileDirectoryInformation,
                BOOLEAN.FALSE, null, BOOLEAN.FALSE);

            if (status != StatusOptions.STATUS_SUCCESS)
            {
                Console.WriteLine("Couldn't read " + directory);
                NtDll.CloseHandle(handle);
                continue;
            }

            NtDll.CloseHandle(handle);

            var current = (FILE_DIRECTORY_INFORMATION*)buffer;
            while (current != null)
            {
                if (!current->IsParentDirectoryEntry() && current->FileAttributes.HasFlag(FileAttributes.Directory))
                {
                    directories.Push(Path.Combine(directory, new string(current->FileName)));
                }

                current = FILE_DIRECTORY_INFORMATION.GetNextInfo(current);
            }

            // This isn't the smartest method. We could count the entries provided from above.
            var size = span.IndexOf(searchPattern);

            // Should never realistically happen.
            if (size == -1) throw new IndexOutOfRangeException();

            dirSizes.Add(size);
        }

        return dirSizes;
    }
}
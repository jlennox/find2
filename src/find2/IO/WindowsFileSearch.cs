using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using find2.Interop;

namespace find2.IO;

internal sealed class WindowsFileSearch : FileSearch
{
    public static bool IsSupported() => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    private readonly WindowsFileSearchBuffer _buffer = new();

    public override void Initialize()
    {
    }

    public override IEnumerator<WindowsFileEntry> GetContents(string directory)
    {
        return new WindowsFileSearchEnumerator(directory, _buffer);
    }

    public override void Dispose()
    {
        _buffer.TryDispose();
    }
}

internal sealed class WindowsFileSearchBuffer : IDisposable
{
    private static readonly int _pageSize = Environment.SystemPageSize * 2;
    private static readonly int _bufferSize = _pageSize * 100;

    public nint Buffer => _buffer;
    public int BufferSize => _usableBufferSize;

    private nint _buffer;
    private int _usableBufferSize;

    public WindowsFileSearchBuffer()
    {
        _buffer = Marshal.AllocHGlobal(_bufferSize);
        Reset();
    }

    public void Increase()
    {
        // Grow the buffer on additional passes.
        // NtQueryDirectoryFile has costs that scale by page count due to ProbeForWrite.
        // This means the buffer should be as small as possible, but not too small. Too
        // small and the amount of system calls hurts performance. Too large and the
        // number of ProbeForWrite hurt performance.
        _usableBufferSize = Math.Min(_usableBufferSize * 2, _bufferSize);
    }

    public void Reset()
    {
        _usableBufferSize = _pageSize;
    }

    public void Dispose()
    {
        var buffer = Interlocked.Exchange(ref _buffer, nint.Zero);

        if (buffer != nint.Zero)
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}

internal unsafe struct WindowsFileSearchEnumerator : IEnumerator<WindowsFileEntry>
{
    private nint _handle;
    private readonly WindowsFileSearchBuffer _buffer;
    private bool _hasFinished;
    private FILE_DIRECTORY_INFORMATION* _current;
    private readonly WindowsFileEntry _entry;
    private readonly string _directory;

    public WindowsFileSearchEnumerator(string directory, WindowsFileSearchBuffer buffer)
    {
        _handle = NtDll.CreateDirectoryHandle(directory);
        _buffer = buffer;
        _hasFinished = false;
        _current = null;
        _entry = new WindowsFileEntry();
        _directory = directory;

        buffer.Reset();

        if (_handle == nint.Zero)
        {
            // TODO:
            Console.WriteLine("!!Error:" + directory);
        }
    }

    public bool MoveNext()
    {
        if (_handle == nint.Zero || _hasFinished)
        {
            return false;
        }

        if (_current != null)
        {
            _current = FILE_DIRECTORY_INFORMATION.GetNextInfo(_current);
        }

        if (_current == null)
        {
            _buffer.Increase();

            var status = NtDll.NtQueryDirectoryFile(
                _handle, nint.Zero, nint.Zero, nint.Zero,
                out _, _buffer.Buffer, (uint)_buffer.BufferSize,
                FILE_INFORMATION_CLASS.FileDirectoryInformation,
                BOOLEAN.FALSE, null, BOOLEAN.FALSE);

            if (status == StatusOptions.STATUS_NO_MORE_FILES)
            {
                _hasFinished = true;
                Dispose();
                return false;
            }

            _current = (FILE_DIRECTORY_INFORMATION*)_buffer.Buffer;

            while (true)
            {
                // Slip past "." and ".." entries.
                // NOTE: Uh, is this `if` inverted...?
                if (!_current->IsParentDirectoryEntry()) break;

                _current = FILE_DIRECTORY_INFORMATION.GetNextInfo(_current);

                if (_current == null)
                {
                    return MoveNext();
                }
            }
        }

        return true;
    }

    // TODO
    public void Reset() { }

    public WindowsFileEntry? Current
    {
        get
        {
            if (_current == null) return default;

            _entry.Set(_directory, _current);
            return _entry;
        }
    }

    object IEnumerator.Current => Current;

    public void Dispose()
    {
        var handle = Interlocked.Exchange(ref _handle, nint.Zero);

        if (handle != nint.Zero)
        {
            NtDll.CloseHandle(handle);
        }
    }
}

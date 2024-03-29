﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using find2.Interop;

namespace find2;

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

    public IntPtr Buffer => _buffer;
    public int BufferSize => _usableBufferSize;

    private IntPtr _buffer;
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
        var buffer = Interlocked.Exchange(ref _buffer, IntPtr.Zero);

        if (buffer != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}

internal unsafe struct WindowsFileSearchEnumerator : IEnumerator<WindowsFileEntry>
{
    private IntPtr _handle;
    private readonly WindowsFileSearchBuffer _buffer;
    private bool _hasFinished;
    private FILE_DIRECTORY_INFORMATION* _current;
    private readonly WindowsFileEntry _entry;

    public WindowsFileSearchEnumerator(string directory, WindowsFileSearchBuffer buffer)
    {
        _handle = NtDll.CreateDirectoryHandle(directory);
        _buffer = buffer;
        _hasFinished = false;
        _current = null;
        _entry = new WindowsFileEntry();

        buffer.Reset();

        if (_handle == IntPtr.Zero)
        {
            Console.WriteLine("!!Error:" + directory);
        }
    }

    public bool MoveNext()
    {
        if (_handle == IntPtr.Zero || _hasFinished)
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
                _handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero,
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

            _entry.Set(_current);
            return _entry;
        }
    }

    object IEnumerator.Current => Current;

    public void Dispose()
    {
        var handle = Interlocked.Exchange(ref _handle, IntPtr.Zero);

        if (handle != IntPtr.Zero)
        {
            NtDll.CloseHandle(handle);
        }
    }
}

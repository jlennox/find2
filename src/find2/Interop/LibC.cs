using System;
using System.Runtime.InteropServices;

namespace find2.Interop;

[StructLayout(LayoutKind.Sequential, Pack = 8)]
internal struct Stat
{
    public ulong st_dev;     // ID of device containing file
    public ulong st_ino;     // Inode number
    public uint st_mode;     // File type and mode
    public uint st_nlink;    // Number of hard links
    public uint st_uid;      // User ID of owner
    public uint st_gid;      // Group ID of owner
    public ulong st_rdev;    // Device ID (if special file)
    public long __pad1;
    public long st_size;     // Total size, in bytes
    public uint st_blksize;  // Block size for filesystem I/O
    public int __pad2;
    public long st_blocks;   // Number of 512B blocks allocated
    public StatTimespec st_atim; // Last access time
    public StatTimespec st_mtim; // Last modification time
    public StatTimespec st_ctim; // Last status change time
    public int __pad3;
}

[StructLayout(LayoutKind.Sequential)]
internal struct StatTimespec
{
    public long tv_sec;  // Seconds
    public long tv_nsec; // Nanoseconds
}

[StructLayout(LayoutKind.Sequential)]
internal struct Passwd
{
    public IntPtr pw_name;       // Username
    public IntPtr pw_passwd;     // User password
    public uint pw_uid;          // User ID
    public uint pw_gid;          // Group ID
    public IntPtr pw_gecos;      // Real name
    public IntPtr pw_dir;        // Home directory
    public IntPtr pw_shell;      // Shell program
}

internal static unsafe partial class LibC
{
    [LibraryImport(Libraries.LibC, SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    private static partial int stat(string path, out Stat buf);

    [LibraryImport(Libraries.LibC, SetLastError = true)]
    private static partial Passwd* getpwuid(uint uid);

    static LibC()
    {
        if (sizeof(Stat) != 128) throw new InvalidProgramException($"{nameof(Stat)} does not align to expectations. Got {sizeof(Stat)}.");
    }

    // TODO: Exception or return null?
    public static int? GetOwnerUserId(string fullpath)
    {
        if (stat(fullpath, out var stats) != 0)
        {
            // throw new Exception($"Error getting file stats '{fullpath}'.");
            return null;
        }

        return (int)stats.st_uid;
    }

    public static string? GetOnwerUsername(string fullpath)
    {
        var ownerId = GetOwnerUserId(fullpath);
        if (ownerId == null) return null;

        var passwdPtr = getpwuid((uint)ownerId);
        if (new IntPtr(passwdPtr) == IntPtr.Zero || passwdPtr->pw_name == IntPtr.Zero)
        {
            // throw new Exception($"Error getting owner info'{fullpath}'.");
            return null;
        }

        return Marshal.PtrToStringAnsi(passwdPtr->pw_name);
    }
}


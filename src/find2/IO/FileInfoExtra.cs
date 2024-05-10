using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using find2.Interop;

namespace find2.IO;

// TODO: This is all very poorly optimized. Most of it is rarely used but if that changes there's big room for
// improvements here.
internal abstract class FileInfoExtra
{
    public static readonly FileInfoExtra SystemInstance = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
        ? WindowsFileInfoExtra.Instance
        : PosixFileInfoExtra.Instance;

    public abstract string? GetOwnerUsername(IFileEntry entry);
    public abstract int? OwnerUserID(IFileEntry entry);
}

internal sealed class WindowsFileInfoExtra : FileInfoExtra
{
    public static readonly WindowsFileInfoExtra Instance = new();

    [ThreadStatic]
    private static byte[]? _reusedSidBuffer;

    public override string? GetOwnerUsername(IFileEntry entry)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) throw new PlatformNotSupportedException();

        var security = new FileSecurity(entry.FullPath, AccessControlSections.Owner);
        var sid = security.GetOwner(typeof(SecurityIdentifier));
        if (sid == null) return "";

        var account = sid.Translate(typeof(NTAccount));
        var accountName = account.ToString();
        if (string.IsNullOrEmpty(accountName)) return "";

        var domainSplitIndex = accountName.IndexOf('\\');
        if (domainSplitIndex == -1) return accountName;
        if (domainSplitIndex == accountName.Length - 1) return accountName;
        return accountName[(domainSplitIndex + 1)..];
    }

    private static int ReadLastInt(SecurityIdentifier sid)
    {
        if (sid.BinaryLength < 4) return 0;
        return ReadBuf<int>(sid, sid.BinaryLength - 4);
    }

    private static T ReadBuf<T>(SecurityIdentifier sid, int offset)
    {
        _reusedSidBuffer ??= new byte[SecurityIdentifier.MaxBinaryLength];

        sid.GetBinaryForm(_reusedSidBuffer, 0);
        ref byte lastFour = ref Unsafe.Add(ref _reusedSidBuffer[0], offset);
        return Unsafe.As<byte, T>(ref lastFour);
    }

    public override int? OwnerUserID(IFileEntry entry)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) throw new PlatformNotSupportedException();

        // Based on  https://cygwin.com/cygwin-ug-net/ntsec.html#ntsec-mapping
        // Implementing this based on the documentation alone seems impossible.
        // For example, how do we tell the difference between these two types?
        // -   S-1-5-21-X-Y-Z-RID                   <=> uid/gid: 0x30000 + RID
        // -   S-1-5-21-X-Y-Z-RID                   <=> uid/gid: 0x100000 + RID
        // FIXME: Find where this happens in cygwin's source.
        // We'll want to keep this as handling the binary and not the string as possible.

        var security = new FileSecurity(entry.FullPath, AccessControlSections.Owner);
        var sid = security.GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier;
        if (sid == null) return 0;

        // Likely the most common? I don't know if this is exactly correct tho...
        // Local machine account in the form of: S-1-5-21-X-Y-Z-RID                   <=> uid/gid: 0x30000 + RID
        if (sid.BinaryLength == 0x1c)
        {
            var rid = ReadLastInt(sid);
            return rid + 0x30000;
        }

        var value = sid.Value;
        switch (value)
        {
            case "S-1-5-18": return 18;
            case "S-1-5-32-545": return 545;
        };

        return null;
    }
}

internal sealed class PosixFileInfoExtra : FileInfoExtra
{
    public static readonly PosixFileInfoExtra Instance = new();

    public override string? GetOwnerUsername(IFileEntry entry)
    {
        return LibC.GetOnwerUsername(entry.FullPath);
    }

    public override int? OwnerUserID(IFileEntry entry)
    {
        return LibC.GetOwnerUserId(entry.FullPath);
    }
}
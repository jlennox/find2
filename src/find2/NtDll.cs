using System;
using System.IO;
using System.Runtime.InteropServices;

namespace find2
{
    internal static class Libraries
    {
        internal const string Kernel32 = "kernel32.dll";
        internal const string NtDll = "ntdll.dll";
    }

    public struct LongFileTime
    {
#pragma warning disable CS0649
        /// <summary>
        /// 100-nanosecond intervals (ticks) since January 1, 1601 (UTC).
        /// </summary>
        internal long TicksSince1601;
#pragma warning restore CS0649

        internal DateTimeOffset ToDateTimeOffset() => new(DateTime.FromFileTimeUtc(TicksSince1601));
    }


    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public unsafe struct FILE_DIRECTORY_INFORMATION
    {
        /// <summary>
        /// Offset in bytes of the next entry, if any.
        /// </summary>
        public uint NextEntryOffset;

        /// <summary>
        /// Byte offset within the parent directory, undefined for NTFS.
        /// </summary>
        public uint FileIndex;
        public LongFileTime CreationTime;
        public LongFileTime LastAccessTime;
        public LongFileTime LastWriteTime;
        public LongFileTime ChangeTime;
        public long EndOfFile;
        public long AllocationSize;

        /// <summary>
        /// File attributes.
        /// </summary>
        /// <remarks>
        /// Note that MSDN documentation isn't correct for this- it can return
        /// any FILE_ATTRIBUTE that is currently set on the file, not just the
        /// ones documented.
        /// </remarks>
        public FileAttributes FileAttributes;

        /// <summary>
        /// The length of the file name in bytes (without null).
        /// </summary>
        public uint FileNameLength;

        /// <summary>
        /// The extended attribute size OR the reparse tag if a reparse point.
        /// </summary>
        //public uint EaSize;

        public char _fileName;

        public ReadOnlySpan<char> FileName
        {
            get
            {
                fixed (char* c = &_fileName)
                {
                    return new ReadOnlySpan<char>(c, (int)FileNameLength / sizeof(char));
                }
            }
        }

        /// <summary>
        /// Gets the next info pointer or null if there are no more.
        /// </summary>
        public static FILE_DIRECTORY_INFORMATION* GetNextInfo(FILE_DIRECTORY_INFORMATION* info)
        {
            if (info == null) return null;

            var nextOffset = info->NextEntryOffset;
            if (nextOffset == 0) return null;

            return (FILE_DIRECTORY_INFORMATION*)((byte*)info + nextOffset);
        }

        // Returns true if the filename is '.' or '..' and is a directory.
        public bool IsParentDirectoryEntry()
        {
            // Must be a directory.
            if ((FileAttributes & FileAttributes.Directory) == 0) return false;

            // Must be a file length of 1 or 2.
            if (FileNameLength > sizeof(char) * 2) return false;

            // Must start with a '.' character.
            if (_fileName != '.') return false;

            // Matched '.'
            if (FileNameLength == sizeof(char))
            {
                return true;
            }

            // Matched '..'
            fixed (char* filenamePtr = &_fileName)
            {
                if (filenamePtr[1] == '.')
                {
                    return true;
                }
            }

            return false;
        }
    }

    // https://github.com/dotnet/runtime/search?q=NtQueryDirectoryFile
    internal partial class NtDll
    {
        // https://msdn.microsoft.com/en-us/library/windows/hardware/ff556633.aspx
        // https://msdn.microsoft.com/en-us/library/windows/hardware/ff567047.aspx
        [DllImport(Libraries.NtDll, CharSet = CharSet.Unicode, ExactSpelling = true)]
        public static extern unsafe uint NtQueryDirectoryFile(
            IntPtr FileHandle,
            IntPtr Event,
            IntPtr ApcRoutine,
            IntPtr ApcContext,
            out IO_STATUS_BLOCK IoStatusBlock,
            IntPtr FileInformation,
            uint Length,
            FILE_INFORMATION_CLASS FileInformationClass,
            BOOLEAN ReturnSingleEntry,
            UNICODE_STRING* FileName,
            BOOLEAN RestartScan);
    }

    internal partial class StatusOptions
    {
        internal const int STATUS_ACTIVE = 0x00000001;
        internal const int STATUS_INACTIVE = 0x00000002;
        internal const int STATUS_ALL = STATUS_ACTIVE | STATUS_INACTIVE;

        internal const uint STATUS_SUCCESS = 0x00000000;
        internal const uint STATUS_SOME_NOT_MAPPED = 0x00000107;
        internal const uint STATUS_NO_MORE_FILES = 0x80000006;
        internal const uint STATUS_INVALID_PARAMETER = 0xC000000D;
        internal const uint STATUS_FILE_NOT_FOUND = 0xC000000F;
        internal const uint STATUS_NO_MEMORY = 0xC0000017;
        internal const uint STATUS_ACCESS_DENIED = 0xC0000022;
        internal const uint STATUS_OBJECT_NAME_NOT_FOUND = 0xC0000034;
        internal const uint STATUS_ACCOUNT_RESTRICTION = 0xC000006E;
        internal const uint STATUS_NONE_MAPPED = 0xC0000073;
        internal const uint STATUS_INSUFFICIENT_RESOURCES = 0xC000009A;
    }

    // https://msdn.microsoft.com/en-us/library/windows/hardware/ff550671.aspx
    [StructLayout(LayoutKind.Sequential)]
    public struct IO_STATUS_BLOCK
    {
        /// <summary>
        /// Status
        /// </summary>
        public IO_STATUS Status;

        /// <summary>
        /// Request dependent value.
        /// </summary>
        public IntPtr Information;

        // This isn't an actual Windows type, it is a union within IO_STATUS_BLOCK. We *have* to separate it out as
        // the size of IntPtr varies by architecture and we can't specify the size at compile time to offset the
        // Information pointer in the status block.
        [StructLayout(LayoutKind.Explicit)]
        public struct IO_STATUS
        {
            /// <summary>
            /// The completion status, either STATUS_SUCCESS if the operation was completed successfully or
            /// some other informational, warning, or error status.
            /// </summary>
            [FieldOffset(0)]
            public uint Status;

            /// <summary>
            /// Reserved for internal use.
            /// </summary>
            [FieldOffset(0)]
            public IntPtr Pointer;
        }
    }

    public enum FILE_INFORMATION_CLASS : uint
    {
        FileDirectoryInformation = 1,
        FileFullDirectoryInformation = 2,
        FileBothDirectoryInformation = 3,
        FileBasicInformation = 4,
        FileStandardInformation = 5,
        FileInternalInformation = 6,
        FileEaInformation = 7,
        FileAccessInformation = 8,
        FileNameInformation = 9,
        FileRenameInformation = 10,
        FileLinkInformation = 11,
        FileNamesInformation = 12,
        FileDispositionInformation = 13,
        FilePositionInformation = 14,
        FileFullEaInformation = 15,
        FileModeInformation = 16,
        FileAlignmentInformation = 17,
        FileAllInformation = 18,
        FileAllocationInformation = 19,
        FileEndOfFileInformation = 20,
        FileAlternateNameInformation = 21,
        FileStreamInformation = 22,
        FilePipeInformation = 23,
        FilePipeLocalInformation = 24,
        FilePipeRemoteInformation = 25,
        FileMailslotQueryInformation = 26,
        FileMailslotSetInformation = 27,
        FileCompressionInformation = 28,
        FileObjectIdInformation = 29,
        FileCompletionInformation = 30,
        FileMoveClusterInformation = 31,
        FileQuotaInformation = 32,
        FileReparsePointInformation = 33,
        FileNetworkOpenInformation = 34,
        FileAttributeTagInformation = 35,
        FileTrackingInformation = 36,
        FileIdBothDirectoryInformation = 37,
        FileIdFullDirectoryInformation = 38,
        FileValidDataLengthInformation = 39,
        FileShortNameInformation = 40,
        FileIoCompletionNotificationInformation = 41,
        FileIoStatusBlockRangeInformation = 42,
        FileIoPriorityHintInformation = 43,
        FileSfioReserveInformation = 44,
        FileSfioVolumeInformation = 45,
        FileHardLinkInformation = 46,
        FileProcessIdsUsingFileInformation = 47,
        FileNormalizedNameInformation = 48,
        FileNetworkPhysicalNameInformation = 49,
        FileIdGlobalTxDirectoryInformation = 50,
        FileIsRemoteDeviceInformation = 51,
        FileUnusedInformation = 52,
        FileNumaNodeInformation = 53,
        FileStandardLinkInformation = 54,
        FileRemoteProtocolInformation = 55,
        FileRenameInformationBypassAccessCheck = 56,
        FileLinkInformationBypassAccessCheck = 57,
        FileVolumeNameInformation = 58,
        FileIdInformation = 59,
        FileIdExtdDirectoryInformation = 60,
        FileReplaceCompletionInformation = 61,
        FileHardLinkFullIdInformation = 62,
        FileIdExtdBothDirectoryInformation = 63,
        FileDispositionInformationEx = 64,
        FileRenameInformationEx = 65,
        FileRenameInformationExBypassAccessCheck = 66,
        FileDesiredStorageClassInformation = 67,
        FileStatInformation = 68
    }
}

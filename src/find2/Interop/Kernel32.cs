using System;
using System.Runtime.InteropServices;

namespace find2.Interop;

internal static partial class Kernel32
{
    [LibraryImport(Libraries.Kernel32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool GetLogicalProcessorInformation(SYSTEM_LOGICAL_PROCESSOR_INFORMATION* buffer, out int bufferSize);

    private static unsafe int GetProcessorCoreCount()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // TODO: Arg, make this work properly for cores on other platforms.
            // sysconf(_SC_NPROCESSORS_ONLN) might work on Linux?
            return Environment.ProcessorCount;
        }

        if (!GetLogicalProcessorInformation(null, out var bufferSize))
        {
            return Environment.ProcessorCount;
        }

        var numEntries = bufferSize / sizeof(SYSTEM_LOGICAL_PROCESSOR_INFORMATION);
        var coreInfo = stackalloc SYSTEM_LOGICAL_PROCESSOR_INFORMATION[numEntries];

        if (!GetLogicalProcessorInformation(coreInfo, out bufferSize))
        {
            return Environment.ProcessorCount;
        }

        var cores = 0;
        for (var i = 0; i < numEntries; ++i)
        {
            var info = coreInfo[i];
            if (info.Relationship == LOGICAL_PROCESSOR_RELATIONSHIP.RelationProcessorCore)
            {
                ++cores;
            }
        }

        return cores > 0 ? cores : Environment.ProcessorCount;
    }

    public static readonly int NumPhysicalCores = GetProcessorCoreCount();

    [StructLayout(LayoutKind.Sequential)]
    private struct CACHE_DESCRIPTOR
    {
        public byte Level;
        public byte Associativity;
        public ushort LineSize;
        public uint Size;
        public uint Type;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct SYSTEM_LOGICAL_PROCESSOR_INFORMATION_UNION
    {
        [FieldOffset(0)] public byte ProcessorCore;
        [FieldOffset(0)] public uint NumaNode;
        [FieldOffset(0)] public CACHE_DESCRIPTOR Cache;
        [FieldOffset(0)] private ulong Reserved1;
        [FieldOffset(8)] private ulong Reserved2;
    }

    private enum LOGICAL_PROCESSOR_RELATIONSHIP
    {
        RelationProcessorCore,
        RelationNumaNode,
        RelationCache,
        RelationProcessorPackage,
        RelationGroup,
        RelationAll = 0xffff
    }

    private struct SYSTEM_LOGICAL_PROCESSOR_INFORMATION
    {
        public UIntPtr ProcessorMask;
        public LOGICAL_PROCESSOR_RELATIONSHIP Relationship;
        public SYSTEM_LOGICAL_PROCESSOR_INFORMATION_UNION ProcessorInformation;
    }
}
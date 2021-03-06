using System;
using System.Runtime.InteropServices;

namespace find2.Interop
{
    internal class Kernel32
    {
        [DllImport("kernel32.dll")]
        private static extern unsafe bool GetLogicalProcessorInformation(SYSTEM_LOGICAL_PROCESSOR_INFORMATION* buffer, out int bufferSize);

        private static unsafe int GetProcessorCoreCount()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // TODO: Arg, make this work properly for cores on other platforms.
                // sysconf(_SC_NPROCESSORS_ONLN)? Might work on Windows?
                return Environment.ProcessorCount;
            }

            GetLogicalProcessorInformation(null, out var bufferSize);
            var numEntries = bufferSize / sizeof(SYSTEM_LOGICAL_PROCESSOR_INFORMATION);
            var coreInfo = new SYSTEM_LOGICAL_PROCESSOR_INFORMATION[numEntries];

            fixed (SYSTEM_LOGICAL_PROCESSOR_INFORMATION* pCoreInfo = coreInfo)
            {
                GetLogicalProcessorInformation(pCoreInfo, out bufferSize);
                var cores = 0;
                for (var i = 0; i < numEntries; ++i)
                {
                    var info = pCoreInfo[i];
                    if (info.Relationship == LOGICAL_PROCESSOR_RELATIONSHIP.RelationProcessorCore)
                        ++cores;
                }
                return cores > 0 ? cores : 1;
            }
        }

        public static readonly int NumPhysicalCores = GetProcessorCoreCount();

        [StructLayout(LayoutKind.Sequential)]
        struct CACHE_DESCRIPTOR
        {
            public byte Level;
            public byte Associativity;
            public ushort LineSize;
            public uint Size;
            public uint Type;
        }

        [StructLayout(LayoutKind.Explicit)]
        struct SYSTEM_LOGICAL_PROCESSOR_INFORMATION_UNION
        {
            [FieldOffset(0)] public byte ProcessorCore;
            [FieldOffset(0)] public uint NumaNode;
            [FieldOffset(0)] public CACHE_DESCRIPTOR Cache;
            [FieldOffset(0)] private ulong Reserved1;
            [FieldOffset(8)] private ulong Reserved2;
        }

        public enum LOGICAL_PROCESSOR_RELATIONSHIP
        {
            RelationProcessorCore,
            RelationNumaNode,
            RelationCache,
            RelationProcessorPackage,
            RelationGroup,
            RelationAll = 0xffff
        }

        struct SYSTEM_LOGICAL_PROCESSOR_INFORMATION
        {
            public UIntPtr ProcessorMask;
            public LOGICAL_PROCESSOR_RELATIONSHIP Relationship;
            public SYSTEM_LOGICAL_PROCESSOR_INFORMATION_UNION ProcessorInformation;
        }
    }
}
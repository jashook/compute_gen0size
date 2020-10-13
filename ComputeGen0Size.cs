using System;
using System.Diagnostics;
using System.Runtime;
using System.Runtime.InteropServices;

namespace compute_gen0size
{
    [StructLayout(LayoutKind.Sequential)]
    public struct PROCESSORCORE
    {
        public byte Flags;
    };

    [StructLayout(LayoutKind.Sequential)]
    public struct NUMANODE
    {
        public uint NodeNumber;
    }

    public enum PROCESSOR_CACHE_TYPE
    {
        CacheUnified,
        CacheInstruction,
        CacheData,
        CacheTrace
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct CACHE_DESCRIPTOR
    {
        public byte Level;
        public byte Associativity;
        public ushort LineSize;
        public uint Size;
        public PROCESSOR_CACHE_TYPE Type;
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct SYSTEM_LOGICAL_PROCESSOR_INFORMATION_UNION
    {
        [FieldOffset(0)]
        public PROCESSORCORE ProcessorCore;
        [FieldOffset(0)]
        public NUMANODE NumaNode;
        [FieldOffset(0)]
        public CACHE_DESCRIPTOR Cache;
        [FieldOffset(0)]
        private UInt64 Reserved1;
        [FieldOffset(8)]
        private UInt64 Reserved2;
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

    public struct SYSTEM_LOGICAL_PROCESSOR_INFORMATION
    {
        public UIntPtr ProcessorMask;
        public LOGICAL_PROCESSOR_RELATIONSHIP Relationship;
        public SYSTEM_LOGICAL_PROCESSOR_INFORMATION_UNION ProcessorInformation;
    }

    class Program
    {
        [DllImport(@"kernel32.dll", SetLastError=true)]
        public static extern bool GetLogicalProcessorInformation(
            IntPtr Buffer,
            ref uint ReturnLength
        );

        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetPhysicallyInstalledSystemMemory(out long TotalMemoryInKilobytes);

        public static SYSTEM_LOGICAL_PROCESSOR_INFORMATION[] GetLogicalProcessorInformationPinvoke()
        {
            uint glpiSize = 0;
            if (!GetLogicalProcessorInformation(IntPtr.Zero, ref glpiSize) && glpiSize == 0)
            {
                throw new InvalidOperationException("Unable to retreive the glpi buffer size.");
            }

            IntPtr intPtr = Marshal.AllocHGlobal((int) glpiSize);
            try
            {
                if (GetLogicalProcessorInformation(intPtr, ref glpiSize))
                {
                    int lpiSize = Marshal.SizeOf(typeof(SYSTEM_LOGICAL_PROCESSOR_INFORMATION));
                    int itemCount = (int)glpiSize / lpiSize;

                    SYSTEM_LOGICAL_PROCESSOR_INFORMATION[] gpli = new SYSTEM_LOGICAL_PROCESSOR_INFORMATION[itemCount];
                    IntPtr gpliPtr = intPtr;
                    for (int index = 0; index < itemCount; ++index)
                    {
                        gpli[index] = (SYSTEM_LOGICAL_PROCESSOR_INFORMATION) Marshal.PtrToStructure(gpliPtr, typeof(SYSTEM_LOGICAL_PROCESSOR_INFORMATION));
                        gpliPtr += lpiSize;
                    }

                    return gpli;
                }
            }
            finally
            {
                Marshal.FreeHGlobal(intPtr);
            }

            return null;
        }

        /// GetCacheSizePerLogicalCpu
        ///
        /// Notes:
        ///
        /// This method will mock the Runtime function GetCacheSizePerLogicalCpu
        public static long GetCacheSizePerLogicalCpu(bool trueSize=false)
        {
            SYSTEM_LOGICAL_PROCESSOR_INFORMATION[] glpi = GetLogicalProcessorInformationPinvoke();

            long last_cache_size = 0;
            for (int index = 0; index < glpi.Length; ++index)
            {
                if (glpi[index].Relationship == LOGICAL_PROCESSOR_RELATIONSHIP.RelationCache)
                {
                    last_cache_size = Math.Max(last_cache_size, glpi[index].ProcessorInformation.Cache.Size);
                }
            }

            return !trueSize ? last_cache_size * 4 : last_cache_size;
        }

        public static long ComputeGen0MinSize()
        {
            long cacheSizePerCpu = GetCacheSizePerLogicalCpu();
            long gen0Size = Math.Max(cacheSizePerCpu, (256*1024));
            long trueSize = Math.Max(GetCacheSizePerLogicalCpu(true), (256*1024));

            Debug.Assert(cacheSizePerCpu > 0);

            int nProcessors = Environment.ProcessorCount;

            long totalPhysicalMemory =  0;
            if (!GetPhysicallyInstalledSystemMemory(out totalPhysicalMemory))
            {
                throw new InvalidOperationException("Unable to return memory stats.");
            }

            // Convert to bytes
            totalPhysicalMemory *= 1024;

            if ((gen0Size * nProcessors) > (totalPhysicalMemory / 6))
            {
                gen0Size /= 2;
                if (gen0Size <= trueSize)
                {
                    gen0Size = trueSize;
                }
            }

            return gen0Size;
        }

        static void Main(string[] args)
        {
            long gcGen0MinSize = ComputeGen0MinSize();
        }
    }
}

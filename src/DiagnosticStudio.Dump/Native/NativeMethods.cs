using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace DiagnosticStudio.Dump.Native
{
    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public unsafe struct NativeSystemInfo
    {
        public uint ProcessorArchitecture;
        public uint NumberOfProcessors;
        public uint MajorVersion;
        public uint MinorVersion;
        public uint BuildNumber;
        public fixed char CSDVersionBuffer[128];

        public string CSDVersion
        {
            get
            {
                fixed (char* p = CSDVersionBuffer)
                {
                    return new string(p);
                }
            }
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public unsafe struct NativeExceptionInfo
    {
        public uint ExceptionCode;
        public uint ExceptionFlags;
        public ulong ExceptionAddress;
        public uint ThreadId;
        public uint NumberParameters;
        public fixed ulong ExceptionInformationBuffer[15];

        public ulong[] ExceptionInformation
        {
            get
            {
                var arr = new ulong[15];
                fixed (ulong* p = ExceptionInformationBuffer)
                {
                    for (int i = 0; i < 15; i++)
                    {
                        arr[i] = p[i];
                    }
                }
                return arr;
            }
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public unsafe struct NativeModuleInfo
    {
        public ulong BaseOfImage;
        public uint SizeOfImage;
        public uint CheckSum;
        public uint TimeDateStamp;
        public fixed char ModuleNameBuffer[260];
        public fixed char ImageNameBuffer[260];
        public fixed char LoadedPdbNameBuffer[260];
        public int IsSymbolLoaded;

        public string ModuleName
        {
            get { fixed (char* p = ModuleNameBuffer) return new string(p); }
        }

        public string ImageName
        {
            get { fixed (char* p = ImageNameBuffer) return new string(p); }
        }

        public string LoadedPdbName
        {
            get { fixed (char* p = LoadedPdbNameBuffer) return new string(p); }
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public unsafe struct NativeStackFrameInfo
    {
        public ulong InstructionOffset;
        public ulong ReturnOffset;
        public ulong FrameOffset;
        public ulong StackOffset;
        public fixed char ModuleNameBuffer[128];
        public fixed char FunctionNameBuffer[256];
        public fixed char SourceFileBuffer[260];
        public uint LineNumber;
        public uint Displacement;

        public string ModuleName
        {
            get { fixed (char* p = ModuleNameBuffer) return new string(p); }
        }

        public string FunctionName
        {
            get { fixed (char* p = FunctionNameBuffer) return new string(p); }
        }

        public string SourceFile
        {
            get { fixed (char* p = SourceFileBuffer) return new string(p); }
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public unsafe struct NativeSymbolResult
    {
        public ulong Address;
        public ulong ModuleBase;
        public fixed char ModuleNameBuffer[128];
        public fixed char FunctionNameBuffer[256];
        public fixed char SourceFileBuffer[260];
        public uint LineNumber;
        public uint Displacement;
        public int Success;

        public string ModuleName
        {
            get { fixed (char* p = ModuleNameBuffer) return new string(p); }
        }

        public string FunctionName
        {
            get { fixed (char* p = FunctionNameBuffer) return new string(p); }
        }

        public string SourceFile
        {
            get { fixed (char* p = SourceFileBuffer) return new string(p); }
        }
    }

    /// <summary>
    /// DiagnosticNative.dll 互操作封裝
    /// </summary>
    public static class NativeMethods
    {
        private const string LibraryName = "DiagnosticNative.dll";

        static NativeMethods()
        {
            NativeLibrary.SetDllImportResolver(typeof(NativeMethods).Assembly, ResolveDll);
        }

        private static IntPtr ResolveDll(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
        {
            if (libraryName == LibraryName)
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string candidate1 = Path.Combine(baseDir, LibraryName);
                if (File.Exists(candidate1))
                {
                    return NativeLibrary.Load(candidate1);
                }

                string solutionDir = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", ".."));
                string candidate2 = Path.Combine(solutionDir, "src", "DiagnosticStudio.Native", "build", "Release", LibraryName);
                if (File.Exists(candidate2))
                {
                    return NativeLibrary.Load(candidate2);
                }
            }
            return IntPtr.Zero;
        }

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int OpenDumpSession([MarshalAs(UnmanagedType.LPWStr)] string dumpFilePath, [MarshalAs(UnmanagedType.LPWStr)] string? symbolPath, out IntPtr outSession);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int CloseDumpSession(IntPtr session);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern unsafe int GetDumpSystemInfo(IntPtr session, NativeSystemInfo* outInfo);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern unsafe int GetDumpExceptionInfo(IntPtr session, NativeExceptionInfo* outException);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int GetDumpModuleCount(IntPtr session, out int outCount);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern unsafe int GetDumpModule(IntPtr session, int index, NativeModuleInfo* outModule);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int GetDumpThreadCount(IntPtr session, out int outCount);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int GetDumpThreadId(IntPtr session, int index, out uint outThreadId);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern unsafe int GetDumpThreadStackTrace(IntPtr session, uint threadId, int maxFrames, NativeStackFrameInfo* outFrames, out int outActualFrames);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern unsafe int ResolveAddressSymbol(IntPtr session, ulong address, NativeSymbolResult* outSymbol);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int IsDumpHeuristicallyRecovered(IntPtr session, out int outIsRecovered);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int CaptureProcessLiveDump(uint processId, [MarshalAs(UnmanagedType.LPWStr)] string dumpFilePath, int dumpType);
    }
}


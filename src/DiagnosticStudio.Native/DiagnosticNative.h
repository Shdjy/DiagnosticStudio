#pragma once

#ifndef NOMINMAX
#define NOMINMAX
#endif

#include <windows.h>
#include <stdint.h>

#ifdef DIAGNOSTICNATIVE_EXPORTS
#define DIAG_API __declspec(dllexport)
#else
#define DIAG_API __declspec(dllimport)
#endif

#pragma pack(push, 8)

// 系统与处理器基本架构信息
struct NativeSystemInfo
{
    uint32_t ProcessorArchitecture;
    uint32_t NumberOfProcessors;
    uint32_t MajorVersion;
    uint32_t MinorVersion;
    uint32_t BuildNumber;
    wchar_t CSDVersion[128];
};

// 崩溃异常摘要
struct NativeExceptionInfo
{
    uint32_t ExceptionCode;
    uint32_t ExceptionFlags;
    uint64_t ExceptionAddress;
    uint32_t ThreadId;
    uint32_t NumberParameters;
    uint64_t ExceptionInformation[15];
};

// 模块信息与符号加载状态
struct NativeModuleInfo
{
    uint64_t BaseOfImage;
    uint32_t SizeOfImage;
    uint32_t CheckSum;
    uint32_t TimeDateStamp;
    wchar_t ModuleName[260];
    wchar_t ImageName[260];
    wchar_t LoadedPdbName[260];
    int32_t IsSymbolLoaded;
};

// 单层堆栈帧
struct NativeStackFrameInfo
{
    uint64_t InstructionOffset;
    uint64_t ReturnOffset;
    uint64_t FrameOffset;
    uint64_t StackOffset;
    wchar_t ModuleName[128];
    wchar_t FunctionName[256];
    wchar_t SourceFile[260];
    uint32_t LineNumber;
    uint32_t Displacement;
};

// 符号解析结果
struct NativeSymbolResult
{
    uint64_t Address;
    uint64_t ModuleBase;
    wchar_t ModuleName[128];
    wchar_t FunctionName[256];
    wchar_t SourceFile[260];
    uint32_t LineNumber;
    uint32_t Displacement;
    int32_t Success;
};

#pragma pack(pop)

extern "C"
{
    DIAG_API int32_t OpenDumpSession(const wchar_t* dumpFilePath, const wchar_t* symbolPath, void** outSession);
    DIAG_API int32_t CloseDumpSession(void* session);
    DIAG_API int32_t GetDumpSystemInfo(void* session, NativeSystemInfo* outInfo);
    DIAG_API int32_t GetDumpExceptionInfo(void* session, NativeExceptionInfo* outException);
    DIAG_API int32_t GetDumpModuleCount(void* session, int32_t* outCount);
    DIAG_API int32_t GetDumpModule(void* session, int32_t index, NativeModuleInfo* outModule);
    DIAG_API int32_t GetDumpThreadCount(void* session, int32_t* outCount);
    DIAG_API int32_t GetDumpThreadId(void* session, int32_t index, uint32_t* outThreadId);
    DIAG_API int32_t GetDumpThreadStackTrace(void* session, uint32_t threadId, int32_t maxFrames, NativeStackFrameInfo* outFrames, int32_t* outActualFrames);
    DIAG_API int32_t ResolveAddressSymbol(void* session, uint64_t address, NativeSymbolResult* outSymbol);
    DIAG_API int32_t CaptureProcessLiveDump(uint32_t processId, const wchar_t* dumpFilePath, int32_t dumpType);
}

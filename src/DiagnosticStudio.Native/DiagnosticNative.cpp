#include "DiagnosticNative.h"
#include <DbgHelp.h>
#include <vector>
#include <string>
#include <memory>
#include <mutex>
#include <algorithm>

#pragma comment(lib, "DbgHelp.lib")

namespace
{
    // 转储会话内部维护状态结构体
    struct DumpSessionInternal
    {
        HANDLE FileHandle = INVALID_HANDLE_VALUE;
        HANDLE MappingHandle = nullptr;
        void* BaseAddress = nullptr;
        uint64_t FileSize = 0;
        HANDLE SymbolProcessHandle = nullptr;

        MINIDUMP_SYSTEM_INFO SystemInfo{};
        bool HasSystemInfo = false;

        MINIDUMP_EXCEPTION_STREAM ExceptionStream{};
        bool HasException = false;

        std::vector<MINIDUMP_MODULE> Modules;
        std::vector<MINIDUMP_THREAD> Threads;
        struct DumpMemoryRange
        {
            uint64_t StartAddress;
            uint64_t DataSize;
            uint64_t Rva;
        };
        std::vector<DumpMemoryRange> MemoryRanges;
        std::vector<std::wstring> ModuleNames;

        // 当前正在展开调用栈的目标线程栈内存描述
        uint64_t ActiveStackStart = 0;
        uint64_t ActiveStackSize = 0;
        const uint8_t* ActiveStackData = nullptr;

        // 是否通过启发式引擎成功进行了容错修复与恢复
        bool IsHeuristicallyRecovered = false;
    };

    // 校验 RVA 相对虚拟地址是否在映射文件合法范围内
    inline bool IsValidRva(uint64_t rva, uint64_t size, uint64_t fileSize)
    {
        return rva != 0 && (rva + size <= fileSize) && (rva + size >= rva);
    }

    // 使用 SEH 保护的内存安全拷贝，防止访问畸形 Dump 导致进程崩溃
    static bool SafeReadMemory(const void* src, void* dst, size_t size)
    {
        if (src == nullptr || dst == nullptr || size == 0)
        {
            return false;
        }

        __try
        {
            memcpy(dst, src, size);
            return true;
        }
        __except (EXCEPTION_EXECUTE_HANDLER)
        {
            return false;
        }
    }

    // 提供给 StackWalk64 的内存读取回调函数 (全面 SEH 隔离保护)
    static BOOL CALLBACK MinidumpReadMemoryProc64(
        HANDLE hProcess,
        DWORD64 qwBaseAddress,
        PVOID lpBuffer,
        DWORD nSize,
        LPDWORD lpNumberOfBytesRead)
    {
        if (hProcess == nullptr || lpBuffer == nullptr || lpNumberOfBytesRead == nullptr || nSize == 0)
        {
            return FALSE;
        }

        *lpNumberOfBytesRead = 0;

        __try
        {
            auto pSession = reinterpret_cast<DumpSessionInternal*>(hProcess);

            // 1. 优先从当前线程正在回溯的栈内存区间读取
            if (pSession->ActiveStackData != nullptr && pSession->ActiveStackSize > 0)
            {
                if (qwBaseAddress >= pSession->ActiveStackStart &&
                    qwBaseAddress < pSession->ActiveStackStart + pSession->ActiveStackSize)
                {
                    const uint64_t offset = qwBaseAddress - pSession->ActiveStackStart;
                    const uint64_t bytesAvailable = pSession->ActiveStackSize - offset;
                    const DWORD toRead = static_cast<DWORD>(std::min<uint64_t>(bytesAvailable, nSize));

                    if (SafeReadMemory(pSession->ActiveStackData + offset, lpBuffer, toRead))
                    {
                        *lpNumberOfBytesRead = toRead;
                        return TRUE;
                    }
                }
            }

            // 2. 检查请求读取的地址是否落在全局 MemoryRanges (Stream 5 / 9) 中
            for (const auto& mr : pSession->MemoryRanges)
            {
                if (qwBaseAddress >= mr.StartAddress && qwBaseAddress < mr.StartAddress + mr.DataSize)
                {
                    const uint64_t offset = qwBaseAddress - mr.StartAddress;
                    const uint64_t bytesAvailable = mr.DataSize - offset;
                    const DWORD toRead = static_cast<DWORD>(std::min<uint64_t>(bytesAvailable, nSize));

                    if (IsValidRva(mr.Rva + offset, toRead, pSession->FileSize))
                    {
                        const auto pSrc = reinterpret_cast<const uint8_t*>(pSession->BaseAddress) + mr.Rva + offset;
                        if (SafeReadMemory(pSrc, lpBuffer, toRead))
                        {
                            *lpNumberOfBytesRead = toRead;
                            return TRUE;
                        }
                    }
                    break;
                }
            }

            // 3. 检查是否请求读取已加载模块的头部或代码段
            for (const auto& mod : pSession->Modules)
            {
                if (qwBaseAddress >= mod.BaseOfImage && qwBaseAddress < mod.BaseOfImage + mod.SizeOfImage)
                {
                    break;
                }
            }
        }
        __except (EXCEPTION_EXECUTE_HANDLER)
        {
            return FALSE;
        }

        return FALSE;
    }

    // SEH 隔离保护的 StackWalk64 包装函数
    static BOOL SafeStackWalk64(
        DWORD MachineType,
        HANDLE hProcess,
        HANDLE hThread,
        LPSTACKFRAME64 StackFrame,
        PVOID ContextRecord,
        PREAD_PROCESS_MEMORY_ROUTINE64 ReadMemoryRoutine,
        PFUNCTION_TABLE_ACCESS_ROUTINE64 FunctionTableAccessRoutine,
        PGET_MODULE_BASE_ROUTINE64 GetModuleBaseRoutine,
        PTRANSLATE_ADDRESS_ROUTINE64 TranslateAddress)
    {
        __try
        {
            return StackWalk64(
                MachineType,
                hProcess,
                hThread,
                StackFrame,
                ContextRecord,
                ReadMemoryRoutine,
                FunctionTableAccessRoutine,
                GetModuleBaseRoutine,
                TranslateAddress);
        }
        __except (EXCEPTION_EXECUTE_HANDLER)
        {
            return FALSE;
        }
    }

    // 使用 SEH 隔离保护的符号解析函数，防御 DbgHelp 内部因未对齐地址或损坏 PDB 抛出的访问违例
    static bool SafeResolveSymbol(
        HANDLE hProcess,
        uint64_t address,
        NativeSymbolResult* outSymbol)
    {
        if (hProcess == nullptr || outSymbol == nullptr || address == 0)
        {
            return false;
        }

        ZeroMemory(outSymbol, sizeof(NativeSymbolResult));
        outSymbol->Address = address;

        __try
        {
            IMAGEHLP_MODULEW64 modInfo{};
            modInfo.SizeOfStruct = sizeof(IMAGEHLP_MODULEW64);
            if (SymGetModuleInfoW64(hProcess, address, &modInfo))
            {
                outSymbol->ModuleBase = modInfo.BaseOfImage;
                wcsncpy_s(outSymbol->ModuleName, 128, modInfo.ModuleName, _TRUNCATE);
            }

            constexpr size_t bufferSize = sizeof(SYMBOL_INFOW) + 256 * sizeof(wchar_t);
            alignas(SYMBOL_INFOW) uint8_t symbolBuffer[bufferSize]{};
            auto pSym = reinterpret_cast<SYMBOL_INFOW*>(symbolBuffer);
            pSym->SizeOfStruct = sizeof(SYMBOL_INFOW);
            pSym->MaxNameLen = 255;

            DWORD64 displacement = 0;
            if (SymFromAddrW(hProcess, address, &displacement, pSym))
            {
                wcsncpy_s(outSymbol->FunctionName, 256, pSym->Name, _TRUNCATE);
                outSymbol->Displacement = static_cast<uint32_t>(displacement);
                outSymbol->Success = 1;
            }

            IMAGEHLP_LINEW64 lineInfo{};
            lineInfo.SizeOfStruct = sizeof(IMAGEHLP_LINEW64);
            DWORD lineDisplacement = 0;
            if (SymGetLineFromAddrW64(hProcess, address, &lineDisplacement, &lineInfo))
            {
                if (lineInfo.FileName != nullptr)
                {
                    wcsncpy_s(outSymbol->SourceFile, 260, lineInfo.FileName, _TRUNCATE);
                }
                outSymbol->LineNumber = lineInfo.LineNumber;
                outSymbol->Success = 1;
            }

            return true;
        }
        __except (EXCEPTION_EXECUTE_HANDLER)
        {
            return false;
        }
    }

    // SEH 隔离保护的 DbgHelp 符号清理函数
    static void SafeSymCleanup(HANDLE hProcess)
    {
        __try
        {
            if (hProcess != nullptr)
            {
                SymCleanup(hProcess);
            }
        }
        __except (EXCEPTION_EXECUTE_HANDLER)
        {
        }
    }

    // SEH 隔离保护的文件映射卸载函数
    static void SafeUnmapView(void* pBase)
    {
        __try
        {
            if (pBase != nullptr)
            {
                UnmapViewOfFile(pBase);
            }
        }
        __except (EXCEPTION_EXECUTE_HANDLER)
        {
        }
    }

    // SEH 隔离保护的句柄关闭函数
    static void SafeCloseHandle(HANDLE& h)
    {
        __try
        {
            if (h != nullptr && h != INVALID_HANDLE_VALUE)
            {
                CloseHandle(h);
                h = INVALID_HANDLE_VALUE;
            }
        }
        __except (EXCEPTION_EXECUTE_HANDLER)
        {
        }
    }

    // SEH 隔离保护的系统信息安全复制函数
    static bool SafeExtractSystemInfo(
        const DumpSessionInternal* pSession,
        NativeSystemInfo* outInfo)
    {
        if (pSession == nullptr || outInfo == nullptr)
        {
            return false;
        }

        __try
        {
            ZeroMemory(outInfo, sizeof(NativeSystemInfo));
            outInfo->ProcessorArchitecture = pSession->SystemInfo.ProcessorArchitecture;
            outInfo->NumberOfProcessors = pSession->SystemInfo.NumberOfProcessors;
            outInfo->MajorVersion = pSession->SystemInfo.MajorVersion;
            outInfo->MinorVersion = pSession->SystemInfo.MinorVersion;
            outInfo->BuildNumber = pSession->SystemInfo.BuildNumber;
            outInfo->CSDVersion[0] = L'\0';

            const auto csdRva = pSession->SystemInfo.CSDVersionRva;
            if (csdRva != 0 && IsValidRva(csdRva, sizeof(ULONG32), pSession->FileSize))
            {
                const auto pBase = reinterpret_cast<const uint8_t*>(pSession->BaseAddress);
                ULONG32 byteLen = 0;
                if (SafeReadMemory(pBase + csdRva, &byteLen, sizeof(ULONG32)) && byteLen > 0)
                {
                    if (IsValidRva(csdRva + sizeof(ULONG32), byteLen, pSession->FileSize))
                    {
                        const size_t charCount = byteLen / sizeof(wchar_t);
                        const size_t maxToCopy = (charCount < 127) ? charCount : 127;
                        SafeReadMemory(pBase + csdRva + sizeof(ULONG32), outInfo->CSDVersion, maxToCopy * sizeof(wchar_t));
                        outInfo->CSDVersion[maxToCopy] = L'\0';
                    }
                }
            }
            return true;
        }
        __except (EXCEPTION_EXECUTE_HANDLER)
        {
            return false;
        }
    }

    // SEH 隔离保护的异常信息安全复制函数
    static bool SafeExtractExceptionInfo(
        const DumpSessionInternal* pSession,
        NativeExceptionInfo* outException)
    {
        if (pSession == nullptr || outException == nullptr)
        {
            return false;
        }

        __try
        {
            ZeroMemory(outException, sizeof(NativeExceptionInfo));
            outException->ExceptionCode = pSession->ExceptionStream.ExceptionRecord.ExceptionCode;
            outException->ExceptionFlags = pSession->ExceptionStream.ExceptionRecord.ExceptionFlags;
            outException->ExceptionAddress = pSession->ExceptionStream.ExceptionRecord.ExceptionAddress;
            outException->ThreadId = pSession->ExceptionStream.ThreadId;
            outException->NumberParameters = pSession->ExceptionStream.ExceptionRecord.NumberParameters;

            for (uint32_t i = 0; i < outException->NumberParameters && i < 15; ++i)
            {
                outException->ExceptionInformation[i] = pSession->ExceptionStream.ExceptionRecord.ExceptionInformation[i];
            }
            return true;
        }
        __except (EXCEPTION_EXECUTE_HANDLER)
        {
            return false;
        }
    }

    // SEH 隔离保护的会话内存释放函数
    static void SafeDeleteSession(DumpSessionInternal* pSession)
    {
        __try
        {
            delete pSession;
        }
        __except (EXCEPTION_EXECUTE_HANDLER)
        {
        }
    }
}

extern "C"
{
    // 打开并初始化 Dump 分析会话
    // 启发式转储流重建器: 针对由于进程被外部强杀、看门狗超时中断等导致流目录索引未回写(全零)的截断转储文件，基于底层物理特征自愈恢复关键流
    static void TryHeuristicRecoverDump(DumpSessionInternal* pSession)
    {
        if (pSession == nullptr || pSession->BaseAddress == nullptr || pSession->FileSize < sizeof(MINIDUMP_HEADER))
        {
            return;
        }

        const auto pBase = reinterpret_cast<const uint8_t*>(pSession->BaseAddress);
        const auto fileSize = pSession->FileSize;
        bool recoveredAnything = false;

        // 1. 启发式恢复 SystemInfo
        if (!pSession->HasSystemInfo)
        {
            const uint64_t candidateOffsets[] = { 0xF8, 0x100, 0x120, 0x140 };
            for (const auto offset : candidateOffsets)
            {
                if (IsValidRva(offset, sizeof(MINIDUMP_SYSTEM_INFO), fileSize))
                {
                    const auto pSys = reinterpret_cast<const MINIDUMP_SYSTEM_INFO*>(pBase + offset);
                    if ((pSys->ProcessorArchitecture == PROCESSOR_ARCHITECTURE_AMD64 ||
                         pSys->ProcessorArchitecture == PROCESSOR_ARCHITECTURE_INTEL ||
                         pSys->ProcessorArchitecture == PROCESSOR_ARCHITECTURE_ARM ||
                         pSys->ProcessorArchitecture == PROCESSOR_ARCHITECTURE_ARM64) &&
                        (pSys->PlatformId == VER_PLATFORM_WIN32_NT || pSys->PlatformId == 2) &&
                        pSys->MajorVersion >= 5 && pSys->MajorVersion <= 20)
                    {
                        SafeReadMemory(pSys, &pSession->SystemInfo, sizeof(MINIDUMP_SYSTEM_INFO));
                        pSession->HasSystemInfo = true;
                        recoveredAnything = true;
                        break;
                    }
                }
            }
        }

        // 2. 启发式恢复 ExceptionStream (扫描常见的致命崩溃码)
        if (!pSession->HasException)
        {
            const uint64_t scanLimit = (fileSize > sizeof(MINIDUMP_EXCEPTION_STREAM)) 
                ? std::min<uint64_t>(fileSize - sizeof(MINIDUMP_EXCEPTION_STREAM), 4 * 1024 * 1024) 
                : 0;
            for (uint64_t offset = 0xF8; offset < scanLimit; offset += 4)
            {
                const auto pExp = reinterpret_cast<const MINIDUMP_EXCEPTION_STREAM*>(pBase + offset);
                const uint32_t code = pExp->ExceptionRecord.ExceptionCode;
                const uint32_t numParams = pExp->ExceptionRecord.NumberParameters;
                const uint32_t tid = pExp->ThreadId;

                const bool isSeverityFatal = (code >= 0x80000000 && code <= 0xE0FFFFFF);
                if (isSeverityFatal && tid > 0 && tid < 1000000 && numParams <= 15 && pExp->__alignment == 0)
                {
                    SafeReadMemory(pExp, &pSession->ExceptionStream, sizeof(MINIDUMP_EXCEPTION_STREAM));
                    pSession->HasException = true;
                    recoveredAnything = true;
                    break;
                }
            }
        }

        // 3. 启发式恢复 ThreadListStream
        if (pSession->Threads.empty())
        {
            const uint64_t scanLimit = (fileSize > sizeof(MINIDUMP_THREAD) * 2)
                ? std::min<uint64_t>(fileSize - sizeof(MINIDUMP_THREAD) * 2, 8 * 1024 * 1024)
                : 0;
            for (uint64_t offset = 0x200; offset < scanLimit; offset += 4)
            {
                const auto pThread0 = reinterpret_cast<const MINIDUMP_THREAD*>(pBase + offset);
                const bool isContextSizeValid = (pThread0->ThreadContext.DataSize == sizeof(CONTEXT) || 
                                                 pThread0->ThreadContext.DataSize == 1232 || 
                                                 pThread0->ThreadContext.DataSize == 716);
                if (isContextSizeValid && pThread0->ThreadId > 0 && pThread0->ThreadId < 1000000 &&
                    pThread0->Stack.Memory.DataSize > 0 && pThread0->Stack.Memory.DataSize < 20 * 1024 * 1024 &&
                    pThread0->ThreadContext.Rva > 0 && pThread0->ThreadContext.Rva < fileSize)
                {
                    uint32_t threadCount = 0;
                    bool hasExplicitCount = false;
                    if (offset >= 4)
                    {
                        const uint32_t candidateCount = *reinterpret_cast<const uint32_t*>(pBase + offset - 4);
                        if (candidateCount >= 1 && candidateCount <= 500)
                        {
                            threadCount = candidateCount;
                            hasExplicitCount = true;
                        }
                    }

                    if (!hasExplicitCount)
                    {
                        uint64_t curr = offset;
                        while (curr + sizeof(MINIDUMP_THREAD) <= fileSize && threadCount < 500)
                        {
                            const auto pT = reinterpret_cast<const MINIDUMP_THREAD*>(pBase + curr);
                            if (pT->ThreadId > 0 && pT->ThreadId < 1000000 && 
                                (pT->ThreadContext.DataSize == sizeof(CONTEXT) || pT->ThreadContext.DataSize == 1232 || pT->ThreadContext.DataSize == 716))
                            {
                                threadCount++;
                                curr += sizeof(MINIDUMP_THREAD);
                            }
                            else
                            {
                                break;
                            }
                        }
                    }

                    if (threadCount > 0)
                    {
                        for (uint32_t t = 0; t < threadCount; ++t)
                        {
                            const uint64_t tOffset = offset + t * sizeof(MINIDUMP_THREAD);
                            if (tOffset + sizeof(MINIDUMP_THREAD) > fileSize) break;
                            const auto pT = reinterpret_cast<const MINIDUMP_THREAD*>(pBase + tOffset);
                            pSession->Threads.push_back(*pT);

                            if (pT->Stack.StartOfMemoryRange > 0 && pT->Stack.Memory.DataSize > 0)
                            {
                                DumpSessionInternal::DumpMemoryRange mr{};
                                mr.StartAddress = pT->Stack.StartOfMemoryRange;
                                mr.DataSize = pT->Stack.Memory.DataSize;
                                mr.Rva = pT->Stack.Memory.Rva;
                                pSession->MemoryRanges.push_back(mr);
                            }
                        }
                        recoveredAnything = true;
                        break;
                    }
                }
            }
        }

        // 4. 启发式恢复 ModuleListStream
        if (pSession->Modules.empty())
        {
            const uint64_t scanLimit = (fileSize > sizeof(MINIDUMP_MODULE))
                ? std::min<uint64_t>(fileSize - sizeof(MINIDUMP_MODULE), 16 * 1024 * 1024)
                : 0;
            for (uint64_t offset = 0x200; offset < scanLimit; offset += 4)
            {
                const uint32_t candidateCount = *reinterpret_cast<const uint32_t*>(pBase + offset);
                if (candidateCount >= 2 && candidateCount <= 1000)
                {
                    const uint64_t modArrayStart = offset + 4;
                    if (modArrayStart + sizeof(MINIDUMP_MODULE) <= fileSize)
                    {
                        const auto pMod0 = reinterpret_cast<const MINIDUMP_MODULE*>(pBase + modArrayStart);
                        if (pMod0->BaseOfImage >= 0x10000 && pMod0->SizeOfImage >= 0x1000 &&
                            pMod0->ModuleNameRva > 0 && pMod0->ModuleNameRva + 4 < fileSize)
                        {
                            const uint32_t nameByteLen = *reinterpret_cast<const uint32_t*>(pBase + pMod0->ModuleNameRva);
                            if (nameByteLen >= 4 && nameByteLen <= 512 && pMod0->ModuleNameRva + 4 + nameByteLen <= fileSize)
                            {
                                const wchar_t* pNameStr = reinterpret_cast<const wchar_t*>(pBase + pMod0->ModuleNameRva + 4);
                                const size_t charLen = nameByteLen / sizeof(wchar_t);
                                std::wstring strTest(pNameStr, charLen);
                                if (strTest.find(L".dll") != std::wstring::npos || strTest.find(L".exe") != std::wstring::npos || strTest.find(L".") != std::wstring::npos)
                                {
                                    for (uint32_t m = 0; m < candidateCount; ++m)
                                    {
                                        const uint64_t modOffset = modArrayStart + m * sizeof(MINIDUMP_MODULE);
                                        if (modOffset + sizeof(MINIDUMP_MODULE) > fileSize) break;
                                        const auto pMod = reinterpret_cast<const MINIDUMP_MODULE*>(pBase + modOffset);
                                        pSession->Modules.push_back(*pMod);

                                        std::wstring modPath;
                                        if (pMod->ModuleNameRva > 0 && pMod->ModuleNameRva + 4 < fileSize)
                                        {
                                            const uint32_t bLen = *reinterpret_cast<const uint32_t*>(pBase + pMod->ModuleNameRva);
                                            if (bLen > 0 && pMod->ModuleNameRva + 4 + bLen <= fileSize)
                                            {
                                                const wchar_t* pStr = reinterpret_cast<const wchar_t*>(pBase + pMod->ModuleNameRva + 4);
                                                modPath.assign(pStr, bLen / sizeof(wchar_t));
                                            }
                                        }
                                        pSession->ModuleNames.push_back(modPath);

                                        SymLoadModuleExW(
                                            pSession->SymbolProcessHandle,
                                            nullptr,
                                            modPath.empty() ? nullptr : modPath.c_str(),
                                            nullptr,
                                            pMod->BaseOfImage,
                                            pMod->SizeOfImage,
                                            nullptr,
                                            0);
                                    }
                                    recoveredAnything = true;
                                    break;
                                }
                            }
                        }
                    }
                }
            }
        }

        // 5. 启发式恢复 64 位连续内存列表 (Memory64ListStream)
        if (pSession->MemoryRanges.size() <= pSession->Threads.size())
        {
            const uint64_t scanLimit = (fileSize > sizeof(MINIDUMP_MEMORY64_LIST))
                ? std::min<uint64_t>(fileSize - sizeof(MINIDUMP_MEMORY64_LIST), 16 * 1024 * 1024)
                : 0;
            for (uint64_t offset = 0x200; offset < scanLimit; offset += 8)
            {
                const auto pMemList = reinterpret_cast<const MINIDUMP_MEMORY64_LIST*>(pBase + offset);
                if (pMemList->NumberOfMemoryRanges >= 5 && pMemList->NumberOfMemoryRanges <= 50000 &&
                    pMemList->BaseRva > offset && pMemList->BaseRva < fileSize)
                {
                    const uint64_t descArrayStart = offset + sizeof(MINIDUMP_MEMORY64_LIST);
                    const uint64_t neededDescSize = pMemList->NumberOfMemoryRanges * sizeof(MINIDUMP_MEMORY_DESCRIPTOR64);
                    if (descArrayStart + neededDescSize <= fileSize)
                    {
                        uint64_t runningRva = pMemList->BaseRva;
                        for (ULONG64 k = 0; k < pMemList->NumberOfMemoryRanges; ++k)
                        {
                            const auto pDesc = reinterpret_cast<const MINIDUMP_MEMORY_DESCRIPTOR64*>(pBase + descArrayStart + k * sizeof(MINIDUMP_MEMORY_DESCRIPTOR64));
                            DumpSessionInternal::DumpMemoryRange range{};
                            range.StartAddress = pDesc->StartOfMemoryRange;
                            range.DataSize = pDesc->DataSize;
                            range.Rva = runningRva;
                            pSession->MemoryRanges.push_back(range);
                            runningRva += pDesc->DataSize;
                        }
                        recoveredAnything = true;
                        break;
                    }
                }
            }
        }

        if (recoveredAnything)
        {
            pSession->IsHeuristicallyRecovered = true;
        }
    }
    int32_t InnerOpenDumpSession(const wchar_t* dumpFilePath, const wchar_t* symbolPath, void** outSession)
    {
        if (dumpFilePath == nullptr || outSession == nullptr)
        {
            return ERROR_INVALID_PARAMETER;
        }

        *outSession = nullptr;
        auto session = std::make_unique<DumpSessionInternal>();

        session->FileHandle = CreateFileW(
            dumpFilePath,
            GENERIC_READ,
            FILE_SHARE_READ,
            nullptr,
            OPEN_EXISTING,
            FILE_ATTRIBUTE_NORMAL,
            nullptr);

        if (session->FileHandle == INVALID_HANDLE_VALUE)
        {
            return static_cast<int32_t>(GetLastError());
        }

        LARGE_INTEGER liSize{};
        if (GetFileSizeEx(session->FileHandle, &liSize))
        {
            session->FileSize = static_cast<uint64_t>(liSize.QuadPart);
        }

        session->MappingHandle = CreateFileMappingW(
            session->FileHandle,
            nullptr,
            PAGE_READONLY,
            0,
            0,
            nullptr);

        if (session->MappingHandle == nullptr)
        {
            const auto err = static_cast<int32_t>(GetLastError());
            CloseHandle(session->FileHandle);
            return err;
        }

        session->BaseAddress = MapViewOfFile(
            session->MappingHandle,
            FILE_MAP_READ,
            0,
            0,
            0);

        if (session->BaseAddress == nullptr)
        {
            const auto err = static_cast<int32_t>(GetLastError());
            CloseHandle(session->MappingHandle);
            CloseHandle(session->FileHandle);
            return err;
        }

        session->SymbolProcessHandle = reinterpret_cast<HANDLE>(session.get());

        // 提取 Dump 所在的目录，拼接为最高优先级的本地符号搜索路径
        std::wstring dumpDir;
        std::wstring fullDumpPath(dumpFilePath);
        const auto lastSlash = fullDumpPath.find_last_of(L"\\/");
        if (lastSlash != std::wstring::npos)
        {
            dumpDir = fullDumpPath.substr(0, lastSlash);
        }

        std::wstring combinedSymbolPath;
        if (!dumpDir.empty())
        {
            combinedSymbolPath = dumpDir + L";" + dumpDir + L"\\Symbols;";
        }
        if (symbolPath != nullptr && wcslen(symbolPath) > 0)
        {
            combinedSymbolPath += symbolPath;
        }

        SymSetOptions(SYMOPT_DEFERRED_LOADS | SYMOPT_LOAD_LINES | SYMOPT_UNDNAME | SYMOPT_FAIL_CRITICAL_ERRORS | SYMOPT_AUTO_PUBLICS);
        SymInitializeW(session->SymbolProcessHandle, combinedSymbolPath.empty() ? nullptr : combinedSymbolPath.c_str(), FALSE);

        MINIDUMP_DIRECTORY* pDir = nullptr;
        void* pStream = nullptr;
        ULONG streamSize = 0;

        // 1. 读取系统信息流
        if (MiniDumpReadDumpStream(session->BaseAddress, SystemInfoStream, &pDir, &pStream, &streamSize) && pStream != nullptr)
        {
            if (streamSize >= sizeof(MINIDUMP_SYSTEM_INFO))
            {
                SafeReadMemory(pStream, &session->SystemInfo, sizeof(MINIDUMP_SYSTEM_INFO));
                session->HasSystemInfo = true;
            }
        }

        // 2. 读取异常流
        pStream = nullptr;
        streamSize = 0;
        if (MiniDumpReadDumpStream(session->BaseAddress, ExceptionStream, &pDir, &pStream, &streamSize) && pStream != nullptr)
        {
            if (streamSize >= sizeof(MINIDUMP_EXCEPTION_STREAM))
            {
                SafeReadMemory(pStream, &session->ExceptionStream, sizeof(MINIDUMP_EXCEPTION_STREAM));
                session->HasException = true;
            }
        }

        // 3. 读取模块流
        pStream = nullptr;
        streamSize = 0;
        if (MiniDumpReadDumpStream(session->BaseAddress, ModuleListStream, &pDir, &pStream, &streamSize) && pStream != nullptr)
        {
            const auto pModuleList = reinterpret_cast<MINIDUMP_MODULE_LIST*>(pStream);
            const auto pBase = reinterpret_cast<const uint8_t*>(session->BaseAddress);

            for (ULONG i = 0; i < pModuleList->NumberOfModules; ++i)
            {
                const auto& mod = pModuleList->Modules[i];
                session->Modules.push_back(mod);

                std::wstring modPath;
                if (mod.ModuleNameRva != 0 && IsValidRva(mod.ModuleNameRva, sizeof(ULONG32), session->FileSize))
                {
                    ULONG32 byteLen = 0;
                    if (SafeReadMemory(pBase + mod.ModuleNameRva, &byteLen, sizeof(ULONG32)) && byteLen > 0)
                    {
                        if (IsValidRva(mod.ModuleNameRva + sizeof(ULONG32), byteLen, session->FileSize))
                        {
                            const size_t charCount = byteLen / sizeof(wchar_t);
                            std::vector<wchar_t> strBuffer(charCount + 1, L'\0');
                            if (SafeReadMemory(pBase + mod.ModuleNameRva + sizeof(ULONG32), strBuffer.data(), byteLen))
                            {
                                modPath.assign(strBuffer.data(), charCount);
                            }
                        }
                    }
                }

                session->ModuleNames.push_back(modPath);

                SymLoadModuleExW(
                    session->SymbolProcessHandle,
                    nullptr,
                    modPath.empty() ? nullptr : modPath.c_str(),
                    nullptr,
                    mod.BaseOfImage,
                    mod.SizeOfImage,
                    nullptr,
                    0);
            }
        }

        // 4. 读取线程流
        pStream = nullptr;
        streamSize = 0;
        if (MiniDumpReadDumpStream(session->BaseAddress, ThreadListStream, &pDir, &pStream, &streamSize) && pStream != nullptr)
        {
            const auto pThreadList = reinterpret_cast<MINIDUMP_THREAD_LIST*>(pStream);
            for (ULONG i = 0; i < pThreadList->NumberOfThreads; ++i)
            {
                session->Threads.push_back(pThreadList->Threads[i]);
            }
        }

        // 5. 读取标准内存段流 (MemoryListStream: Stream 5) 与 64位内存段流 (Memory64ListStream: Stream 9)
        pStream = nullptr;
        streamSize = 0;
        if (MiniDumpReadDumpStream(session->BaseAddress, MemoryListStream, &pDir, &pStream, &streamSize) && pStream != nullptr)
        {
            const auto pMemList = reinterpret_cast<const MINIDUMP_MEMORY_LIST*>(pStream);
            for (ULONG i = 0; i < pMemList->NumberOfMemoryRanges; ++i)
            {
                const auto& mr = pMemList->MemoryRanges[i];
                session->MemoryRanges.push_back({ mr.StartOfMemoryRange, mr.Memory.DataSize, mr.Memory.Rva });
            }
        }

        pStream = nullptr;
        streamSize = 0;
        if (MiniDumpReadDumpStream(session->BaseAddress, Memory64ListStream, &pDir, &pStream, &streamSize) && pStream != nullptr)
        {
            const auto pMem64List = reinterpret_cast<const MINIDUMP_MEMORY64_LIST*>(pStream);
            uint64_t curRva = pMem64List->BaseRva;
            for (ULONG64 i = 0; i < pMem64List->NumberOfMemoryRanges; ++i)
            {
                const auto& mr = pMem64List->MemoryRanges[i];
                session->MemoryRanges.push_back({ mr.StartOfMemoryRange, mr.DataSize, curRva });
                curRva += mr.DataSize;
            }
        }

        // 7. 启发式容错诊断: 若关键流(异常、模块或线程)未能通过标准流目录获取，启动物理特征流自愈引擎
        if (!session->HasException || session->Modules.empty() || session->Threads.empty())
        {
            TryHeuristicRecoverDump(session.get());
        }

        *outSession = session.release();
        return ERROR_SUCCESS;
    }

    // 关闭 Dump 分析会话并安全释放句柄与映射内存 (全面 SEH 隔离保护)
    int32_t InnerCloseDumpSession(void* session)
    {
        if (session == nullptr)
        {
            return ERROR_INVALID_PARAMETER;
        }

        auto pSession = static_cast<DumpSessionInternal*>(session);
        {

            if (pSession->SymbolProcessHandle != nullptr)
            {
                SafeSymCleanup(pSession->SymbolProcessHandle);
                pSession->SymbolProcessHandle = nullptr;
            }

            if (pSession->BaseAddress != nullptr)
            {
                SafeUnmapView(pSession->BaseAddress);
                pSession->BaseAddress = nullptr;
            }

            if (pSession->MappingHandle != nullptr)
            {
                SafeCloseHandle(pSession->MappingHandle);
                pSession->MappingHandle = nullptr;
            }

            if (pSession->FileHandle != INVALID_HANDLE_VALUE)
            {
                SafeCloseHandle(pSession->FileHandle);
                pSession->FileHandle = INVALID_HANDLE_VALUE;
            }
        }

        SafeDeleteSession(pSession);
        return ERROR_SUCCESS;
    }

    // 获取系统环境与 CPU 架构信息
    int32_t InnerGetDumpSystemInfo(void* session, NativeSystemInfo* outInfo)
    {
        if (session == nullptr || outInfo == nullptr)
        {
            return ERROR_INVALID_PARAMETER;
        }

        auto pSession = static_cast<DumpSessionInternal*>(session);

        if (!pSession->HasSystemInfo)
        {
            return ERROR_NOT_FOUND;
        }

        if (!SafeExtractSystemInfo(pSession, outInfo))
        {
            return ERROR_READ_FAULT;
        }

        return ERROR_SUCCESS;
    }

    // 获取崩溃异常摘要
    int32_t InnerGetDumpExceptionInfo(void* session, NativeExceptionInfo* outException)
    {
        if (session == nullptr || outException == nullptr)
        {
            return ERROR_INVALID_PARAMETER;
        }

        auto pSession = static_cast<DumpSessionInternal*>(session);

        if (!pSession->HasException)
        {
            return ERROR_NOT_FOUND;
        }

        if (!SafeExtractExceptionInfo(pSession, outException))
        {
            return ERROR_READ_FAULT;
        }

        return ERROR_SUCCESS;
    }

    // 获取模块列表总数
    int32_t InnerGetDumpModuleCount(void* session, int32_t* outCount)
    {
        if (session == nullptr || outCount == nullptr)
        {
            return ERROR_INVALID_PARAMETER;
        }

        auto pSession = static_cast<DumpSessionInternal*>(session);

        *outCount = static_cast<int32_t>(pSession->Modules.size());
        return ERROR_SUCCESS;
    }

    // 获取单个模块详细信息与 PDB 加载状态
    int32_t InnerGetDumpModule(void* session, int32_t index, NativeModuleInfo* outModule)
    {
        if (session == nullptr || outModule == nullptr)
        {
            return ERROR_INVALID_PARAMETER;
        }

        auto pSession = static_cast<DumpSessionInternal*>(session);

        if (index < 0 || index >= static_cast<int32_t>(pSession->Modules.size()))
        {
            return ERROR_INVALID_INDEX;
        }

        ZeroMemory(outModule, sizeof(NativeModuleInfo));
        const auto& mod = pSession->Modules[index];
        outModule->BaseOfImage = mod.BaseOfImage;
        outModule->SizeOfImage = mod.SizeOfImage;
        outModule->CheckSum = mod.CheckSum;
        outModule->TimeDateStamp = mod.TimeDateStamp;

        const auto& fullPath = pSession->ModuleNames[index];
        wcsncpy_s(outModule->ImageName, 260, fullPath.c_str(), _TRUNCATE);

        const auto lastSlash = fullPath.find_last_of(L"\\/");
        if (lastSlash != std::wstring::npos)
        {
            wcsncpy_s(outModule->ModuleName, 260, fullPath.c_str() + lastSlash + 1, _TRUNCATE);
        }
        else
        {
            wcsncpy_s(outModule->ModuleName, 260, fullPath.c_str(), _TRUNCATE);
        }

        IMAGEHLP_MODULEW64 modInfo{};
        modInfo.SizeOfStruct = sizeof(IMAGEHLP_MODULEW64);
        if (SymGetModuleInfoW64(pSession->SymbolProcessHandle, mod.BaseOfImage, &modInfo))
        {
            wcsncpy_s(outModule->LoadedPdbName, 260, modInfo.LoadedPdbName, _TRUNCATE);
            outModule->IsSymbolLoaded = (modInfo.SymType != SymNone) ? 1 : 0;
        }

        return ERROR_SUCCESS;
    }

    // 获取线程列表总数
    int32_t InnerGetDumpThreadCount(void* session, int32_t* outCount)
    {
        if (session == nullptr || outCount == nullptr)
        {
            return ERROR_INVALID_PARAMETER;
        }

        auto pSession = static_cast<DumpSessionInternal*>(session);

        *outCount = static_cast<int32_t>(pSession->Threads.size());
        return ERROR_SUCCESS;
    }

    // 获取指定索引的线程 ID
    int32_t InnerGetDumpThreadId(void* session, int32_t index, uint32_t* outThreadId)
    {
        if (session == nullptr || outThreadId == nullptr)
        {
            return ERROR_INVALID_PARAMETER;
        }

        auto pSession = static_cast<DumpSessionInternal*>(session);

        if (index < 0 || index >= static_cast<int32_t>(pSession->Threads.size()))
        {
            return ERROR_INVALID_INDEX;
        }

        *outThreadId = pSession->Threads[index].ThreadId;
        return ERROR_SUCCESS;
    }

    // 根据内存虚拟地址解析符号、函数名、源码文件及行号
    int32_t InnerResolveAddressSymbol(void* session, uint64_t address, NativeSymbolResult* outSymbol)
    {
        if (session == nullptr || outSymbol == nullptr)
        {
            return ERROR_INVALID_PARAMETER;
        }

        auto pSession = static_cast<DumpSessionInternal*>(session);

        SafeResolveSymbol(pSession->SymbolProcessHandle, address, outSymbol);
        return ERROR_SUCCESS;
    }

    // 获取指定线程的调用栈并安全解析各层栈帧 (优先采用异常发生瞬间现场上下文 + StackWalk64 标准展开)
    int32_t InnerGetDumpThreadStackTrace(void* session, uint32_t threadId, int32_t maxFrames, NativeStackFrameInfo* outFrames, int32_t* outActualFrames)
    {
        if (session == nullptr || outFrames == nullptr || outActualFrames == nullptr || maxFrames <= 0)
        {
            return ERROR_INVALID_PARAMETER;
        }

        ZeroMemory(outFrames, maxFrames * sizeof(NativeStackFrameInfo));
        *outActualFrames = 0;

        auto pSession = static_cast<DumpSessionInternal*>(session);

        const MINIDUMP_THREAD* pTargetThread = nullptr;
        for (const auto& t : pSession->Threads)
        {
            if (t.ThreadId == threadId)
            {
                pTargetThread = &t;
                break;
            }
        }

        if (pTargetThread == nullptr)
        {
            return ERROR_NOT_FOUND;
        }

        const bool isCrashThread = pSession->HasException && (pSession->ExceptionStream.ThreadId == threadId);

        // 初始化当前线程的栈空间指针，供 StackWalk64 回调使用
        const auto& stackDesc = pTargetThread->Stack;
        pSession->ActiveStackStart = stackDesc.StartOfMemoryRange;
        pSession->ActiveStackSize = stackDesc.Memory.DataSize;
        pSession->ActiveStackData = nullptr;

        if (stackDesc.Memory.DataSize > 0 && IsValidRva(stackDesc.Memory.Rva, stackDesc.Memory.DataSize, pSession->FileSize))
        {
            pSession->ActiveStackData = reinterpret_cast<const uint8_t*>(pSession->BaseAddress) + stackDesc.Memory.Rva;
        }
        else
        {
            // 若 RVA 为 0，从 MemoryRanges (Stream 5 / 9) 中匹配包含当前线程栈起始地址的内存段
            for (const auto& mr : pSession->MemoryRanges)
            {
                if (pSession->ActiveStackStart >= mr.StartAddress &&
                    pSession->ActiveStackStart < mr.StartAddress + mr.DataSize)
                {
                    const uint64_t offset = pSession->ActiveStackStart - mr.StartAddress;
                    const uint64_t available = mr.DataSize - offset;
                    pSession->ActiveStackSize = (pSession->ActiveStackSize > 0) ? (std::min)(pSession->ActiveStackSize, available) : available;
                    if (IsValidRva(mr.Rva + offset, pSession->ActiveStackSize, pSession->FileSize))
                    {
                        pSession->ActiveStackData = reinterpret_cast<const uint8_t*>(pSession->BaseAddress) + mr.Rva + offset;
                    }
                    break;
                }
            }
        }

        if (pSession->SystemInfo.ProcessorArchitecture == PROCESSOR_ARCHITECTURE_AMD64)
        {
            CONTEXT ctx64{};
            bool hasValidContext = false;

            // 1. 如果是崩溃线程，优先从 ExceptionStream 获取发生异常瞬间的原始 CONTEXT
            if (isCrashThread && pSession->ExceptionStream.ThreadContext.Rva != 0)
            {
                const auto ctxRva = pSession->ExceptionStream.ThreadContext.Rva;
                if (IsValidRva(ctxRva, sizeof(CONTEXT), pSession->FileSize))
                {
                    const auto pCtxRaw = reinterpret_cast<const uint8_t*>(pSession->BaseAddress) + ctxRva;
                    if (SafeReadMemory(pCtxRaw, &ctx64, sizeof(CONTEXT)))
                    {
                        hasValidContext = true;
                    }
                }
            }

            // 2. 若未从 ExceptionStream 获取，则从线程流中获取线程上下文
            if (!hasValidContext && pTargetThread->ThreadContext.Rva != 0)
            {
                const auto ctxRva = pTargetThread->ThreadContext.Rva;
                if (IsValidRva(ctxRva, sizeof(CONTEXT), pSession->FileSize))
                {
                    const auto pCtxRaw = reinterpret_cast<const uint8_t*>(pSession->BaseAddress) + ctxRva;
                    if (SafeReadMemory(pCtxRaw, &ctx64, sizeof(CONTEXT)))
                    {
                        hasValidContext = true;
                    }
                }
            }

            if (!hasValidContext)
            {
                return ERROR_READ_FAULT;
            }

            // 如果是崩溃线程且存在明确的 ExceptionAddress，确保初始 RIP 指向真实崩溃指令
            if (isCrashThread && pSession->ExceptionStream.ExceptionRecord.ExceptionAddress != 0)
            {
                ctx64.Rip = pSession->ExceptionStream.ExceptionRecord.ExceptionAddress;
            }

            int32_t frameCount = 0;

            // 3. 使用 StackWalk64 沿着 PE 异常展开表进行标准栈展开
            STACKFRAME64 stackFrame{};
            stackFrame.AddrPC.Offset = ctx64.Rip;
            stackFrame.AddrPC.Mode = AddrModeFlat;
            stackFrame.AddrFrame.Offset = ctx64.Rbp;
            stackFrame.AddrFrame.Mode = AddrModeFlat;
            stackFrame.AddrStack.Offset = ctx64.Rsp;
            stackFrame.AddrStack.Mode = AddrModeFlat;

            CONTEXT walkContext = ctx64;
            std::vector<uint64_t> recordedAddresses;

            while (frameCount < maxFrames)
            {
                const uint64_t pc = stackFrame.AddrPC.Offset;
                if (pc == 0)
                {
                    break;
                }

                // 防御循环栈展开递归
                if (std::find(recordedAddresses.begin(), recordedAddresses.end(), pc) != recordedAddresses.end())
                {
                    break;
                }
                recordedAddresses.push_back(pc);

                auto& curFrame = outFrames[frameCount];
                curFrame.InstructionOffset = pc;
                curFrame.FrameOffset = stackFrame.AddrFrame.Offset;
                curFrame.StackOffset = stackFrame.AddrStack.Offset;
                curFrame.ReturnOffset = stackFrame.AddrReturn.Offset;

                NativeSymbolResult symResult{};
                SafeResolveSymbol(pSession->SymbolProcessHandle, pc, &symResult);
                wcsncpy_s(curFrame.ModuleName, 128, symResult.ModuleName, _TRUNCATE);
                wcsncpy_s(curFrame.FunctionName, 256, symResult.FunctionName, _TRUNCATE);
                wcsncpy_s(curFrame.SourceFile, 260, symResult.SourceFile, _TRUNCATE);
                curFrame.LineNumber = symResult.LineNumber;
                curFrame.Displacement = symResult.Displacement;

                ++frameCount;

                const BOOL walkSuccess = SafeStackWalk64(
                    IMAGE_FILE_MACHINE_AMD64,
                    pSession->SymbolProcessHandle,
                    reinterpret_cast<HANDLE>(static_cast<uintptr_t>(threadId)),
                    &stackFrame,
                    &walkContext,
                    MinidumpReadMemoryProc64,
                    SymFunctionTableAccess64,
                    SymGetModuleBase64,
                    nullptr);

                if (!walkSuccess)
                {
                    break;
                }
            }

            // 4. 如果 StackWalk64 展开层数较少（例如被内联或非标准调用阻断），辅以严格验证的启发式扫描补充深层帧
            if (frameCount < 5 && pSession->ActiveStackData != nullptr && pSession->ActiveStackSize >= sizeof(uint64_t))
            {
                const size_t wordCount = pSession->ActiveStackSize / sizeof(uint64_t);
                size_t startIndex = 0;
                if (ctx64.Rsp >= pSession->ActiveStackStart && ctx64.Rsp < pSession->ActiveStackStart + pSession->ActiveStackSize)
                {
                    startIndex = (ctx64.Rsp - pSession->ActiveStackStart) / sizeof(uint64_t);
                }

                for (size_t i = startIndex; i < wordCount && frameCount < maxFrames; ++i)
                {
                    uint64_t candidateAddr = 0;
                    if (!SafeReadMemory(pSession->ActiveStackData + i * sizeof(uint64_t), &candidateAddr, sizeof(uint64_t)))
                    {
                        break;
                    }

                    if (candidateAddr == 0 || std::find(recordedAddresses.begin(), recordedAddresses.end(), candidateAddr) != recordedAddresses.end())
                    {
                        continue;
                    }

                    bool inModule = false;
                    for (const auto& mod : pSession->Modules)
                    {
                        if (candidateAddr >= mod.BaseOfImage && candidateAddr < mod.BaseOfImage + mod.SizeOfImage)
                        {
                            inModule = true;
                            break;
                        }
                    }

                    if (inModule)
                    {
                        NativeSymbolResult symResult{};
                        SafeResolveSymbol(pSession->SymbolProcessHandle, candidateAddr, &symResult);

                        if (symResult.ModuleName[0] != L'\0')
                        {
                            recordedAddresses.push_back(candidateAddr);
                            auto& curFrame = outFrames[frameCount];
                            curFrame.InstructionOffset = candidateAddr;
                            curFrame.StackOffset = pSession->ActiveStackStart + i * sizeof(uint64_t);
                            curFrame.FrameOffset = 0;
                            curFrame.ReturnOffset = 0;

                            wcsncpy_s(curFrame.ModuleName, 128, symResult.ModuleName, _TRUNCATE);
                            wcsncpy_s(curFrame.FunctionName, 256, symResult.FunctionName, _TRUNCATE);
                            wcsncpy_s(curFrame.SourceFile, 260, symResult.SourceFile, _TRUNCATE);
                            curFrame.LineNumber = symResult.LineNumber;
                            curFrame.Displacement = symResult.Displacement;

                            ++frameCount;
                        }
                    }
                }
            }

            pSession->ActiveStackData = nullptr;
            pSession->ActiveStackStart = 0;
            pSession->ActiveStackSize = 0;

            *outActualFrames = frameCount;
            return ERROR_SUCCESS;
        }

        return ERROR_NOT_SUPPORTED;
    }

    // 对运行中进程捕获热快照 Dump
    DIAG_API int32_t IsDumpHeuristicallyRecovered(void* session, int32_t* outIsRecovered)
    {
        if (session == nullptr || outIsRecovered == nullptr)
        {
            return ERROR_INVALID_PARAMETER;
        }
        auto pSession = static_cast<DumpSessionInternal*>(session);
        *outIsRecovered = pSession->IsHeuristicallyRecovered ? 1 : 0;
        return ERROR_SUCCESS;
    }

    int32_t InnerCaptureProcessLiveDump(uint32_t processId, const wchar_t* dumpFilePath, int32_t dumpType)
    {
        if (dumpFilePath == nullptr)
        {
            return ERROR_INVALID_PARAMETER;
        }

        HANDLE hProcess = OpenProcess(PROCESS_QUERY_INFORMATION | PROCESS_VM_READ | PROCESS_DUP_HANDLE, FALSE, processId);
        if (hProcess == nullptr)
        {
            return static_cast<int32_t>(GetLastError());
        }

        HANDLE hFile = CreateFileW(
            dumpFilePath,
            GENERIC_WRITE,
            0,
            nullptr,
            CREATE_ALWAYS,
            FILE_ATTRIBUTE_NORMAL,
            nullptr);

        if (hFile == INVALID_HANDLE_VALUE)
        {
            const auto err = static_cast<int32_t>(GetLastError());
            CloseHandle(hProcess);
            return err;
        }

        MINIDUMP_TYPE mdType = MiniDumpNormal;
        if (dumpType == 1)
        {
            mdType = MiniDumpWithFullMemory;
        }
        else
        {
            // 包含私有读写内存与句柄数据，使 Dump 转储真实反映堆内存泄漏 (Private Bytes)
            mdType = static_cast<MINIDUMP_TYPE>(
                MiniDumpWithIndirectlyReferencedMemory |
                MiniDumpWithProcessThreadData |
                MiniDumpWithUnloadedModules |
                MiniDumpWithHandleData |
                MiniDumpWithPrivateReadWriteMemory |
                MiniDumpWithFullMemoryInfo);
        }

        const BOOL success = MiniDumpWriteDump(
            hProcess,
            processId,
            hFile,
            mdType,
            nullptr,
            nullptr,
            nullptr);

        const auto lastError = success ? ERROR_SUCCESS : static_cast<int32_t>(GetLastError());

        CloseHandle(hFile);
        CloseHandle(hProcess);

        return lastError;
    }

    DIAG_API int32_t OpenDumpSession(const wchar_t* dumpFilePath, const wchar_t* symbolPath, void** outSession)
    {
        __try { return InnerOpenDumpSession(dumpFilePath, symbolPath, outSession); }
        __except(EXCEPTION_EXECUTE_HANDLER) { return ERROR_READ_FAULT; }
    }

    DIAG_API int32_t CloseDumpSession(void* session)
    {
        __try { return InnerCloseDumpSession(session); }
        __except(EXCEPTION_EXECUTE_HANDLER) { return ERROR_READ_FAULT; }
    }

    DIAG_API int32_t GetDumpSystemInfo(void* session, NativeSystemInfo* outInfo)
    {
        __try { return InnerGetDumpSystemInfo(session, outInfo); }
        __except(EXCEPTION_EXECUTE_HANDLER) { return ERROR_READ_FAULT; }
    }

    DIAG_API int32_t GetDumpExceptionInfo(void* session, NativeExceptionInfo* outException)
    {
        __try { return InnerGetDumpExceptionInfo(session, outException); }
        __except(EXCEPTION_EXECUTE_HANDLER) { return ERROR_READ_FAULT; }
    }

    DIAG_API int32_t GetDumpModuleCount(void* session, int32_t* outCount)
    {
        __try { return InnerGetDumpModuleCount(session, outCount); }
        __except(EXCEPTION_EXECUTE_HANDLER) { return ERROR_READ_FAULT; }
    }

    DIAG_API int32_t GetDumpModule(void* session, int32_t index, NativeModuleInfo* outModule)
    {
        __try { return InnerGetDumpModule(session, index, outModule); }
        __except(EXCEPTION_EXECUTE_HANDLER) { return ERROR_READ_FAULT; }
    }

    DIAG_API int32_t GetDumpThreadCount(void* session, int32_t* outCount)
    {
        __try { return InnerGetDumpThreadCount(session, outCount); }
        __except(EXCEPTION_EXECUTE_HANDLER) { return ERROR_READ_FAULT; }
    }

    DIAG_API int32_t GetDumpThreadId(void* session, int32_t index, uint32_t* outThreadId)
    {
        __try { return InnerGetDumpThreadId(session, index, outThreadId); }
        __except(EXCEPTION_EXECUTE_HANDLER) { return ERROR_READ_FAULT; }
    }

    DIAG_API int32_t GetDumpThreadStackTrace(void* session, uint32_t threadId, int32_t maxFrames, NativeStackFrameInfo* outFrames, int32_t* outActualFrames)
    {
        __try { return InnerGetDumpThreadStackTrace(session, threadId, maxFrames, outFrames, outActualFrames); }
        __except(EXCEPTION_EXECUTE_HANDLER) { return ERROR_READ_FAULT; }
    }

    DIAG_API int32_t ResolveAddressSymbol(void* session, uint64_t address, NativeSymbolResult* outSymbol)
    {
        __try { return InnerResolveAddressSymbol(session, address, outSymbol); }
        __except(EXCEPTION_EXECUTE_HANDLER) { return ERROR_READ_FAULT; }
    }

    DIAG_API int32_t CaptureProcessLiveDump(uint32_t processId, const wchar_t* dumpFilePath, int32_t dumpType)
    {
        __try { return InnerCaptureProcessLiveDump(processId, dumpFilePath, dumpType); }
        __except(EXCEPTION_EXECUTE_HANDLER) { return ERROR_READ_FAULT; }
    }
}



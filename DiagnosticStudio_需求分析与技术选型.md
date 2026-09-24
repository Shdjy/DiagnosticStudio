# Diagnostic Studio —— Windows 程序诊断与可视化调试平台

> 项目定位：面向 Windows C#/C++ 工业上位机及长期运行程序的 **Dump 分析、源码定位、运行时监控、内存泄漏分析、资源泄漏分析和故障根因辅助定位平台**。
>
> 核心思想：**底层能力交给 C++，产品/UI/分析编排交给 C#，通过统一的数据模型把 Win32、DbgHelp、ETW、CLR、PDB 等底层信息转化为可理解的诊断结果。**

---

## 1. 项目目标

### 1.1 核心目标

构建一个类似“专用诊断工作台”的工具，而不是简单替代 WinDbg。

用户应该能够：

1. 拖入 `.dmp` 文件。
2. 自动识别进程、模块、线程、异常和调用栈。
3. 自动加载/匹配 PDB。
4. 将地址解析到 DLL → 函数 → 源文件 → 行号。
5. 自动分析异常栈和可疑代码位置。
6. 在源码浏览器中自动标记：
   - 🔴 高风险/关键问题
   - 🟠 高概率问题
   - 🟡 可疑问题
   - 🔵 普通信息
7. Attach 到正在运行的进程。
8. 实时监控：
   - CPU
   - 内存
   - Private Bytes
   - Working Set
   - Threads
   - Handles
   - GDI Objects
   - USER Objects
   - Modules
9. 分析 Managed Heap 和 Native Heap。
10. 发现内存、Handle、线程等资源持续增长。
11. 对两个时间点/两个 Dump 进行对比。
12. 生成 HTML/JSON 等诊断报告。
13. 通过规则引擎自动生成“问题候选”和“根因线索”。

---

# 2. 产品定位

## 2.1 不做什么

不要把项目目标定义为：

> “重新实现一个 WinDbg。”

WinDbg、DbgHelp、ETW、CLRMD 等已经提供了大量底层能力。

本项目应该定位为：

> **Windows 原生调试与诊断能力的可视化智能分析层。**

---

## 2.2 核心价值

普通调试工具：

```text
地址
↓
调用栈
↓
程序员自己分析
```

Diagnostic Studio：

```text
Dump
↓
符号解析
↓
调用栈
↓
源码定位
↓
运行状态分析
↓
规则引擎
↓
异常关联
↓
风险评分/分类
↓
给出问题线索
```

最终目标是让用户看到：

```text
🔴 Potential Null Pointer

Module:
    Module_PreformProcess.dll

Function:
    ProcessFrame()

Source:
    ProcessFrame.cpp:1287

Observed:
    data = 0x00000000

Access:
    data->data

Related stack:
    ProcessFrame()
    ProcessPreform()
    WorkerThread()

Confidence:
    High
```

而不是只看到：

```text
0x00007FF812345678
```

---

# 3. 总体架构

```text
┌───────────────────────────────────────────────┐
│              Diagnostic Studio               │
│                 C# / WPF                     │
├───────────────────────────────────────────────┤
│ UI / MVVM                                    │
│                                               │
│ Dump View                                    │
│ Memory View                                  │
│ Thread View                                  │
│ Module View                                  │
│ Source View                                  │
│ Timeline View                                │
│ Rule View                                    │
│ Report View                                  │
├───────────────────────────────────────────────┤
│ Diagnostic Core / Analysis Engine            │
├───────────────────────────────────────────────┤
│ Dump Engine │ Symbol │ Memory │ Rule │ Report │
├───────────────────────────────────────────────┤
│             Native Bridge                    │
├───────────────────────────────────────────────┤
│            C++ Native Engine                 │
├───────────────────────────────────────────────┤
│ DbgHelp │ Win32 │ ETW │ Heap │ NT API        │
└───────────────────────────────────────────────┘
```

---

# 4. 技术选型

| 模块 | 技术 |
|---|---|
| UI | C# WPF |
| UI 架构 | MVVM |
| Core | C# |
| Native Engine | C++ |
| Native API | Win32 / DbgHelp / ETW |
| Dump | MiniDump + DbgHelp |
| Symbol | PDB / Symbol Server |
| Managed 分析 | CLRMD |
| Native 分析 | Windows Heap / ETW / Native API |
| C# ↔ C++ | C ABI / PInvoke |
| IPC | Named Pipe |
| 数据库 | SQLite |
| 实时数据 | Channel / RingBuffer |
| 配置 | JSON |
| 日志 | DiagnosticLog |
| 图表 | WPF Chart |
| 插件 | AssemblyLoadContext |
| 报告 | HTML / JSON |
| C++ 构建 | CMake + Visual Studio 2022 |
| C# 构建 | .NET / Visual Studio 2022 |

---

# 5. 为什么采用 C# + C++

## 5.1 C# 负责

C# 更适合：

- WPF UI
- MVVM
- 数据绑定
- TreeView
- DataGrid
- 源码编辑器
- 图表
- 异步任务
- JSON
- SQLite
- 配置管理
- 报告生成
- 插件管理

---

## 5.2 C++ 负责

C++ 更适合：

- DbgHelp
- MiniDump
- StackWalk
- Symbol API
- PE
- Windows Heap
- VirtualQueryEx
- ReadProcessMemory
- ETW
- Native API
- Handle
- Thread
- Native Allocation
- 性能敏感的采集任务
- Native Agent

---

## 5.3 两者边界

推荐：

```text
C# UI
    ↓
C# Diagnostic Core
    ↓
Native Bridge
    ↓
C++ DiagnosticNative.dll
    ↓
Windows
```

不要让 WPF 层直接大量 P/Invoke。

C++ 应该向 C# 暴露稳定的、数据导向的 C ABI。

例如：

```cpp
extern "C"
{
    DIAG_API int OpenDump(
        const wchar_t* path,
        void** handle);

    DIAG_API int GetThreadCount(
        void* handle);

    DIAG_API int GetThreadInfo(
        void* handle,
        int index,
        ThreadInfo* info);
}
```

C#：

```csharp
[DllImport(
    "DiagnosticNative.dll",
    CallingConvention = CallingConvention.Cdecl,
    CharSet = CharSet.Unicode)]
private static extern int OpenDump(
    string path,
    out IntPtr handle);
```

---

# 6. 项目目录建议

```text
DiagnosticStudio/
│
├── src/
│   │
│   ├── DiagnosticStudio.UI/
│   │   ├── Views/
│   │   ├── ViewModels/
│   │   ├── Controls/
│   │   └── Resources/
│   │
│   ├── DiagnosticStudio.Core/
│   │   ├── Models/
│   │   ├── Services/
│   │   ├── Analysis/
│   │   ├── Rules/
│   │   ├── Symbols/
│   │   └── Reports/
│   │
│   ├── DiagnosticStudio.Dump/
│   │   ├── DumpReader/
│   │   ├── StackAnalyzer/
│   │   └── ExceptionAnalyzer/
│   │
│   ├── DiagnosticStudio.Memory/
│   │   ├── Managed/
│   │   ├── Native/
│   │   └── Handles/
│   │
│   ├── DiagnosticStudio.Monitor/
│   │   ├── Cpu/
│   │   ├── Memory/
│   │   ├── Threads/
│   │   └── ETW/
│   │
│   ├── DiagnosticStudio.Native/
│   │   └── C++ Project
│   │
│   └── DiagnosticStudio.Plugins/
│
├── tests/
│   ├── Core.Tests/
│   ├── Dump.Tests/
│   ├── Memory.Tests/
│   └── Native.Tests/
│
├── docs/
│   ├── architecture.md
│   ├── dump-analysis.md
│   ├── memory-analysis.md
│   └── native-agent.md
│
└── DiagnosticStudio.sln
```

---

# 7. 核心模块

# 7.1 Dump Analyzer

输入：

```text
.dmp
```

输出：

```text
DumpInfo
├── Process
├── Modules
├── Threads
├── Exceptions
├── Handles
├── MemoryRegions
└── Environment
```

功能：

- MiniDump 加载
- Dump 类型识别
- 进程信息
- 模块列表
- 线程列表
- 异常信息
- 调用栈
- 内存区域
- Handle 信息
- CLR 信息

---

# 7.2 Symbol Manager

负责：

```text
EXE/DLL
+
PDB
+
GUID/Age
```

验证：

```text
Module
    ↓
PDB
    ↓
GUID
    ↓
Age
    ↓
是否匹配
```

必须支持：

```text
Local PDB
Symbol Cache
Symbol Server
```

显示：

```text
Module_PreformProcess.dll

PDB:
    Module_PreformProcess.pdb

Status:
    ✓ Matched
```

或者：

```text
⚠ Symbol mismatch

Expected GUID:
    XXXXX

Actual GUID:
    YYYYY
```

---

# 7.3 Stack Analyzer

调用：

```text
StackWalk
```

生成：

```text
Thread
    ↓
Frame
    ↓
Module
    ↓
Function
    ↓
Source File
    ↓
Line
```

最终：

```text
Thread 27

WorkerThread()
    ↓
ProcessPreform()
    ↓
ProcessFrame()
    ↓
SaveResult()
    ↓
memcpy()
```

---

# 7.4 Source Resolver

负责：

```text
Instruction Address
        ↓
PDB
        ↓
Source File
        ↓
Line
```

源码显示：

```cpp
1285    memcpy(
1286        data->data,      // 🔴
1287        frame->data,
1288        frame->size);
```

支持：

- 自动定位
- 行号
- 函数
- 文件
- 模块
- 风险标记
- 点击调用栈跳转
- 点击问题跳转

---

# 8. 风险标记系统

不要简单使用：

```text
红 = 一定错误
黄 = 一定有问题
```

应该定义：

```text
Critical
High
Warning
Info
```

UI 可以映射为：

```text
🔴 Critical
🟠 High
🟡 Warning
🔵 Info
```

示例：

```text
Critical:
    AccessViolation
    StackOverflow

High:
    Null Pointer
    Invalid Memory Access
    Possible Use-After-Free

Warning:
    Memory Growth
    Handle Growth
    Thread Growth

Info:
    Module Loaded
    GC Activity
```

必须在 UI 中明确：

> 风险等级是分析器根据证据得出的诊断线索，不代表程序员源码一定存在对应缺陷。

---

# 9. Memory Analyzer

内存分析必须拆成：

```text
Managed Memory
+
Native Memory
```

---

# 9.1 Managed Memory

使用 CLRMD。

需要支持：

```text
GC Heap
Generation
Type
Size
Count
GC Root
Reference Chain
```

例如：

```text
System.Byte[]
    2.1 GB

System.String
    380 MB

Bitmap
    210 MB
```

点击：

```text
System.Byte[]
```

显示：

```text
GC Root
    ↓
BlockingCollection
    ↓
ReceiveDatas
    ↓
FrameData[]
    ↓
byte[]
```

重点不是：

> 谁占用了最多内存？

而是：

> 为什么这些对象还活着？

---

# 9.2 Native Memory

第一阶段：

```text
Process Memory
VirtualQueryEx
Heap
VirtualAlloc
```

第二阶段：

```text
ETW
+
Native Agent
```

最终目标：

```text
Allocation
    ↓
Address
    ↓
Size
    ↓
Thread
    ↓
Call Stack
    ↓
Lifetime
    ↓
Free
```

---

# 10. Native Allocation Tracking

建议设计：

```text
DiagnosticAgent.dll
```

用于注入/加载目标程序。

跟踪：

```text
malloc
calloc
realloc
new
delete
HeapAlloc
HeapFree
VirtualAlloc
VirtualFree
```

数据：

```cpp
struct AllocationInfo
{
    void* Address;
    size_t Size;

    uint64_t AllocationTime;
    uint64_t ThreadId;

    std::vector<void*> Stack;

    ModuleInfo Module;
};
```

输出：

```text
Allocation Site

FrameBuffer::Allocate()

Count:
    1,247

Total:
    9.84 GB

Freed:
    0

Potential Leak:
    High
```

注意：

普通 `new/delete` Hook 与 CRT/Heap/VirtualAlloc 之间存在重叠问题，第一版不要试图一次性覆盖所有分配路径。

---

# 11. Live Monitor

Attach 到进程：

```text
Process
    ↓
Monitor
```

监控：

```text
CPU
Memory
Private Bytes
Working Set
Commit
Threads
Handles
GDI Objects
USER Objects
Modules
```

实时显示：

```text
Private Bytes      3.82 GB
Working Set        2.91 GB
Threads            126
Handles            18,532
GDI Objects        8,231
USER Objects       1,210
```

---

# 12. 趋势分析

必须支持时间序列。

例如：

```text
Memory Growth
```

数据：

```text
10:00    1.2 GB
10:10    1.4 GB
10:20    1.8 GB
10:30    2.3 GB
10:40    3.1 GB
```

分析：

```text
Growth Rate:
    +285 MB / 10min
```

规则：

```text
PrivateBytesGrowth > X
HandleGrowth > X
GdiGrowth > X
ThreadGrowth > X
```

---

# 13. Thread Analyzer

监控：

```text
Thread Count
CPU
State
Wait
Stack
Lifetime
```

检测：

```text
线程数量持续增长
单线程 CPU 长时间过高
线程长时间等待
可能死锁
线程异常退出
```

死锁分析目标：

```text
Thread A
    owns Lock B
    waits Lock A

Thread B
    owns Lock A
    waits Lock B
```

形成：

```text
🔴 Possible Deadlock
```

---

# 14. Handle Analyzer

重点：

```text
HANDLE
GDI
USER
Socket
File
Event
Mutex
Semaphore
Thread
Process
```

关注：

```text
数量
增长速度
类型
创建线程
关联模块
```

例如：

```text
GDI Objects

Start:
    1,200

Current:
    8,231

Growth:
    +7,031
```

---

# 15. Dump Compare

支持：

```text
Dump A
Dump B
```

自动比较：

```text
Memory
Threads
Handles
Modules
Exceptions
Stacks
```

例如：

```text
Private Bytes

Dump A:
    1.2 GB

Dump B:
    3.8 GB

Difference:
    +2.6 GB
```

Top Growth：

```text
System.Byte[]     +1.4 GB
Bitmap            +520 MB
Native Heap       +630 MB
```

---

# 16. Timeline / Event Correlation

面向工业程序，建议建立统一事件时间线：

```text
Camera Trigger
Image Received
Algorithm Start
Algorithm End
PLC Signal
WMS Request
Save Start
Save End
Exception
GC
Allocation
```

例如：

```text
10:31:20.102 Camera Trigger
10:31:20.105 Image Received
10:31:20.107 Algorithm Start
10:31:20.162 Algorithm End
10:31:20.163 Save Start
10:31:20.205 Save End
```

用途：

```text
漏检
漏贴
信号丢失
算法超时
卡顿
偶发异常
```

---

# 17. Rule Engine

建议独立设计规则引擎。

规则示例：

```yaml
rules:

  - name: MemoryGrowth
    condition: "PrivateBytesGrowth > 500MB"
    level: "warning"

  - name: GDILeak
    condition: "GDIObjectsGrowth > 1000"
    level: "critical"

  - name: ThreadGrowth
    condition: "ThreadCountGrowth > 20"
    level: "warning"

  - name: NullPointer
    condition: "AccessViolation && NullAddressAccess"
    level: "high"
```

规则引擎输入：

```text
Dump
Monitor
ETW
CLR
Native Agent
```

输出：

```text
DiagnosticFinding
```

统一数据模型：

```text
DiagnosticFinding
├── Level
├── Title
├── Description
├── Evidence
├── Module
├── Function
├── SourceFile
├── Line
├── Stack
├── Confidence
└── RelatedObjects
```

---

# 18. Diagnostic Finding 数据模型

C#：

```csharp
public enum DiagnosticLevel
{
    Info,
    Warning,
    High,
    Critical
}

public sealed class DiagnosticFinding
{
    public DiagnosticLevel Level { get; set; }

    public string Title { get; set; }

    public string Description { get; set; }

    public string Module { get; set; }

    public string Function { get; set; }

    public string SourceFile { get; set; }

    public int Line { get; set; }

    public double Confidence { get; set; }

    public List<string> Evidence { get; set; }

    public List<StackFrameInfo> Stack { get; set; }
}
```

后续所有分析器尽量统一输出这个模型。

---

# 19. UI 设计

建议主界面：

```text
┌─────────────────────────────────────────────────────────────┐
│ Diagnostic Studio                                           │
├──────────────┬──────────────────────────────────────────────┤
│              │                                              │
│ Overview     │ Diagnostic Overview                          │
│              │                                              │
│ Dump         │ 🔴 2 Critical                                │
│              │ 🟠 4 High                                    │
│ Memory       │ 🟡 8 Warning                                 │
│              │                                              │
│ Threads      │ Memory    3.8 GB                             │
│              │ Threads   126                                │
│ Handles      │ Handles   18,532                             │
│              │                                              │
│ Modules      │                                              │
│              │ Top Findings                                 │
│ Source       │                                              │
│              │ 1. Null Pointer                              │
│ Timeline     │ 2. Memory Growth                             │
│              │ 3. Handle Growth                             │
│ Rules        │                                              │
│              │                                              │
│ Reports      │                                              │
└──────────────┴──────────────────────────────────────────────┘
```

---

# 20. Source Viewer

建议重点开发。

支持：

```text
源码
行号
语法高亮
断点样式标记
Diagnostic Finding
Stack Frame
```

示例：

```text
1284
1285 memcpy(
1286     data->data,       🔴
1287     frame->data,
1288     frame->size);
```

右侧：

```text
Problem
────────────────

Potential Null Pointer

data:
    0x00000000

Source:
    ProcessFrame.cpp:1286

Stack:
    ProcessFrame
    ProcessPreform
    WorkerThread
```

---

# 21. 自动报告

第一阶段推荐：

```text
HTML
JSON
```

报告：

```text
Diagnostic Report

1. Summary

2. Process Information

3. Exception

4. Threads

5. Call Stack

6. Symbols

7. Memory

8. Handles

9. Modules

10. Findings

11. Evidence

12. Recommendations
```

这里的 Recommendations 应该是技术排查建议，而不是直接断言根因。

---

# 22. 日志系统

Diagnostic Studio 自身也必须具备高质量日志。

建议：

```text
DiagnosticLog
```

记录：

```text
时间
线程
模块
级别
事件
耗时
异常
```

例如：

```text
[10:32:01.123]
[DumpAnalyzer]
[INFO]
Open dump: xxx.dmp

[10:32:01.152]
[SymbolManager]
[INFO]
PDB matched

[10:32:01.301]
[StackAnalyzer]
[INFO]
Thread analysis completed
Duration=149ms
```

分析操作必须记录耗时。

---

# 23. 性能设计

必须避免：

```text
UI线程
    ↓
Dump分析
    ↓
阻塞
```

应该：

```text
UI
 ↓
Async Service
 ↓
Worker
 ↓
Progress
 ↓
Result
```

实时监控使用：

```text
Channel<T>
```

或：

```text
RingBuffer
```

避免大量 UI Dispatcher 调用。

---

# 24. 数据模型分层

推荐：

```text
Raw Data
    ↓
Normalized Model
    ↓
Analysis Model
    ↓
Diagnostic Finding
    ↓
UI ViewModel
```

不要：

```text
Win32 API
    ↓
直接绑定 WPF
```

这样以后替换底层实现不会影响 UI。

---

# 25. Plugin Architecture

以后可以扩展：

```text
Plugins
├── CSharpPlugin
├── NativePlugin
├── BaslerPlugin
├── PLCPlugin
├── ModbusPlugin
├── OPCPlugin
└── IndustrialPlugin
```

插件可以提供：

```text
数据采集器
分析器
规则
UI页面
报告组件
```

第一版不需要实现完整插件系统，但 Core 层应该预留接口。

---

# 26. 第一阶段 MVP

不要一开始实现全部功能。

第一阶段目标：

```text
Dump
 ↓
PDB
 ↓
Thread
 ↓
Stack
 ↓
Exception
 ↓
Source
 ↓
Finding
 ↓
UI标记
```

必须实现：

- 打开 Dump
- 读取异常
- 读取线程
- 解析调用栈
- PDB 匹配
- 地址转换
- 源码定位
- 源码高亮
- Findings 列表
- HTML 报告

---

# 27. 第二阶段

加入：

```text
Live Monitor
```

实现：

- CPU
- Memory
- Private Bytes
- Working Set
- Threads
- Handles
- GDI
- USER
- Modules
- 实时曲线
- 阈值报警
- 数据保存

---

# 28. 第三阶段

加入：

```text
Managed Memory
```

实现：

- CLRMD
- GC Heap
- Object Count
- Object Size
- GC Root
- Reference Chain
- Dump Compare

---

# 29. 第四阶段

加入：

```text
Native Memory
```

实现：

- ETW
- Heap
- Allocation Tracking
- Native Agent
- Allocation Stack
- Allocation Lifetime
- Leak Candidate

---

# 30. 第五阶段

加入：

```text
Industrial Diagnostic
```

实现：

- Camera Event
- PLC Event
- Algorithm Event
- WMS Event
- Timeline
- Event Correlation
- 超时检测
- 信号丢失分析
- 业务规则

---

# 31. 第一版明确不做

为了避免项目失控，第一版不要做：

```text
❌ 完整 IDE
❌ 完整 WinDbg 替代品
❌ 在线修改进程内存
❌ 自动修改源码
❌ 自动修复 Bug
❌ 全量 malloc/new Hook
❌ 完整 ETW 分析器
❌ 完整 CLR Profiler
❌ 全平台支持
```

先完成：

```text
Dump
+
PDB
+
Stack
+
Source
+
Finding
```

---

# 32. 最关键的技术风险

## 32.1 PDB/源码不匹配

必须设计 Symbol Cache 和 Symbol Validation。

---

## 32.2 Dump 信息不完整

不同 Dump 类型包含的信息不同。

必须在 UI 中显示：

```text
Dump Type
Available Data
Missing Data
```

---

## 32.3 Native Memory 泄漏难以精确判断

不能把：

```text
内存增长
```

直接等价于：

```text
内存泄漏
```

需要区分：

```text
Cache
正常增长
Heap Fragmentation
对象生命周期长
真正 Leak
```

---

## 32.4 Hook 的侵入性

Native Agent 可能改变目标程序行为。

必须提供：

```text
Low Overhead
Normal
Deep Tracking
```

不同级别。

---

# 33. 推荐开发顺序

```text
Step 1
建立 Solution
    ↓

Step 2
C# WPF + MVVM
    ↓

Step 3
Diagnostic Core
    ↓

Step 4
C++ DiagnosticNative.dll
    ↓

Step 5
DbgHelp
    ↓

Step 6
Dump Loader
    ↓

Step 7
Thread / Stack
    ↓

Step 8
Symbol Manager
    ↓

Step 9
Source Resolver
    ↓

Step 10
Source Viewer
    ↓

Step 11
DiagnosticFinding
    ↓

Step 12
Rule Engine
    ↓

Step 13
HTML Report
    ↓

Step 14
Live Monitor
    ↓

Step 15
Managed Memory
    ↓

Step 16
Native Memory
    ↓

Step 17
ETW
    ↓

Step 18
Native Agent
    ↓

Step 19
Timeline
    ↓

Step 20
Industrial Plugin
```

---

# 34. 最终产品形态

最终目标：

```text
                  Diagnostic Studio
                         │
          ┌──────────────┼──────────────┐
          │              │              │
        Dump           Live           Trace
       Analysis       Monitor        Analysis
          │              │              │
          └──────────────┼──────────────┘
                         │
                  Analysis Engine
                         │
          ┌──────────────┼──────────────┐
          │              │              │
       Exception       Memory         Thread
       Analysis       Analysis        Analysis
          │              │              │
          └──────────────┼──────────────┘
                         │
                    Rule Engine
                         │
                  DiagnosticFinding
                         │
          ┌──────────────┼──────────────┐
          │              │              │
       Source         Timeline        Report
       Viewer         Analysis        Generator
```

最终用户看到的不是大量底层数据，而是：

```text
┌──────────────────────────────────────────────┐
│ Diagnostic Summary                           │
├──────────────────────────────────────────────┤
│                                              │
│ 🔴 2 Critical                                │
│ 🟠 4 High                                    │
│ 🟡 8 Warning                                 │
│                                              │
│ Top Findings                                 │
│                                              │
│ 🔴 Potential Null Pointer                    │
│    ProcessFrame.cpp:1287                    │
│                                              │
│ 🟠 Native Memory Growth                      │
│    +1.8 GB / 30 min                          │
│                                              │
│ 🟡 Handle Growth                             │
│    +8,231                                    │
│                                              │
│ [Open Source] [View Stack] [View Memory]     │
└──────────────────────────────────────────────┘
```

这应该成为整个项目的核心用户体验。

---

# 35. 核心原则

1. **C# 做产品，C++ 做底层。**
2. **不要重新实现 WinDbg，利用现有底层能力。**
3. **不要只展示原始数据，要进行分析和关联。**
4. **所有诊断结论必须保留 Evidence。**
5. **风险等级不等于确定的 Bug。**
6. **Managed Memory 和 Native Memory 必须分开分析。**
7. **源码定位必须建立在可靠的 PDB/源码匹配基础上。**
8. **UI 不直接依赖底层 Win32 API。**
9. **分析任务必须异步执行。**
10. **第一版只做 Dump → Symbol → Stack → Source → Finding。**
11. **Native Agent 和 ETW 放到后续阶段。**
12. **面向工业软件场景逐步增加 Camera/PLC/Algorithm/WMS 事件关联能力。**

---

# 36. 第一阶段验收标准

第一阶段完成后，应满足：

```text
[✓] 打开 .dmp
[✓] 显示进程信息
[✓] 显示异常
[✓] 显示线程
[✓] 显示调用栈
[✓] 加载 PDB
[✓] 验证 PDB
[✓] 地址 → 函数
[✓] 函数 → 源码
[✓] 源码 → 行号
[✓] 源码高亮
[✓] Finding 列表
[✓] 点击 Finding 跳转源码
[✓] 点击 Stack Frame 跳转源码
[✓] 导出 HTML
```

---

# 37. 项目最终目标

最终形成：

> **一个面向 Windows 工业软件的可视化故障诊断平台。**

核心能力：

```text
Crash
Memory
Thread
Handle
CPU
Dump
PDB
ETW
CLR
Native Heap
Source
Timeline
Rule
Report
```

核心工作流：

```text
发现问题
    ↓
采集数据
    ↓
自动分析
    ↓
发现异常模式
    ↓
定位模块
    ↓
定位函数
    ↓
定位源码
    ↓
关联运行时证据
    ↓
生成 Diagnostic Finding
    ↓
工程师确认根因
```

最终原则：

> **工具负责缩短“从现象到证据”的距离，最终的 Bug 判断仍由工程师根据证据确认。**

# Diagnostic Studio —— Windows 程序诊断与可视化调试平台

[![Platform](https://img.shields.io/badge/Platform-Windows%2010%2F11%20(x64)-blue.svg?style=flat-square&logo=windows)](https://github.com)
[![Framework](https://img.shields.io/badge/.NET-8.0--windows-512BD4.svg?style=flat-square&logo=dotnet)](https://dotnet.microsoft.com/)
[![Language](https://img.shields.io/badge/C%2B%2B-17%20(MSVC)-00599C.svg?style=flat-square&logo=c%2B%2B)](https://visualstudio.microsoft.com/)
[![UI](https://img.shields.io/badge/UI-WPF%20Fluent%20Dark-0078D7.svg?style=flat-square)](https://github.com)
[![Release](https://img.shields.io/badge/Release-v1.2.7-green.svg?style=flat-square)](https://github.com)
[![Tests](https://img.shields.io/badge/Tests-30%2F30%20Passed-brightgreen.svg?style=flat-square)](https://github.com)

> 面向 Windows C#/C++ 工业上位机及长期运行程序的 **Dump 智能分析、源码定位标红标黄、运行期进程监控、内存与资源泄漏分析、双 Dump 差分对比与故障根因辅助诊断平台**。

---

## 目录

- [一、 系统架构与分层设计](#一-系统架构与分层设计)
- [二、 核心特性与解决痛点](#二-核心特性与解决痛点)
  - [1. 拖拽 Dump 自动分析与“责任代码穿透标红”](#1-拖拽-dump-自动分析与责任代码穿透标红)
  - [2. 多维全覆盖智能规则引擎](#2-多维全覆盖智能规则引擎)
  - [3. 可视化符号路径配置与 PDB 自动加载](#3-可视化符号路径配置与-pdb-自动加载)
  - [4. 运行中进程无侵入实时监控与极速检索](#4-运行中进程无侵入实时监控与极速检索)
  - [5. WindowChrome 沉浸式暗黑界面](#5-windowchrome-沉浸式暗黑界面)
  - [6. 托管堆与原生堆深度差分引擎](#6-托管堆与原生堆深度差分引擎)
  - [7. 独立存储结构与中间文件一键清理](#7-独立存储结构与中间文件一键清理)
  - [8. 自包含专业诊断报告导出](#8-自包含专业诊断报告导出)
  - [9. 截断与未闭合 Dump 启发式物理流特征自愈](#9-截断与未闭合-dump-启发式物理流特征自愈)
- [三、 快速上手与获取方式](#三-快速上手与获取方式)
  - [方式 A：免安装绿色便携版（推荐）](#方式-a免安装绿色便携版推荐)
  - [方式 B：从源码编译构建](#方式-b从源码编译构建)
- [四、 工程结构说明](#四-工程结构说明)
- [五、 版本变更记录](#五-版本变更记录)

---

## 一、 系统架构与分层设计

Diagnostic Studio 遵循面向对象设计原则与依赖倒置规范（DIP），**底层核心 Native 能力由 C++17 封装，业务编排、规则推导与交互呈现交由 C# WPF**：

```text
┌─────────────────────────────────────────────────────────────┐
│                 DiagnosticStudio.UI                         │
│           C# WPF (MVVM / Fluent Dark Theme)                 │
│                                                             │
│   Overview    DumpView    SourceViewer    LiveMonitor       │
│   DiffView    ReportView  MetricLiveChart Logs              │
├─────────────────────────────────────────────────────────────┤
│                 DiagnosticStudio.Core                       │
│  DiagnosticFinding | RuleEngine | Reports | SystemLogger    │
├───────────────────┬───────────────────┬─────────────────────┤
│DiagnosticStudio.  │DiagnosticStudio.  │DiagnosticStudio.    │
│      Dump         │      Memory       │     Monitor         │
│ SuspectCodeLocator│   DumpComparator  │  LiveProcessMonitor │
│ DumpAnalyzer      │   DeltaAnalysis   │  LeakTrendDetector  │
├───────────────────┴───────────────────┴─────────────────────┤
│                Native Interop (C ABI Bridge)                │
├─────────────────────────────────────────────────────────────┤
│             DiagnosticNative.dll (C++17)                    │
│      DbgHelp / MiniDump / PDB / StackScan / SEH Isolation   │
└─────────────────────────────────────────────────────────────┘
```

---

## 二、 核心特性与解决痛点

### 1. 拖拽 Dump 自动分析与“责任代码穿透标红”
- **免除繁琐 WinDbg 命令**：直接拖入 `.dmp` 文件（支持全窗口即拖即放与全景放行），系统异步解析进程环境、崩溃异常记录、所有线程列表与调用栈；
- **真实现场还原与调用栈展开**：
  - 优先从 `MINIDUMP_EXCEPTION_STREAM.ThreadContext` 提取发生异常瞬间的原始 CPU 寄存器现场，杜绝被异常分发逻辑篡改；
  - 结合 PE `.pdata` / `UNWIND_INFO` 展开表进行标准栈展开 (`StackWalk64`)，确保指令指针与崩溃源头分毫不差；
  - 自动将 Dump 所在目录、`Symbols` 目录及上一级目录加入符号搜索链，本地 PDB 100% 自动识别加载并解析源码文件与行号；
- **业务责任根因反向归咎算法 (`SuspectCodeLocator`)**：
  - 当崩溃在 Windows 系统库（如 `ucrtbase.dll!abort`、`ntdll.dll!RtlReportCriticalFailure`、`kernelbase.dll!RaiseException`）内部爆发时，系统自动识别底层只是拦截触发点，智能向上回溯定位首个调用系统库的**用户业务责任帧 (`IsRootCause`)**；
  - 🔴 **业务责任崩溃点 (Critical)**：将用户责任代码整行红底高亮，行尾显示 `[🔴 崩溃责任根因]`，双击直接在源码视图定位用户业务代码；
  - 🟡 **业务入口上下文 (Suspect/Context)**：识别外层调用入口，整行黄底高亮；
- **工业级高鲁棒性隔离**：
  - **Native SEH 隔离保护**：`DiagnosticNative.cpp` 全面对 `SafeStackWalk64`、`MinidumpReadMemoryProc64` 内存读取及符号解析进行结构化异常（SEH）拦截，彻底杜绝底层 DbgHelp 抛出访问违例击穿至 CoreCLR 导致闪退；
  - **UI 异步安全渲染**：`SourceCodeViewer` 源码阅读器全面接入响应式数据流，异步排版杜绝 WPF 虚拟化死锁，并在应用全局挂载未捕获异常防御拦截。

### 2. 多维全覆盖智能规则引擎
- **空指针解引用规则 (`NullPointerRule`)**：识别非法访问地址落入 `0x0` ~ `0x10000` 空指针保护页；
- **野指针与 Use-After-Free 规则 (`AccessViolationRule`)**：自动识别 `0xFEEEFEEE` (堆已释放)、`0xCDCDCDCD` 等经典调试内存魔数；
- **栈缓冲区破坏与安全保护违例 (`BufferOverflowRule`)**：识别 `0xC0000409` (STATUS_STACK_BUFFER_OVERRUN / `__fastfail` / `/GS` 栈金丝雀破坏)；
- **未捕获标准 C++ 异常 (`UnhandledCppExceptionRule`)**：识别 `0xE06D7363` (`throw std::runtime_error` 等未处理 C++ 异常)；
- **算术除零故障 (`DivideByZeroRule`)**：识别 `0xC0000094` 整数除零与浮点除零；
- **堆损坏与重复释放 (`HeapCorruptionRule`)**：识别 `0xC0000374` (STATUS_HEAP_CORRUPTION / Double Free)；
- **断言与断点规则 (`AssertOrBreakpointRule`)**：识别 `0x80000003` (`assert()` 断言失败 / `DebugBreak`)；
- **挂起与卡死诊断 (`HangOrDeadlockRule`)**：针对无异常流卡死转储，检测主线程等待与锁争用；
- **全能托底诊断 (`GeneralCrashFallbackRule`)**：兜底捕获未知崩溃异常，确保任何有异常的转储绝对不会出现“0 项风险”；
- **完整证据链呈现**：每条诊断不仅输出结论，更提供详细 Evidence 证据链与现场排查建议。

### 3. 可视化符号路径配置与 PDB 自动加载
- **常驻符号配置栏**：主界面全局常驻“🏷️ 调试符号路径 (PDB)”配置栏，支持 Consolas 等宽字体文本自由编辑与双向数据绑定；
- **本地 PDB 自动识别 + 手动一键选取**：自动检索 Dump 所在目录；同时提供【📁 选择 PDB 目录】操作按钮快速选取自定义输出路径；
- **无缝重新分析**：修改或增补 PDB 路径后，点击【⚡ 重新分析】按钮即可就地重新执行全链路 Dump 逆向分析。

### 4. 运行中进程无侵入实时监控与极速检索
- **进程模糊检索与 PID 快速过滤**：顶部提供 `[🔍 查找进程]` 输入框，支持输入进程名称或数字 PID 实时筛选目标进程；
- **底层指标无损采样**：
  - **Private Bytes（私有提交内存）**：监控内存泄漏的核心指标；
  - **Working Set（物理工作集）**；
  - **Handle Count（系统句柄数）**；
  - **GDI Objects（GDI 图元对象）**：针对 Windows 10,000 个硬上限监控界面雪崩；
  - **Thread Count 与 CPU 使用率**；
- **线性回归泄漏识别算法 (`LeakTrendDetector`)**：通过滑动窗口计算指标斜率，当私有内存或 GDI 单调持续攀升且无回落时，主动发出橙色/红色泄漏警报；
- **一键无损热快照 (`LiveSnapshotService`)**：现场发现泄漏或异常走势时，点击【📸 抓取快照并分析】，瞬时生成 MiniDump 并自动加载到 Dump 分析引擎中进行深挖。

### 5. WindowChrome 沉浸式暗黑界面
- **无缝深色顶栏**：基于 WPF `WindowChrome` 自定义深色顶栏，消除系统原生非客户区刺眼白条；
- **高质感窗体控制按钮**：集成暗黑风格最小化、最大化/还原、关闭（悬停红底）系统控制按钮；
- **DWM 暗黑沉浸**：调用 Win32 DWM API 启用 `DWMWA_USE_IMMERSIVE_DARK_MODE`，系统边框与阴影全域契合深色主题。

### 6. 托管堆与原生堆深度差分引擎
- **彻底超越传统静态对比，直击具体增长对象与源码行**：
  - **托管堆差分分析 (`ManagedHeapAnalyzer`)**：基于 `Microsoft.Diagnostics.Runtime` (ClrMD 3.x) 深度遍历 GC 堆，按增量体积 ($\Delta Bytes$) 输出 Top 对象排行榜；
  - **原生虚拟内存段差分分析 (`NativeMemoryAnalyzer`)**：解析 MiniDump 二进制流（`MemoryInfoListStream` 与 `Memory64ListStream`），提取所有 `MEM_COMMIT | MEM_PRIVATE` 私有堆段的基地址、段大小与变动状态；
  - **启发式线程栈扫描与代码泄漏点定位 (`LocateSuspectCode`)**：全面扫描所有活跃线程调用栈，结合用户源码特征，精准归咎定位到责任模块、责任函数、源文件与行号（如 `CppCrashTarget.exe!SlowMemoryLeakScenario::Execute (MemoryLeakScenarios.cpp:126)`）；
  - **一键直达源码**：快照对比顶部醒目展示【🎯 疑似内存泄漏责任点】，配备【⚡ 在源码编辑器中定位】快捷按钮，点击直接切换至【源码定位】并在该行代码高亮呈现！

> [!TIP]
> **排查闭环推荐**：
> 1. 监控发现内存持续上涨，点击【📸 抓取快照并分析】生成基线快照 A；
> 2. 持续运行数分钟观察内存净增后，再次抓取对比快照 B；
> 3. 进入【⚖️ 快照对比 (Diff)】，加载快照 A 与 B，系统自动计算净增明细并精准锁定代码泄漏点具体源码行！

### 7. 独立存储结构与中间文件一键清理
- **独立工作区目录 (`DiagnosticsData/`)**：结构化管理排查数据：
  - **快照专区 (`DiagnosticsData/Snapshots/`)**：热快照按 `{进程名}_{PID}_{时间戳}.dmp` 命名，多进程独立存储；
  - **日志专区 (`DiagnosticsData/Logs/`)**：系统运行日志按天分卷输出 (`DiagnosticStudio_yyyyMMdd.log`)；
  - **报告专区 (`DiagnosticsData/Reports/`)**：HTML/JSON 导出诊断报告集中归档；
- **中间文件占用监控与一键清理**：
  - 在【📋 系统日志】页面顶部实时呈现快照、日志、报告文件的数量与体积占用；
  - 提供【🧹 清理中间文件】按钮，一键安全回收历史快照与旧文件（自动保护当天正在写入的活动日志）。

### 8. 自包含专业诊断报告导出
- 支持一键导出暗黑科技风格的独立自包含 **HTML 报告** 与 **JSON 报告**，包含风险大卡片、异常摘要、证据链与崩溃调用栈表格。

### 9. 截断与未闭合 Dump 启发式物理流特征自愈
- **应对生产环境未收尾 Dump 难题**：在工业现场或服务器环境中，当程序崩溃触发 Full Dump 写入时，守护进程/看门狗超时强杀（TerminateProcess）常导致 MiniDump 头部的流目录索引表未能完成回写（全 0 占位符），造成市面上常规调试工具无法识别任何流；
- **底层物理特征扫描与自愈还原 (`TryHeuristicRecoverDump`)**：
  - **SystemInfo / ExceptionStream 自愈**：自动扫描定位底层 `MINIDUMP_SYSTEM_INFO` 与 `MINIDUMP_EXCEPTION_STREAM`，提取真实崩溃码（如 `0xC0000005`、`0xC0000374`）、故障线程 ID、崩溃指令地址与操作数；
  - **ThreadList / ModuleList 智能重组**：基于上下文特征还原所有线程结构与模块加载列表，并挂载符号引擎；
  - **智能推导诊断预警**：触发 `CorruptedDumpRecoveryRule`，在 UI 呈现故障分析结果的同时提示用户检查 Dump 截断来源（如看门狗超时设置）。

---

## 三、 快速上手与获取方式

### 方式 A：免安装绿色便携版（推荐）

本项目已发布开箱即用的 Release 免安装版本（位于 `release/` 目录）：

| 规格类型 | 分发包文件 | 压缩包体积 | 特点与适用场景 |
| :--- | :--- | :--- | :--- |
| **自包含完整版** *(推荐)* | `DiagnosticStudio_v1.2.7_win-x64_Portable.zip` | ~68 MB | **开箱即用**：内嵌完整 .NET 8 运行时，无需安装任何依赖环境，适合现场上位机、无网络工控机与生产服务器。 |
| **轻量依赖版** | `DiagnosticStudio_v1.2.7_win-x64_Lite.zip` | ~1.4 MB | **极致轻巧**：适合个人开发机或已安装 .NET 8 Desktop Runtime 的环境。 |

**使用说明**：
1. 下载并解压上述任意 `.zip` 压缩包到任意本地文件夹；
2. 双击运行 `DiagnosticStudio.UI.exe`；
3. 将 `.dmp` 转储文件拖入窗口，或切换至【进程监控】页面连接目标进程即可开始诊断。

---

### 方式 B：从源码编译构建

#### 环境依赖
- Windows 10 / 11 (x64)
- .NET 8.0 SDK 或更高版本
- Visual Studio 2022 (包含 C++ 桌面开发工作负载与 MSVC 工具链)
- CMake 3.20 或更高版本

#### 1. 编译 C++ Native 底层库
```powershell
cmake -B "src/DiagnosticStudio.Native/build" -S "src/DiagnosticStudio.Native" -A x64
cmake --build "src/DiagnosticStudio.Native/build" --config Release
```

#### 2. 编译 C# 解决方案
```powershell
dotnet build DiagnosticStudio.sln -c Release
```

#### 3. 运行自动化单元测试套件
```powershell
dotnet test tests/DiagnosticStudio.Tests/DiagnosticStudio.Tests.csproj -c Release --no-build
```

#### 4. 运行客户端
```powershell
.\src\DiagnosticStudio.UI\bin\Release\net8.0-windows\DiagnosticStudio.UI.exe
```

---

## 四、 工程结构说明

```text
WinDbgTool/
├── release/                        # Release 免安装发布产物目录 (Portable zip & 解压包)
├── samples/                        # 完全独立的测试靶场程序（用于调试工具功能验证与压测）
│   ├── CSharpCrashTarget/          # C# .NET 8 独立测试靶场（崩溃Dump定位、慢速受控内存泄漏、句柄泄漏、死锁）
│   ├── CppCrashTarget/             # C++17 Native 独立测试靶场（空指针解引用红黄高亮、UAF、栈溢出、GDI泄漏）
│   ├── build_all.ps1               # 靶场一键构建脚本
│   └── README.md                   # 靶场详细测试场景与安全防护说明文档
├── src/
│   ├── DiagnosticStudio.Native/    # C++17 动态链接库，封装 DbgHelp / MiniDump / PDB / 启发式自愈与栈扫描
│   ├── DiagnosticStudio.Core/      # C# 业务模型、规则引擎、HTML/JSON报告生成器、系统日志
│   ├── DiagnosticStudio.Dump/      # C# Dump 分析引擎、Native Bridge、可疑代码定位器
│   ├── DiagnosticStudio.Memory/    # C# 内存分析与双 Dump 差分比较器
│   ├── DiagnosticStudio.Monitor/   # C# 实时进程监控器、线性回归泄漏算法、热快照触发器
│   └── DiagnosticStudio.UI/        # C# WPF 现代化桌面客户端（MVVM、沉浸式顶栏、实时折线图、源码阅读器）
├── tests/
│   └── DiagnosticStudio.Tests/     # xUnit 自动化单元测试套件（覆盖规则引擎、趋势算法、差分、报告及真实 Dump）
├── DiagnosticStudio_需求分析与技术选型.md # 原始架构与技术选型规格书
├── DiagnosticStudio.sln            # 统一解决方案文件
├── README.md                       # 项目说明文档
└── CHANGENOTE.md                   # 版本变更记录
```

> **注意**：为保持仓库精简并规避 GitHub 大文件（>100MB Dump 转储）限制，`tests/`（单测用例与样本转储）与 `samples/`（测试靶场与生成转储）在本地保留用于调试，不纳入远程 Git 版本控制。

---

## 五、 版本变更记录

详细的版本迭代与演进履历请参阅 [CHANGENOTE.md](CHANGENOTE.md)。

- **v1.2.7 (2026-10-29)**: 增加截断与未闭合 Dump 启发式物理流特征自愈恢复机制（Heuristic Dump Recovery）；
- **v1.2.6 (2026-09-24)**: 剥离测试工程与大文件转储，重构 Git 提交树，解除 GitHub 推送大小限制；
- **v1.2.5 (2026-09-24)**: 窗体标题栏品牌恢复经典样式，系统图标四周白色边缘透明化；
- **v1.2.4 (2026-09-24)**: 应用程序图标换新，根治 Baml2006 解码兼容性问题；
- **v1.0.0 ~ v1.2.3**: 基础诊断、源码高亮定位、实时进程监控、双 Dump 差分及报告导出功能建立。

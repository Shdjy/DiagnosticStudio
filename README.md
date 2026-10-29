# Diagnostic Studio —— Windows 程序诊断与可视化调试平台

[![Platform](https://img.shields.io/badge/Platform-Windows%2010%2F11%20(x64)-blue.svg?style=flat-square&logo=windows)](https://github.com)
[![Framework](https://img.shields.io/badge/.NET-8.0--windows-512BD4.svg?style=flat-square&logo=dotnet)](https://dotnet.microsoft.com/)
[![Language](https://img.shields.io/badge/C%2B%2B-17%20(MSVC)-00599C.svg?style=flat-square&logo=c%2B%2B)](https://visualstudio.microsoft.com/)
[![UI](https://img.shields.io/badge/UI-WPF%20Fluent%20Dark-0078D7.svg?style=flat-square)](https://github.com)
[![Release](https://img.shields.io/badge/Release-v2.0.0-green.svg?style=flat-square)](https://github.com)
[![Tests](https://img.shields.io/badge/Tests-45%2F45%20Passed-brightgreen.svg?style=flat-square)](https://github.com)

> 面向 Windows C#/C++ 工业上位机及长期运行程序的 **Dump 智能分析、源码定位标红标黄、运行期进程监控、内存与资源泄漏分析、双 Dump 差分对比与故障根因辅助诊断平台**。

---

## 目录

- [一、 系统架构与分层设计](#一-系统架构与分层设计)
- [二、 核心特性与解决痛点](#二-核心特性与解决痛点)
  - [1. 拖拽 Dump 自动分析与“责任代码穿透标红”](#1-拖拽-dump-自动分析与责任代码穿透标红)
  - [2. 根因定位语义拆分与故障归因链条](#2-根因定位语义拆分与故障归因链条)
  - [3. 多维可信规则引擎与透明执行审计](#3-多维可信规则引擎与透明执行审计)
  - [4. Dump 完整性自检与符号质量显式分级](#4-dump-完整性自检与符号质量显式分级)
  - [5. 工业上位机运行事件关联与统一时间线](#5-工业上位机运行事件关联与统一时间线)
  - [6. 线程并发、死锁与卡滞线程差分分析](#6-线程并发死锁与卡滞线程差分分析)
  - [7. 故障案例库沉淀与相似故障检索](#7-故障案例库沉淀与相似故障检索)
  - [8. 运行中进程无侵入实时监控与防抖泄漏检测](#8-运行中进程无侵入实时监控与防抖泄漏检测)
  - [9. 虚拟内存区域差分与堆分析分层](#9-虚拟内存区域差分与堆分析分层)
  - [10. 统一结构化证据模型与隐私数据脱敏报告](#10-统一结构化证据模型与隐私数据脱敏报告)
  - [11. 截断与未闭合 Dump 启发式物理流特征自愈](#11-截断与未闭合-dump-启发式物理流特征自愈)
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
│  EvidenceItem   │ RuleEngine & Audit  │ ConcurrencyAnalyzer │
│  IndustrialTime │ Knowledge & Cases   │ Reports & Sanitizer │
├───────────────────┬───────────────────┬─────────────────────┤
│DiagnosticStudio.  │DiagnosticStudio.  │DiagnosticStudio.    │
│      Dump         │      Memory       │     Monitor         │
│ SuspectCodeLocator│  DumpComparator   │ LiveProcessMonitor  │
│ DumpAnalyzer      │  NativeRegionDiff │ LeakTrendDetector   │
│ TaskManager       │  ManagedHeapDiff  │ Options & Cooldown  │
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

### 2. 根因定位语义拆分与故障归因链条
- **避免将崩溃现场误判为根因**：建立 `CodeLocationRole` 角色归因体系：
  - **异常指令点 (`ExceptionInstruction`)**：崩溃发生的硬件指令位置；
  - **直接触发点 (`DirectTriggerCall`)**：业务代码中直接调用底层库的栈帧；
  - **破坏上下文点 (`SuspectBusinessCode`)**：更外层的业务入口或可能造成状态损坏的逻辑；
- **全链路归因实体 (`CodeLocationChain`)**：
  - 串联异常指令、直接触发点与候选根因；
  - 标明确定性等级（`ConfirmedRootCause` 确凿根因、`ProbableHypothesis` 诊断假设、`InsufficientEvidence` 证据不足）；
  - 自动输出未确认假设与建议进行的现场验证步骤。

### 3. 多维可信规则引擎与透明执行审计
- **拒绝规则异常静默被吞**：
  - 记录单条规则执行状态 (`RuleExecutionRecord`)：成功（Success）、失败（Failed）、跳过（Skipped），记录耗时、异常类型与完整堆栈；
  - 计算整体分析状态（`Success` / `PartialSuccess` / `Failed`）；
- **动态置信度评估**：根据异常参数、特异性内存标记、源码行号可用性、Dump 完整性综合打分，杜绝写死高置信度；
- **结构化证据链输出**：每条诊断明确拆分为已确认事实（Confirmed Facts）、辅助证据（Supporting Evidence）、诊断假设（Hypotheses）与验证步骤（Verification Steps）。

### 4. Dump 完整性自检与符号质量显式分级
- **Dump 状态明确标识 (`DumpIntegrityInfo`)**：区分完整（Complete）、部分可用（PartiallyAvailable）、启发式恢复（HeuristicallyRecovered）与损坏（Corrupted）；
- **各物理流状态报告**：显式检验系统信息流、异常流、模块流、线程流与内存流状态；
- **符号加载质量明细 (`SymbolQualityInfo`)**：区分匹配符号（Matched PDB）、仅导出表（Export Table Only）、符号不匹配（Mismatched PDB）与加载失败。

### 5. 工业上位机运行事件关联与统一时间线
- **高频业务事件记录 (`IndustrialTimelineRecorder`)**：面向工业 7x24 高频检测设计，采用无 GC 环形缓冲区，支持容量上限（默认 50,000 条）与自动轮转；
- **业务生命周期追踪 (`TimelineCorrelator`)**：按 `FrameId` 串联单帧全链路（触发 -> 相机取图 -> 算法推理 -> PLC 剔废 -> 图像存盘）；
- **工业故障特征诊断**：自动检测算法推理超时、PLC 心跳通信中断；
- **统一时间线对齐**：将进程崩溃时刻与崩溃前夕业务异动事件对齐在统一全局时间线上，还原故障前业务过程。

### 6. 线程并发、死锁与卡滞线程差分分析
- **并发分析引擎 (`ThreadConcurrencyAnalyzer`)**：
  - 深度扫描各线程调用栈中的临界区、互斥锁、事件等待、多对象等待与睡眠原语；
  - 构建线程等待有向图（Wait-For Graph），检测相互阻塞的死锁环路 (`DeadlockCycle`)；
  - 识别线程池工作线程耗尽 (`ThreadPool Exhaustion`) 风险；
- **双 Dump 线程差分 (`DetectStuckThreads`)**：对比两个快照中完全相同且处于等待阻塞的停滞僵死线程。

### 7. 故障案例库沉淀与相似故障检索
- **故障知识库 (`FaultCaseItem` / `IFaultCaseRepository`)**：结构化沉淀历史排查经验（异常码、责任模块、责任函数、根因分类、解决方案）；
- **相似故障匹配检索 (`SimilarCaseMatcher`)**：基于异常码、模块名、责任函数及调用栈特征计算多维加权相似度，现场排查时秒级推荐相似历史案例；
- **规则动态管理 (`DiagnosticRuleManager`)**：支持在运行时动态启用/禁用规则、调整规则执行优先级。

### 8. 运行中进程无侵入实时监控与防抖泄漏检测
- **底层指标无损采样**：Private Bytes、Working Set、Handle Count、GDI Objects、Thread Count 与 CPU 使用率；
- **多维统计学升级 (`LeakTrendDetectorOptions`)**：
  - 计算线性回归判定系数 $R^2$ 过滤离散杂音；
  - 计算单调上升比率，识别持续泄漏、阶梯式突发、周期性波动、瞬时尖峰回落与预热后趋稳；
  - 引入采样逆流防御、告警冷却与状态滞回（Hysteresis），杜绝阈值边缘频繁抖动；
- **采样质量追踪与防 PID 复用**：记录单次采样耗时与状态，引入 `ProcessInstanceId`（PID + 启动时间）防止进程重启数据污染。

### 9. 虚拟内存区域差分与堆分析分层
- **规范命名与数据契约**：明确定义为“虚拟内存区域差分” (`NativeRegionGrowthItem`)，清晰呈现 `MEM_COMMIT`、`MEM_PRIVATE` 等虚拟内存属性；
- **语义分层与提示**：明确标注虚拟内存区域变动不等价于单个对象分配；
- **托管堆深度差分 (`ManagedHeapAnalyzer`)**：遍历 GC 堆输出 Top 托管对象排行榜；
- **代码泄漏点精准定位 (`LocateSuspectCode`)**：扫描线程栈，精准归咎责任模块、函数、源文件与行号。

### 10. 统一结构化证据模型与隐私数据脱敏报告
- **强类型证据模型 (`EvidenceItem`)**：支持底层事实、辅助证据、假设、验证步骤分级管理；
- **全要素可复现性报告**：生成包含完整性、规则审计、结构化证据的 HTML/JSON 报告；
- **数据隐私脱敏 (`ReportSanitizer`)**：支持脱敏 Windows 用户名、本地源码路径与内网 IP 地址。

### 11. 截断与未闭合 Dump 启发式物理流特征自愈
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

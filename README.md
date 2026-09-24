# Diagnostic Studio —— Windows 程序诊断与可视化调试平台

> 面向 Windows C#/C++ 工业上位机及长期运行程序的 **Dump 智能分析、源码定位标红标黄、运行期进程监控、内存与资源泄漏分析、双 Dump 差分对比与故障根因辅助诊断平台**。

---

## 一、 系统架构与分层设计

Diagnostic Studio 采用现代分层与依赖倒置架构（DIP），**底层能力交给 C++ Native 库，上层分析编排、规则推导与 UI 呈现交给 C# WPF**：

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

### 1. 拖拽 Dump 自动分析、崩溃点精准定位与“责任代码穿透标红”
* **无需敲击任何复杂 WinDbg 命令**：直接拖入 `.dmp` 文件（支持全窗口即拖即放与全景放行），系统异步自动提取进程环境、崩溃异常记录、所有线程列表与调用栈；
* **原始现场上下文还原与真实调用栈展开**：
  * 优先从 `MINIDUMP_EXCEPTION_STREAM.ThreadContext` 提取发生异常瞬间的原始 CPU 寄存器现场，杜绝被异常分发逻辑篡改；
  - 结合 PE `.pdata` / `UNWIND_INFO` 异常展开表进行标准栈展开 (`StackWalk64`)，确保指令指针与崩溃源头分毫不差；
  - 自动将 Dump 所在目录、`Symbols` 目录及上一级目录加入符号搜索链，本地 PDB 100% 自动识别加载并解析源码文件与行号；
* **业务责任根因反向归咎算法 (`SuspectCodeLocator`)**：
  * 当崩溃在 Windows 底层系统库（如 `ucrtbase.dll!abort`、`ntdll.dll!RtlReportCriticalFailure`、`kernelbase.dll!RaiseException`）内部爆发时，系统自动识别系统层只是拦截触发点，智能向上回溯定位首个调用系统库的**用户业务责任帧 (`IsRootCause`)**；
  * 🔴 **业务责任崩溃点 (Critical)**：将用户责任代码整行红底高亮，行尾显示 `[🔴 崩溃责任根因]`，双击直接在源码视图定位用户业务代码；
  * 🟡 **业务入口上下文 (Suspect/Context)**：识别外层调用入口，整行黄底高亮；
* **工业级双重防御与高鲁棒性隔离**：
  * **Native SEH 隔离保护**：`DiagnosticNative.cpp` 全面对 `SafeStackWalk64`、`MinidumpReadMemoryProc64` 内存读取及符号解析进行结构化异常（SEH）拦截，彻底杜绝底层 DbgHelp 抛出访问违例击穿至 CoreCLR 导致闪退；
  * **UI 异步安全渲染与全局异常兜底**：`SourceCodeViewer` 源码阅读器全面接入 `INotifyPropertyChanged` 响应式数据流，异步安全排版杜绝 WPF 虚拟化死锁，并在 `App.xaml.cs` 挂载全生命周期未捕获异常捕获，确保上位机长期稳定运行。

### 2. 多维全覆盖智能规则引擎 (Rule Engine)
* **空指针解引用规则 (`NullPointerRule`)**：识别非法访问地址落入 0x0 ~ 0x10000 空指针保护页；
* **野指针与 Use-After-Free 规则 (`AccessViolationRule`)**：自动识别 `0xFEEEFEEE` (堆已释放)、`0xCDCDCDCD` 等经典内存魔数特征；
* **栈缓冲区破坏与安全保护违例 (`BufferOverflowRule`)**：识别 `0xC0000409` (STATUS_STACK_BUFFER_OVERRUN / `__fastfail` / `/GS` 栈金丝雀破坏)；
* **未捕获标准 C++ 异常 (`UnhandledCppExceptionRule`)**：识别 `0xE06D7363` (throw std::runtime_error 等未处理 C++ 异常)；
* **算术除零故障 (`DivideByZeroRule`)**：识别 `0xC0000094` 整数除零与浮点除零；
* **堆损坏与重复释放 (`HeapCorruptionRule`)**：识别 `0xC0000374` (STATUS_HEAP_CORRUPTION / Double Free)；
* **断言与断点规则 (`AssertOrBreakpointRule`)**：识别 `0x80000003` (assert() 断言失败 / DebugBreak)；
* **挂起与卡死诊断 (`HangOrDeadlockRule`)**：针对无异常流卡死转储，检测主线程等待与锁争用；
* **全能托底诊断 (`GeneralCrashFallbackRule`)**：兜底捕获所有未知崩溃异常，确保任何有异常的转储绝对不会出现“0 项风险”；
* **证据链条保留**：每条诊断不仅有结论，还保留了完整的 Evidence 证据与 Recommendations 排查建议。

### 3. 可视化符号路径配置与本地 PDB 目录选择 (Symbol Configuration)
* **常驻符号配置栏**：主界面全局常驻“🏷️ 调试符号路径 (PDB)”配置栏，支持 Consolas 等宽字体文本自由编辑与双向数据绑定；
* **本地 PDB 自动识别 + 手动一键选取**：自动检索 Dump 所在目录；同时提供【📁 选择 PDB 目录】操作按钮快速选取自定义输出路径；
* **无缝重新分析**：修改或增补 PDB 路径后，点击【⚡ 重新分析】按钮即可就地重新执行全链路 Dump 逆向分析。

### 4. 运行中进程无侵入实时监控与极速检索 (Live Monitor)
* **进程模糊检索与 PID 快速过滤**：顶部提供 `[🔍 查找进程]` 输入框，支持输入进程名称（如 `CppCrash`）或数字 PID 实时筛选目标进程，配合一键清空与刷新；
* **无损采样**：Private Bytes（私有提交内存）、Working Set（物理工作集）、Handle Count（系统句柄数）、GDI Objects（GDI 图元对象）、Thread Count 与 CPU 使用率；
* **线性回归泄漏识别算法 (`LeakTrendDetector`)**：通过滑动窗口计算指标斜率，主动发出橙色/红色泄漏警报；
* **一键无损热快照 (`LiveSnapshotService`)**：现场点击【📸 瞬间抓取快照并分析】，瞬时生成 MiniDump 并自动加载到分析引擎深挖。

### 5. WindowChrome 沉浸式暗黑界面 (Fluent Dark UI)
* **彻底消除白色大白条**：基于 WPF `WindowChrome` 自定义无缝深色顶栏，消除系统原生非客户区刺眼白条；
* **高质感窗体控制按钮**：右上角集成暗黑风格最小化、最大化/还原、关闭（悬停红底）系统控制按钮；
* **DWM 暗黑沉浸**：调用 Win32 DWM API 启用 `DWMWA_USE_IMMERSIVE_DARK_MODE`，系统边框与阴影全域契合深色主题。
* **无损采样**：基于 Windows 原生底层接口无侵入高频采样，目标进程零感知：
  * **Private Bytes（私有提交内存）**：监控内存泄漏的黄金指标；
  * **Working Set（物理工作集）**；
  * **Handle Count（系统句柄数）**；
  * **GDI Objects（GDI 图元对象）**：针对 Windows 10,000 个硬上限监控界面雪崩；
  * **Thread Count 与 CPU 使用率**；
* **线性回归泄漏识别算法 (`LeakTrendDetector`)**：通过滑动窗口计算指标斜率，当私有内存或 GDI 单调持续攀升且无回落时，主动发出橙色/红色泄漏警报；
* **一键无损热快照 (`LiveSnapshotService`)**：现场发现泄漏或异常走势时，点击【📸 一键抓取快照并分析】，瞬时生成 MiniDump 并自动加载到 Dump 分析引擎中进行深挖。

### 6. 托管堆与原生堆深度差分引擎、堆明细排行与代码泄漏点定位 (Heap Diff & Code Leak Locator)
* **彻底超越传统静态对比，直击具体增长对象与源码行**：
  * **攻克 PE 跨度无变化的技术局限**：DLL 的 PE 跨度 (`SizeOfImage`) 静态写死，动态申请的内存全在私有堆中；
  * **深度多维物理差分体系**：
    * **托管堆差分分析 (`ManagedHeapAnalyzer`)**：基于 `Microsoft.Diagnostics.Runtime` (ClrMD 3.x) 深度遍历 GC 堆，提取双 Dump 的类型直方图，按增量体积 ($\Delta Bytes$) 输出 Top 对象排行榜（对象类型、增量体积、增量实例数、类别推导与根引用线索）；
    * **原生虚拟内存段差分分析 (`NativeMemoryAnalyzer`)**：解析 MiniDump 二进制流（`MemoryInfoListStream` 与 `Memory64ListStream`），提取所有 `MEM_COMMIT | MEM_PRIVATE` 私有堆段的十六进制基地址、段大小与变动状态（新增分配段/扩容段）；
    * **启发式线程栈扫描与代码泄漏点定位 (`LocateSuspectCode`)**：全面扫描所有活跃线程调用栈，结合用户源码（`IsUserCode`）、内存分配与业务工作循环特征，精准归咎定位到责任模块、责任函数、源文件与行号（如 `CppCrashTarget.exe!SlowMemoryLeakScenario::Execute (MemoryLeakScenarios.cpp:126)`），置信度达 90%~98%；
    * **一键直达源码**：快照对比顶部醒目展示【🎯 疑似内存泄漏责任点】，配备【⚡ 在源码编辑器中定位】快捷按钮，点击直接切换至【源码定位】并在该行代码高亮呈现！
* **常见疑问说明**：在【📈 进程监控】页面中发现私有内存持续上涨（如红色预警 Leak Trend Alerts: +58 MB/min）后点击抓取快照，为什么 Dump 分析中未报致命崩溃错误？
  * **原理解析**：监控基于**时间序列（Time Series）**的动态斜率算法，能敏锐发现内存随时间的持续累积；而单份 Dump 是**瞬态静态切片**，在程序未触发崩溃异常时，单凭一个快照无法判断哪些堆对象属于业务缓存、哪些属于无用泄漏。
* **标准排查闭环**：
  1. **捕获基线 Dump A**：监控出现泄漏趋势告警时，点击【📸 抓取快照并分析】，系统自动在 Snapshots 目录保存基线快照；
  2. **等待内存继续增长**：让程序继续运行数分钟，在监控折线图上观察到内存继续净增数十兆；
  3. **捕获对比 Dump B**：再次点击【📸 抓取快照并分析】生成对比快照；
  4. **进入【⚖️ 快照对比 (Diff)】差分定位**：加载 Dump A 与 Dump B，系统自动计算两者之间的净增体积、线程变化、**堆与内存段增量明细表**，并**精准锁定代码泄漏点具体源码行号**！

### 7. 独立存储结构与中间文件一键清理 (Workspace Storage & Cleanup)
* **独立工作区目录 (DiagnosticsData/)**：彻底脱离与 exe 同级目录混放的模式，设立规范化的专用子目录树：
  * **快照专区 (DiagnosticsData/Snapshots/)**：热快照按 {进程名}_{PID}_{时间戳}.dmp（例如 CppCrashTarget_20932_20260924_140500.dmp）结构化命名，各进程各批次独立存储，永不相互覆盖；
  * **日志专区 (DiagnosticsData/Logs/)**：系统运行日志按天分卷输出 (DiagnosticStudio_yyyyMMdd.log)；
  * **报告专区 (DiagnosticsData/Reports/)**：HTML/JSON 导出诊断报告集中归档；
* **界面级中间文件占用监控与一键清理**：
  * 在【📋 系统日志 (Logs)】页面顶部实时呈现快照、日志、报告文件的数量与体积占用；
  * 提供【🧹 清理中间文件】按钮，一键安全回收历史快照与旧文件（自动保护当天正在写入的活动日志）；
  * 提供【📂 打开数据目录】与【📂 快照目录】快速直达文件资源管理器。

### 8. 专业诊断报告导出
* 支持一键导出暗黑科技风格的独立自包含 **HTML 报告** 与 **JSON 报告**，包含风险大卡片、异常摘要、证据链与崩溃调用栈表格。

---

## 三、 工程结构说明

```text
WinDbgTool/
├── samples/                        # 完全独立的测试靶场程序（用于调试工具功能验证与压测）
│   ├── CSharpCrashTarget/          # C# .NET 8 独立测试靶场（崩溃Dump定位、慢速受控内存泄漏、句柄泄漏、死锁）
│   ├── CppCrashTarget/             # C++17 Native 独立测试靶场（空指针解引用红黄高亮、UAF、栈溢出、GDI泄漏）
│   ├── build_all.ps1               # 靶场一键构建脚本
│   └── README.md                   # 靶场详细测试场景与安全防护说明文档
├── src/
│   ├── DiagnosticStudio.Native/    # C++17 动态链接库，封装 DbgHelp / MiniDump / PDB / 启发式栈扫描
│   ├── DiagnosticStudio.Core/      # C# 业务模型、规则引擎、HTML/JSON报告生成器、系统日志
│   ├── DiagnosticStudio.Dump/      # C# Dump 分析引擎、Native Bridge、可疑代码定位器
│   ├── DiagnosticStudio.Memory/    # C# 内存分析与双 Dump 差分比较器
│   ├── DiagnosticStudio.Monitor/   # C# 实时进程监控器、线性回归泄漏算法、热快照触发器
│   └── DiagnosticStudio.UI/        # C# WPF 现代化桌面客户端（包含 Assets 抽象调试虫子专属应用图标、MVVM、自绘折线图、源码阅读器）
├── tests/
│   └── DiagnosticStudio.Tests/     # xUnit 自动化单元测试套件（覆盖规则引擎、趋势算法、差分、报告及真实 Dump）
├── DiagnosticStudio_需求分析与技术选型.md # 原始架构与技术选型规格书
├── DiagnosticStudio.sln            # 统一解决方案
├── README.md                       # 项目说明文档
└── CHANGENOTE.md                   # 版本变更记录
```

> **说明**：为了保持代码仓库精简并规避 GitHub 大文件（>100MB Dump 转储）限制，`tests/`（单测工程）与 `samples/`（测试靶场与生成转储）在本地开发环境中保留供调试验证，不纳入远程 Git 版本控制。

---

## 四、 编译与运行指南

### 环境依赖
* Windows 10 / 11 (x64)
* .NET 8.0 SDK 或更高版本
* Visual Studio 2022 (包含 C++ 桌面开发工作负载与 MSVC 工具链)
* CMake 3.20 或更高版本

### 1. 编译 C++ Native 库
```powershell
cmake -B "src/DiagnosticStudio.Native/build" -S "src/DiagnosticStudio.Native" -A x64
cmake --build "src/DiagnosticStudio.Native/build" --config Release
```

### 2. 编译整个 C# 解决方案
```powershell
dotnet build DiagnosticStudio.sln
```

### 3. 运行自动化单元测试
```powershell
dotnet test tests/DiagnosticStudio.Tests/DiagnosticStudio.Tests.csproj --no-build
```

### 4. 启动客户端
直接运行生成的桌面端程序：
```powershell
.\src\DiagnosticStudio.UI\bin\Debug\net8.0-windows\DiagnosticStudio.UI.exe
```

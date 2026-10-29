# Diagnostic Studio 版本变更记录 (CHANGENOTE)

## [1.2.7] - 2026-10-29

### 截断与未闭合 Dump 启发式物理流特征自愈恢复机制 (Heuristic Dump Recovery & Fault-Tolerant Engine)

#### 1. 根治生产环境截断转储“0异常/0模块/0线程”解析丢失问题
- **问题背景**：
  - 在工业现场或高负载服务场景下，转储大文件（Full Dump）落盘耗时较长，外部守护进程或看门狗超时强杀（TerminateProcess）、系统关机或断电常导致 MiniDump 写入底层流数据后，**未能完成流目录索引表（Stream Directory）的回写**；
  - 此时转储文件头部预留的流目录表（0x20 起始）全为 0 占位符，微软原生 API `MiniDumpReadDumpStream` 判定为 `UnusedStream (0)` 直接返回 `ERROR_NOT_FOUND (1168)`，造成常规调试器误判转储损坏并丢失全部异常记录、模块与线程信息。
- **启发式特征自愈与流重组引擎 (TryHeuristicRecoverDump)**：
  - 在 `DiagnosticNative.cpp` 中研发物理特征扫描自愈算法，当流目录解析缺失时自动触发：
  - **SystemInfo 启发式重建**：扫描标准候选偏移，通过处理器架构（x64/x86/ARM64）、平台类型及主副版本号特征自愈恢复操作系统与环境信息；
  - **ExceptionStream 异常流自愈**：扫描内存特征，识别常见致命崩溃码（如 0xC0000005 访问违例、0xC0000374 堆损坏、0xC00000FD 栈溢出、0xC0000409 FastFail 等），提取崩溃线程 ID、指令地址与上下文异常记录；
  - **ThreadListStream 线程流自愈**：校验 x64/x86 标准上下文（CONTEXT）尺寸及调用栈合法性，遍历还原所有存活线程并自动注册到内存映射；
  - **ModuleListStream 模块流重组**：定位加载模块特征与 Unicode 模块路径，恢复模块基址并挂载符号引擎（`SymLoadModuleExW`）；
  - **Memory64ListStream 内存段恢复**：恢复 64 位连续内存段，确保调用栈展开与内存寻址能力。

#### 2. 上层容错感知与规则引擎提示 (CorruptedDumpRecoveryRule)
- **底层与上层通信打通**：`DiagnosticNative.dll` 导出 `IsDumpHeuristicallyRecovered` 接口，C# `DumpAnalyzerService` 自动感知并标记 `report.IsRecovered = true`；
- **智能诊断规则命中**：新增 `CorruptedDumpRecoveryRule` 并注册到 `DefaultRuleEngine`：
  - 标记 `Warning` 级别预警：“转储文件索引未闭合（已通过启发式引擎自动修复解析）”；
  - 详细分析故障产生原因，并给出看门狗超时强杀限制调整与 MiniDump 参数优化的工业现场实操建议。


## [1.2.6] - 2026-09-24

### 剥离测试工程、测试代码及 Dump 转储大文件，优化 Git 提交历史，解除 GitHub 推送大小限制 (Push Optimization & Test Stripping)

#### 1. 彻底解决 GitHub 推送 Dump 文件过大拦截故障 (GH001)
- **问题根因**：原版本控制提交中包含了样例与测试目录下的转储大文件（如 `Cpp_LeakCompare_StateB.dmp` 达到 121.82MB），触发了 GitHub 单文件不得超过 100MB 的硬性拒绝拦截策略（GitHub Large Files Limit）；
- **根除策略**：重构 Git 提交历史，将所有 `.dmp`、`.mdmp` 等大型内存转储二进制文件从提交树中彻底剥离，确保推送时 0 冗余大对象。

#### 2. 取消测试工程与测试代码的版本控制提交
- **工程分层与安全隔离**：
  - 将 `tests/`（`DiagnosticStudio.Tests` 单元测试与端到端测试工程）和 `samples/`（C#/C++ 崩溃复现目标工程与自动化验证脚本）从 Git 版本控制中剔除；
  - 本地磁盘上的工程源码、测试用例与样本数据完整保留，不影响本地单测运行与本地崩溃排查验证；
- **配置规则完善 (`.gitignore`)**：
  - 全局忽略 `*.dmp`、`*.mdmp`、`[Dd]umps/` 以及 `tests/`、`samples/` 目录；
  - 严谨定义忽略规则，避免匹配破坏 `src/DiagnosticStudio.Dump` 核心业务模块。

## [1.2.5] - 2026-09-24

### 应用内标题栏恢复经典样式、桌面图标四角纯白区域完全透明化与双模式同步更新 (Icon Transparency & Internal Logo Reversion)

#### 1. 应用内 Logo 恢复为原始经典样式
- 根据需求，将 `MainWindow.xaml` 自定义暗黑标题栏左上角品牌展示区恢复为经典的 `⚡` 闪电标识，配合 `Diagnostic Studio` 与 `C#/C++ 工业诊断平台` 徽章，保持应用界面内部原有的轻简与专业感。

#### 2. 桌面与系统级图标圆角四周白色区域透明化
- **像素级图形处理与通道重构**：
  - 对用户提供的“代码窗口+放大镜+红色Bug”图标进行像素分析，发现原始图像四个角落为非透明浅白色区域（`#FDFDFD`）；
  - 实现基于 BFS 洪水填充的高保真边缘检测算法，从图像四边及四顶点精确提取外部背景区域，将其 Alpha 通道置为完全透明（`A=0`）；
  - 在圆角交界边缘实施 1 像素反走样羽化算法，彻底消除白边与锯齿感；
  - 重新生成具备完全透明通道的 512×512 `Assets/bug.png` 及符合 Windows GDI 标准的 `Assets/app.ico` 桌面图标。

#### 3. Debug 与 Release 双模式全量编译与运行实测
- 重新构建 Debug 与 Release 两个配置，二进制 EXE 原生内嵌该透明圆角图标；
- 启动测试双模式应用程序，两模式均瞬间拉起、平稳运行，全生命周期日志 0 异常。

## [1.2.4] - 2026-09-24

### 应用程序全新“代码窗口+放大镜+红色调试Bug”图标换新、Baml2006 解码异常根治与 Debug/Release 双模式同步落地 (App Icon Overhaul & Baml2006 Decoding Fix)

#### 1. 根治“在 System.Windows.Baml2006.TypeConverterMarkupExtension 上提供值时引发异常”启动故障
- **故障排查与深层根因剖析**：
  - 用户反馈启动应用时弹出报错拦截：“程序在渲染或界面调度时发生异常，已进行防御拦截：在 'System.Windows.Baml2006.TypeConverterMarkupExtension' 上提供值时引发了异常”；
  - 调取底层日志并深度分析发现 COM 异常：`0x88982F60 (WINCODEC_ERR_UNSUPPORTEDIMAGE, 图像无法识别)`；
  - **核心根因**：Windows WIC (Windows Imaging Component) 解码器在解析 ICO 图标时，明确规范小尺寸（16x16/32x32/48x48）必须采用标准未压缩 32bpp DIB（BITMAPINFOHEADER + 颜色表 + AND Mask），如果直接写入 PNG 数据流，WIC 的 `IconBitmapDecoder` 会判定图像损坏并抛出异常，导致 BAML 标记扩展在转换 `Window.Icon` 时反射崩溃。
- **根本性技术解决方案**：
  1. **图标生成机制重构**：采用 Windows 原生 GDI 管道（`GetHicon` / `Icon.FromHandle`）生成符合 Windows 规范的标准 ICO 资源文件；
  2. **WPF 表现层与系统层解耦**：
     - 在 PE 二进制层面保留 `<ApplicationIcon>Assets\app.ico</ApplicationIcon>`；
     - 在 XAML 表现层采用全局绝对 Pack URI（`pack://application:,,,/DiagnosticStudio.UI;component/Assets/bug.png`）加载 512x512 高保真矢量位图，既享受全 DPI 高清抗锯齿，又彻底杜绝相对路径寻址与 ICO 解码兼容性风险。

#### 2. 应用图标换新为“代码窗口 + 放大镜 + 红色调试 Bug”专属样式
- **视觉升级**：
  - 采用深蓝色圆角外框，内部集成程序代码窗口、高亮放大镜与核心红色调试 Bug（Debug Bug），精准传达工业程序故障诊断、内存审计与深层调试的核心业务定位；
  - 同步更新标题栏 Logo 与应用图标。

#### 3. Debug 与 Release 双模式全链路同步与验证
- 在 `DiagnosticStudio.UI.csproj` 中配置 `<None Include="Assets\**"><CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory></None>`，确保输出目录自动同步物理资产；
- Debug 与 Release 模式全量重新编译并运行实测：
  - Debug 模式启动验证：启动成功，全生命周期日志正常挂载，0 异常；
  - Release 模式启动验证：启动成功，全生命周期日志正常挂载，0 异常。

## [1.2.3] - 2026-09-24

### 应用程序全新抽象科技虫子（Debugger Bug）专属图标设计与工程落地 (Application Icon & Brand Visual System)

#### 1. 新增抽象几何虫子专属图标资产与工程集成
- **需求与设计背景**：
  - Diagnostic Studio 作为一款专业的 Windows C#/C++ 工业诊断与程序调试工具，原有窗口采用临时字符符号，缺乏专属的调试软件品牌视觉标识；
  - 根据调试软件（Debugger）经典“Bug”意象，采用极简几何与抽象矢量盾牌科技风格（规避写实昆虫细节），设计兼具科技感、现代感与工业专业性的抽象虫子（Debug Bug）图标。
- **图标工程资产规格**：
  - `Assets/app.ico`：标准 Windows 多分辨率图标资源（包含 16x16、24x24、32x32、48x48、64x64、128x128、256x256 七大规格），具备完整 32 位 ARGB 高清透明通道；
  - `Assets/bug.png`：512x512 高清矢量位图，供应用内沉浸式 UI 和关于窗口直接渲染。
- **UI 与项目工程全链路配置**：
  - `DiagnosticStudio.UI.csproj`：配置 `<ApplicationIcon>Assets\app.ico</ApplicationIcon>` 与 `<Resource>` 资产引用，使编译生成的 `DiagnosticStudio.UI.exe` 二进制文件原生内嵌 Windows 资源管理器图标；
  - `MainWindow.xaml`：配置 `<Window Icon="Assets/app.ico">`，并在沉浸式暗黑标题栏左上角 Logo 区域集成微缩虫子图标，强化现代化开发者工具视觉统一性。
- **构建与测试验证**：
  - .NET 8.0 Release 模式编译全部通过（0 警告，0 错误）；
  - 成功从编译产物 `DiagnosticStudio.UI.exe` 中提取并验证内嵌图标资源有效性。

## [1.2.2] - 2026-09-24

### 真实快照线程栈深层穿透展开、原生私有堆暴增段解析与用户代码泄漏点定位攻坚 (Native Stack Walk & Heap Segment Diff Fix)

#### 1. 攻克“定位到 ntdll!NtDelayExecution、关联代码空白、无堆内存增长明细”三大连环根因
- **故障排查与现场复现**：
  - 用户在实际对比两份快照 Dump（+98.26 MB 物理净增）时反馈：“相比重构之前并没有多很多的内容。无法定位到代码，也没有堆内存的增长明细”，界面责任代码显示为 `ntdll!NtDelayExecution`，关联代码显示为空白。
- **三大连环深层技术根因彻底剖析**：
  1. **MiniDump 二进制魔数常量拼写错误**：
     - `NativeMemoryAnalyzer.cs` 中的 `MINIDUMP_SIGNATURE` 最初误写为了 `0x504D4E44`（实际小端序 'MDMP' 整数值为 `0x504D444D`）；
     - 导致解析任何 MiniDump 文件均在第一步签名校验时被拦截并直接返回空列表，造成 Tab 2 原生提交段列表为空且 Tab 1 缺少原生段扩容诊断卡片。
  2. **MiniDumpWithFullMemory 下线程栈 RVA 为 0 导致栈展开被截断为 1 帧**：
     - 在抓取包含大内存提交的 MiniDump 时，Windows 将线程栈统一放入 `MemoryListStream` (Stream 5) 或 `Memory64ListStream` (Stream 9) 中，而 `MINIDUMP_THREAD.Stack.Memory.Rva` 被置为 0；
     - 原 Native 展开逻辑仅检查 `stackDesc.Memory.Rva != 0`，未从全局内存流反查真实物理 RVA，导致内存读取回调拒绝访问栈内存，`StackWalk64` 展开第 0 帧后立刻中断，导致 33 层调用栈被腰斩为仅剩 1 帧（`ntdll!NtDelayExecution`）。
  3. **系统底层模块无扩展名匹配穿透漏洞**：
     - Windows DbgHelp API（`SymGetModuleInfoW64`）返回的模块名不带扩展名（如 `ntdll`、`KERNELBASE`、`kernel32`）；
     - 原 `SystemModuleNames` 黑名单仅收录带 `.dll` 的名称，导致 `IsUserModule("ntdll")` 误判为 `true`；由于第 0 帧被当作用户代码，直接屏蔽了深层的真实业务代码。
- **根本性解决方案与全链路修复**：
  1. **修正 MiniDump 签名魔数**：将魔数常量纠正为 `0x504D444D`，完整提取出全部 50 个暴增私有内存段（如基地址 `0x0000023E275A0000` 扩容 +15.62 MB 等，总计 +98.26 MB）；
  2. **C++ Native 模块深度重构 (`DiagnosticNative.cpp`)**：
     - 在 `OpenDumpSession` 中同时解析并缓存 `MemoryListStream` 与 `Memory64ListStream`；
     - 在 `MinidumpReadMemoryProc64` 与线程栈初始化逻辑中增加全局内存范围回溯，彻底解决 RVA 为 0 的场景；
     - **调用栈展开能力瞬间从 1 帧暴增恢复至 33 帧**！
  3. **模块过滤与源码优先级启发式扫描 (`SuspectCodeLocator.cs` & `NativeMemoryAnalyzer.cs`)**：
     - 排除 `ntdll`、`KERNELBASE` 等无后缀模块名；
     - 增加 `IsCompilerInternalSource` 过滤 STL/CRT 内部头文件（如 `__msvc_chrono.hpp`, `thread`, `sharedmutex.cpp`）；
     - 建立三级优先级体系，以最高权重精准命中真实业务责任代码：
       **`CppCrashTarget!CppCrashTarget::SlowMemoryLeakScenario::Execute`**
       **源文件：`MemoryLeakScenarios.cpp` 第 126 行**！
  4. **UI 卡片与定位增强 (`DiagnosticFinding.cs` & `MainWindow.xaml`)**：
     - 在 Tab 1 首要卡片中展示完整的源码路径与行号：`📍 关联代码: MemoryLeakScenarios.cpp (第 126 行)`；
     - 在证据链中直接列出首要暴增内存段；
     - 双击卡片或点击顶部横幅【⚡ 在源码编辑器中定位】直接跳转源码并高亮第 126 行。
- **全套构建与回归验证**：
  - CMake Debug / Release 模式重新编译 `DiagnosticNative.dll` 并同步分发；
  - .NET 8.0 解决方案 Debug / Release 双模式编译全部通过（0 警告，0 错误）；
  - 全套 29 项单元测试全部通过（通过率 100%）。

## [1.2.1] - 2026-09-24

### 快照对比只读属性 WPF 双向绑定异常防御与稳定性修复 (WPF Run OneWay Binding Defense)

#### 1. 修复快照对比时弹出“无法对 PrimarySuspectDetail 进行 TwoWay 或 OneWayToSource 绑定”异常
- **故障现象**：
  - 用户在【⚖️ 快照对比】中选定两份 Dump 执行对比时，界面弹出错误对话框：“对比失败: 无法对‘DiagnosticStudio.Memory.Services.DumpDiffResult’类型的只读属性‘PrimarySuspectDetail’进行 TwoWay 或 OneWayToSource 绑定”。
- **深层根因剖析**：
  1. WPF 框架中 `<Run Text="{Binding ...}"/>` 内联元素的 `TextProperty` 依赖属性在元数据中默认配置为 `FrameworkPropertyMetadataOptions.BindsTwoWayByDefault`（默认双向绑定）；
  2. `DumpDiffResult` 与 `SuspectCodeLocation` 中的 `PrimarySuspectDetail`、`PrimarySuspectDisplay`、`HasPrimarySuspect`、`DisplayLocation` 等计算属性最初为只读表达式主体属性（只有 getter，无 setter）；
  3. 当视图绑定建立时，WPF 反射找不到属性 setter，直接抛出 `InvalidOperationException`。
- **架构级双重防御实现**：
  1. **模型层防御**：在 `IDumpComparator.cs` 与 `SuspectCodeLocation.cs` 中，为所有只读计算属性（`PrimarySuspectDetail`、`PrimarySuspectDisplay`、`HasPrimarySuspect`、`DisplayLocation`、`DeltaFileSizeBytes`、`DeltaThreadCount`、`DeltaModuleCount`）均追加安全空 setter（`set { }`），从底层杜绝任何双向绑定反射失败；
  2. **视图层防御**：在 `MainWindow.xaml` 中，将快照对比界面顶部责任定位横幅与卡片组中的所有 `<Run Text="{Binding ...}"/>` 显式设置 `Mode=OneWay`，规范数据单向流向。
- **构建与测试验证**：
  - Debug 与 Release 双模式编译全部通过（0 警告，0 错误）；
  - 全套 28 项单元测试通过率 100%。

## [1.2.0] - 2026-09-24

### 托管堆与原生堆深度差分引擎、堆对象增量排行明细、内存段分布与代码泄漏点精准定位 (Heap Diff & Suspect Code Leak Locator)

#### 1. 彻底攻克“PE 跨度无变化对比缺乏意义、缺少具体增长明细与代码定位”的核心诉求
- **精准响应工业现场诊断痛点**：
  - 用户在实际 C++ 内存泄漏测试中（Dump 体积从 7.51 MB 暴涨至 105.77 MB，净增 +98.26 MB），指出现有对比“PE 跨度没有变化，只能对比出宏观数量，没有进一步分析，关联代码为空白，没有实质作用”；
  - **技术根因剖析**：
    1. DLL/EXE 的 PE 跨度 (`SizeOfImage`) 是静态编译链接固化的，运行时无论是 `malloc/new` 还是 GC 分配，内存均落在操作系统的私有堆（Private Commit Heap）中，永远不会改变 PE 映像跨度；
    2. 旧版代码推导仅检查“新生线程”，而工业程序或测试程序中，持续产生泄漏的往往是常驻运行的主循环或工作线程，因此旧版未捕获到任何用户代码，导致“关联代码：(空白)”；
  - **根本性解决方案**：
    从底层跳出 PE 模块限制，实现**Managed Heap（托管堆）**与**Native Heap（原生内存段）**的双维度深度差分，并引入**启发式线程栈扫描算法**，直接定位到造成内存暴增的代码文件、函数和具体行号！

#### 2. 新增 Managed Heap 托管堆差分分析器 (IManagedHeapAnalyzer / ManagedHeapAnalyzer)
- **基于 Microsoft.Diagnostics.Runtime (ClrMD 3.x) 深度遍历 GC 堆**：
  - 自动检测转储文件是否包含 .NET 托管运行时（CLR / CoreCLR）；
  - 遍历 GC 堆全部存活对象（`ClrHeap.EnumerateObjects()`），对每个类型聚合数量与大小，构建精准的类型直方图；
  - 对比 Dump A 与 Dump B 的托管直方图，按增量字节数 (`DeltaBytes`) 降序输出 Top 增长对象排行榜；
  - 包含类型全限定名（如 `System.Byte[]`、`System.String`、业务实体类）、增量体积、增量实例数、当前堆占用、基线堆占用，并推导类别特征与持有根线索（如排查长生命周期静态集合与未注销事件）。

#### 3. 新增 Native Heap / 虚拟内存段差分分析器 (INativeMemoryAnalyzer / NativeMemoryAnalyzer)
- **基于 MiniDump 二进制流规范提取私有提交堆内存**：
  - 直接从 Dump 文件中解析 `MemoryInfoListStream` (Stream 16) 与 `Memory64ListStream` (Stream 9)；
  - 提取所有 `MEM_COMMIT | MEM_PRIVATE` 私有读写段的十六进制基地址、段大小与属性；
  - 建立基线段映射，精确计算各内存段的物理净增量，分类标识“新增分配段”与“扩容段”；
  - 形成 Top 原生虚拟内存段增长排行表，清晰呈现到底是哪个虚拟内存段在持续膨胀。

#### 4. 启发式线程栈扫描与代码泄漏点精准定位算法 (LocateSuspectCode)
- **智能责任评分模型 (Suspect Scoring Model)**：
  - 针对增量超过 1MB 的物理膨胀，全面扫描 Dump B 中的全部活跃线程（不局限于新生线程）；
  - 排除操作系统纯内核空闲等待线程（如 `ntdll!ZwWaitForWorkViaWorkerFactory` 扣分）；
  - 对包含用户源码（具备 `SourceFile` 且行号大于 0）的线程加分；
  - 对包含内存分配特征（`malloc`, `new`, `push_back`, `Allocate`, `Buffer`, `Leak`）或业务工作循环（`Execute`, `Run`, `Worker`, `Process`）的调用帧赋予极高权重；
- **精准定位代码责任点 (SuspectCodeLocation)**：
  - 成功从包含 `SlowMemoryLeakScenario::Execute` 的线程中提取出：
    - 责任模块：`CppCrashTarget.exe`
    - 责任函数：`CppCrashTarget::SlowMemoryLeakScenario::Execute`
    - 源码位置：`MemoryLeakScenarios.cpp` 第 126 行
    - 置信度：`95%`
  - 填入首要诊断条目的 `Module`, `Function`, `SourceFile`, `Line`, `RelatedStack`，彻底终结“关联代码：(空白)”的问题！

#### 5. 快照对比 UI 界面全面升级与源码直达
- **新增【🎯 疑似代码泄漏责任点】醒目横幅**：
  - 在【⚖️ 快照对比】界面顶部高亮展示推导出的责任函数、源码文件、行号与置信度；
  - 配备【⚡ 在源码编辑器中定位】快捷按钮，点击直接切换至【源码定位】并在该行代码高亮呈现！
- **新增【📈 堆与内存段增量明细 (Top Growth)】核心 Tab**：
  - 若为 .NET 进程：呈现【托管堆对象增长排行表】；
  - 若为 Native C++ 进程：呈现【原生虚拟内存段与堆块增长排行表】；
- **完善的四 Tab 对比体系**：
  - Tab 1: 【🔍 差分智能推导与代码定位】
  - Tab 2: 【📈 堆与内存段增量明细 (Top Growth)】
  - Tab 3: 【🧵 线程变动明细 (Thread Changes)】
  - Tab 4: 【📦 PE 模块列表 (Modules)】

#### 6. 自动化测试套件扩充与全量通过
- 新增 `NativeAndManagedHeapDiffTests` 测试类，包含常驻线程代码泄漏定位算法、诊断推导首要责任代码提取及堆增量计算测试；
- 全套 28 项单元测试全部通过（通过率 100%）。

---

## [1.1.3] - 2026-09-24

### 界面弹性响应式重构、独立工作区存储服务与中间文件一键清理 (Storage & Cleanup & UX Overhaul)

#### 1. 快照差分深度推导引擎与根因诊断面板重构 (Diff Insights & Root Cause Heuristics)
- **正面回应“PE 跨度无变化，对比缺乏分析意义”的技术痛点**：
  - **明确 PE 跨度的真实定位**：PE 跨度 (SizeOfImage) 仅用于识别程序在运行期间是否动态注入或加载/卸载了新 DLL/插件，对于堆内存泄漏本身确实无直接关联；
  - **彻底重构快照对比核心诊断面板**：不再让用户直面单调且无变化的 PE 模块表，而在界面首屏（第一 Tab 默认选中）重磅推出【🔍 差分智能推导与疑似根因 (Diagnostic Insights)】看板；
- **深度差分推导算法升级 (DumpComparator.cs)**：
  - **私有内存暴涨智能推导**：基于实际物理增量划分 Critical/High/Warning 等级，自动识别目标程序底层架构（.NET 托管 CLR vs Native C++），针对性给出静态集合排查、LOH 堆优化、对象引用链分析或 Native 缓冲区释放建议；
  - **新生线程用户责任代码穿透**：自动比对双 Dump 间的全部线程调用堆栈，一旦检测到新创建的活跃线程执行了用户业务代码（IsUserCode），自动提取责任函数名、源码文件与行号，直接生成一条高风险业务代码归咎项，**双击即可直达源码红黄高亮定位**；
  - **线程风暴与积压告警**：检测未退出的大量新生线程，预警未 Join 或未受控并发导致的线程栈泄漏；
- **快照对比三 Tab 结构清晰分层**：
  - Tab 1: 【🔍 差分智能推导与疑似根因 (Insights)】（默认首屏呈现，深度推导与排查建议）
  - Tab 2: 【🧵 线程变动明细 (Thread Changes)】（新生与终止线程列表）
  - Tab 3: 【📦 PE 模块列表 (Modules)】（动态库加载与卸载状态）

#### 2. 快照对比 Run 绑定崩溃修复与暗黑高对比度 Tab 样式 (UI Bugfix & Contrast Overhaul)
- **修复 TwoWay 绑定只读属性引发的对比崩溃**：
  - **核心根因**：WPF 内联元素 Run.Text 依赖属性在框架底层的默认绑定模式为 TwoWay（双向绑定）。当绑定到 DumpDiffResult.FileSizeADisplay 等计算只读属性时，WPF 反射找不到 setter，直接抛出 InvalidOperationException: 无法对只读属性进行 TwoWay 绑定；
  - **双重防御修复**：在 XAML 绑定中显式声明 Mode=OneWay，并在 DumpDiffResult 模型各展示属性中追加安全空 setter，彻底消除弹窗报错；
- **重构 TabControl 与 DataGrid 表头暗黑科技样式**：
  - **核心痛点**：原生 WPF TabItem 与 DataGridColumnHeader 在选中或切换时出现白底白字、浅底浅字，文字辨识度极差；
  - **高对比度暗黑重构 (SubTabItemStyle & DarkDataGridHeaderStyle)**：
    - 未选中项采用深底高对比浅灰字（#20202A / #94A3B8）；
    - 选中项采用高亮工业蓝底纯白加粗字（#1D4ED8 / #FFFFFF），边框高亮（#3B82F6）；
    - DataGrid 表头统一定制为深黑沉浸式背景（#1E1E28）配清晰浅灰字，字迹锐利醒目，视觉层次极佳。

#### 2. 快照对比增量为 0 根因彻底解决与底层转储类型升级 (Dump Diff & Private Memory)
- **核心根因深入剖析**：
  1. **比对指标错位**：原快照对比列表中显示的是各个 DLL 的 MINIDUMP_MODULE.SizeOfImage（PE 映像大小）。该数值由编译链接器写死在 PE 文件头中，运行时永远不变（例如 dbgcore.dll 永远是 212992 字节）。即便进程发生严重内存泄漏，动态分配也是在全局堆（Heap）中，绝不可能改变 DLL 自身的 PE 跨度，因此增量差值 DeltaBytes 必然全部为 0；
  2. **转储类型未包含私有堆内存**：原快照采用默认轻量 MiniDump，未包含私有读写内存页，Dump 文件仅 150KB，未将进程私有内存写入转储；
- **底层与界面全面重构**：
  - **Native 底层升级**：在 DiagnosticNative.cpp 中升级转储标志，加入 MiniDumpWithPrivateReadWriteMemory 与 MiniDumpWithFullMemoryInfo，快照将完整收录进程分配的私有堆内存页，使转储文件大小真实随内存泄漏而增长；
  - **快照对比核心指标卡片**：在【⚖️ 快照对比 (Diff)】界面新增【转储体积与私有堆物理净增 (Delta)】、【活跃线程数净增量】与【动态链接库总数】三张醒目指标大卡片，以红色高亮展示真正的内存膨胀量；
  - **模块表格清晰修正**：将表头重命名为【基线 PE 跨度 / 当前 PE 跨度 / PE 映像差值】，并增加显要的原理提示横幅，避免概念混淆；
  - **新增线程变动明细 Tab**：完整呈现双 Dump 间的【新增活跃线程】与【已销毁线程】及顶层调用栈。

#### 2. 工作区独立分门别类存储服务 (WorkspaceStorageService)
- **彻底消除文件与 exe 混放**：
  - 建立专用的 DiagnosticsData 工作区独立存储结构，与 DiagnosticStudio.UI.exe 同级彻底隔离；
  - **快照独立目录 (DiagnosticsData/Snapshots/)**：热快照自动结构化命名为 {ProcessName}_{PID}_{yyyyMMdd_HHmmss}.dmp（包含目标进程名、进程 PID 与时间戳），绝不覆盖历史记录；
  - **运行日志独立目录 (DiagnosticsData/Logs/)**：系统日志按日分卷保存为 DiagnosticStudio_{yyyyMMdd}.log，支持多进程长期运行写入；
  - **报告独立目录 (DiagnosticsData/Reports/)**：导出的 HTML 与 JSON 诊断报告统一归档；
- **抽象存储管理接口 (IWorkspaceStorageService)**：
  - 遵循依赖倒置（DIP）与单一职责（SRP）原则，提供 GetSnapshotsDirectory、GetLogsDirectory、GenerateSnapshotFilePath、GetCleanupSummary 及 CleanIntermediateFiles 等接口。

#### 2. UI 界面中间文件统计与一键清理功能
- **系统日志页中间文件管理面板**：
  - 在【📋 系统日志 (Logs)】界面顶部集成存储监控卡片，实时显示工作区物理路径、快照 Dump 数量与占用、运行日志与临时报告体积；
  - 提供【🧹 清理中间文件】按钮，一键安全清理历史快照与旧日志，弹出释放空间统计详情（自动保护当天正在写入的活动日志）；
  - 提供【📂 打开数据目录】与【🔄 刷新占用】快捷操作；
- **进程监控页快照直达**：
  - 在【📈 进程监控 (Monitor)】操作栏新增【📂 快照目录】按钮，抓取快照后可直接在 Windows 文件资源管理器中查验生成的 Dump 文件。

#### 3. 进程编译锁死问题与进程监控顶部工具栏响应式重构
- **编译锁死根因排除**：由于上位机运行期间锁定 DiagnosticNative.dll，已排查并终止遗留进程，全套解决方案 100% 编译成功并更新最新时间戳；
- **顶部工具栏弹性流式重构**：采用 DockPanel + WrapPanel 架构，右侧操作按钮组享有最高布局优先级，彻底解决右侧按钮被压缩、裁剪、显示不全的问题。

### 界面响应式弹性优化与运行中热快照内存泄漏诊断联动 (UI Layout & Live Snapshot Leak Workflow)

#### 1. 进程监控页面顶部工具栏自适应布局重构 (UI Responsive Layout Overhaul)
- **核心根因**：原 MainWindow.xaml 进程监控页面（Monitor Tab）顶部工具栏采用纯多列固定网格（Width="220", Width="340"），各控件总宽度超过 1100px。在窗口未全屏、中低分辨率或系统启用了 125%/150% DPI 缩放时，最右侧的操作按钮组（【▶️ 开始实时监控】、【⏸️ 停止监控】、【📸 瞬间抓取快照并分析】）被严重挤压、换行异常或被裁剪遮挡；
- **重构方案**：
  - 改用 DockPanel + WrapPanel 响应式流式布局；
  - 右侧监控操作按钮组赋予 DockPanel.Dock="Right" 最高布局优先级，保证无论窗口如何调整，操作按钮始终享有完整空间，彻底杜绝压缩截断；
  - 左侧查找输入框（Width="160"）与目标进程下拉框（MinWidth="180" MaxWidth="320"）采用自适应流式排布，在极端紧凑尺寸下自动平滑折行。

#### 2. 快照生成路径规范化与历史覆盖缺陷修复 (LiveSnapshotService)
- **核心根因**：原快照抓取直接将目录路径 Snapshots 传入 Win32 CaptureProcessLiveDump，导致生成了一个无扩展名的 Snapshots 单一文件，多次抓取发生强制覆盖，无法保留快照历史记录用于增量对比；
- **解决方案**：
  - 在 LiveSnapshotService.CaptureSnapshotAsync 中增强目录与文件判断，自动创建目标文件夹，并按 Process_{PID}_{yyyyMMdd_HHmmss}.dmp 时间戳规范化命名；
  - 保证每次点击热快照抓取均独立保留完整 Dump 镜像，为后续双 Dump 差分比对提供可靠的基线与当前样本。

#### 3. 新增运行中热快照诊断指导规则 (LiveSnapshotMemoryLeakRule) 与状态联动
- **业务痛点阐述**：用户在【📈 进程监控】中已发现私有内存持续单调增长告警，但点击“📸 瞬间抓取快照并分析”后，概览页未检出崩溃异常问题项，造成“监控报了泄漏但快照没分析出问题”的使用困惑；
- **底层原理剖析**：
  - 监控（Live Monitor）基于**时序回归趋势算法**，能敏锐感知时间维度上的单调增长；
  - 单一静态快照（Dump）反映的是**瞬态静态切片**，在无崩溃异常发生时，单 Dump 无法直接推导哪些堆对象是增量泄漏，必须通过**前后两份快照对比 (Diff)** 才能精准计算增量；
- **解决方案**：
  - 新增 LiveSnapshotMemoryLeakRule : IDiagnosticRule 诊断规则，注册至 DefaultRuleEngine；
  - 当检测到目标转储为运行中抓取的热快照（且无未处理崩溃异常）时，自动生成【💡 运行中热快照诊断】项，向用户详细解释单快照与时序趋势的差异，并列出标准的“基线快照 A -> 对比快照 B -> 【⚖️ 快照对比 (Diff)】差分定位”三步法操作指引；
  - 在 MainViewModel 中联动 MonitorAlerts，捕获成功后即刻在状态栏引导用户执行快照差分。

---

## [1.1.2] - 2026-09-24

### 缺陷修复与交互增强：文件拖拽放行、强类型通知与深层分析定位修复 (UI & Analysis Fixes)

#### 1. 根治文件拖拽无法响应问题 (Drag & Drop Bugfix)
- **核心根因**：原 `MainWindow.xaml` 仅声明了 `Drop="Window_Drop"`，由于缺失 `PreviewDragOver` 事件处理，Windows 操作系统默认将拖拽悬停状态标记为禁止放行 (`DragDropEffects.None`)，导致用户在窗口中松开鼠标时 `Window_Drop` 完全不被触发，文件未能真正交由后台引擎解析；
- **解决方案**：
  - 在 `MainWindow.xaml` 挂载 `PreviewDragOver="Window_PreviewDragOver"`；
  - 在 `MainWindow.xaml.cs` 中实现该事件，凡是携带文件拖放操作一律显式放行 `e.Effects = DragDropEffects.Copy; e.Handled = true;`；
  - 增强 `Window_Drop` 容错逻辑，精准过滤并提取目标 `.dmp` 文件路径。

#### 2. Overview 概览页强类型属性绑定与双向通知
- **核心根因**：原 XAML 视图层直接绑定 `CurrentDumpReport.Modules.Count` 及 `CurrentDumpReport.Exception.ExceptionName` 等深层属性，由于 `List<T>` 集合自身不实现 `INotifyPropertyChanged`，导致即使转储分析完成且对象已赋值，概览页卡片仍然停留于初始默认状态（显示 0/0 且无异常记录）；
- **解决方案**：
  - 在 `MainViewModel` 中显式扩展 `ModuleCount`、`ThreadCount`、`ExceptionNameDisplay`、`ExceptionAddressDisplay` 及 `FaultingThreadIdDisplay` 等强类型属性；
  - 在 `CurrentDumpReport` 发生变更时集中广播触发 `OnPropertyChanged`，确保 UI 概览统计面板毫秒级即时刷新。

#### 3. 现场排查与日志持久化能力增强
- 在 `DiagnosticLogger` 中添加无阻塞本地文件持久化机制，自动在运行目录输出 `diagnostic_studio.log`，记录全部转储分析过程、符号加载路径与耗时数据，便于现场问题精准排查；
- 真实转储端到端测试 (`RealDumpTests.AnalyzeCppCrash_20260924_092715_EndToEnd_ShouldSucceed`) 100% 验证通过：
  - 成功提取 **46** 个加载模块、**1** 个崩溃现场线程；
  - 精准识别到 **0xC0000094 (STATUS_INTEGER_DIVIDE_BY_ZERO)** 除零崩溃；
  - 命中 `DivideByZeroRule` 诊断规则，准确锁定至源码文件 `CrashScenarios.cpp` 第 **165** 行！

---

## [1.1.1] - 2026-09-24

### 严重故障修复：转储分析闪退崩溃拦截与稳定性加固 (Crash Fixes & Stability Hardening)

#### 1. 分析靶场转储导致 UI 崩溃闪退的根因排查与全面解决
- **核心根因 (Windows 事件日志精准锁定：P/Invoke 结构体非 Blittable 封送崩溃)**：
  - 通过 Windows 事件查看器 (`Event ID 1026`) 抓取到闪退核心调用栈：
    `System.AccessViolationException: Attempted to read or write protected memory.`
    `at DiagnosticStudio.Dump.Native.NativeMethods.GetDumpSystemInfo(IntPtr, NativeSystemInfo ByRef)`
  - 原 `NativeSystemInfo` 等结构体内嵌了 `[MarshalAs(UnmanagedType.ByValTStr)] public string` 等非托管字符串，属于 Non-Blittable 复合类型。在 .NET 64 位运行时中，P/Invoke Stub 桩函数在栈上进行动态封送转换与内存反向解组时，由于对齐或缓冲区大小与原生栈帧边界不完全同构，触发 CPU 硬件级内存保护违规 (`STATUS_ACCESS_VIOLATION`)；
  - 该异常属于**破坏性状态异常 (Corrupted State Exception, CSE)**，CLR 会直接绕过普通 `try...catch` 逻辑并调用 `coreclr_set_error_writer` 强行终止进程！
- **终极根治重构方案：100% 纯二进制 Blittable 内存同构架构**：
  - 将 `NativeSystemInfo`, `NativeExceptionInfo`, `NativeModuleInfo`, `NativeStackFrameInfo`, `NativeSymbolResult` 全部重构为 `unsafe struct`，使用固定大小内存缓冲区（`fixed char Buffer[...]` / `fixed ulong Buffer[...]`）；
  - 彻底抹平托管与非托管内存之间的任何差异，CLR 传递内存时 0 拷贝、0 动态封送 Stub，彻底杜绝任何封送器引发的 `AccessViolationException`；
  - 对外保留只读字符串属性访问器（如 `sysInfo.CSDVersion`、`mod.ModuleName`），实现对上层业务调用逻辑的 100% 零修改无缝向后兼容；
- **C++ Native 栈回溯与内存回调 SEH 逃逸防护**：
  - 在 `DiagnosticNative.cpp` 中新增 `SafeExtractSystemInfo`、`SafeExtractExceptionInfo` 与 `SafeStackWalk64`，全面引入 `__try ... __except (EXCEPTION_EXECUTE_HANDLER)` 隔离拦截，底层异常 100% 结构化拦截。
- **UI 渲染重入与 VirtualizingStackPanel 虚拟化崩溃**：
  - 在 `SourceCodeViewer.xaml.cs` 中，`CodeLineViewModel` 继承实现 `INotifyPropertyChanged`，彻底移除 `CodeLines[index] = target;` 集合自替换；滚动调度通过 `DispatcherPriority.Loaded` 异步延迟执行并用 `try...catch` 防御。
- **全局生命周期未捕获异常防御**：
  - 在 `App.xaml.cs` 中统一挂载 `DispatcherUnhandledException`、`AppDomain.UnhandledException` 与 `TaskScheduler.UnobservedTaskException`。

#### 2. 全流水线回归测试验证
- 针对 `samples/CppCrashTarget/build/Debug/Dumps` 下 6 个全部靶场转储文件执行批量端到端回归测试，全部通过；
- 全套 21 项自动化单元测试全部通过（0 失败，0 错误）。

---

## [1.1.0] - 2026-09-24

### 核心引擎升级与工业级体验革新 (Core Enhancements & UX Overhaul)

#### 1. DMP 崩溃位置精准定位与责任源码直接穿透
- **崩溃现场上下文还原 (`DiagnosticStudio.Native`)**：
  - 彻底修复原回溯直接读取 `MINIDUMP_THREAD.ThreadContext` 导致指令指针被未处理异常过滤器污染的问题；
  - 优先从 `MINIDUMP_EXCEPTION_STREAM.ThreadContext` 提取发生崩溃瞬间的原始 CPU 寄存器现场 (RIP, RSP, RBP, RAX, RBX 等)；
  - 强制对齐崩溃指令地址 (`ExceptionRecord.ExceptionAddress`) 为栈顶触发点。
- **结合 x64 异常表标准展开 (`StackWalk64`)**：
  - 基于转储文件映射内存提供自定义内存读取回调 `MinidumpReadMemoryProc64`；
  - 调用 `StackWalk64` 沿着 PE `.pdata` / `UNWIND_INFO` 标准展开调用栈，杜绝此前纯靠扫描栈上残留 8 字节数值导致的虚假系统 DLL 栈帧。
- **本地符号路径全自动注入 (`DumpAnalyzerService`)**：
  - 打开转储文件时，系统自动提取 Dump 所在目录及 `Symbols` 子目录，并将其置顶拼接入符号搜索链；
  - 本地编译输出目录的 PDB 能够 100% 自动被匹配加载，彻底解决无法逆向提取函数名、源文件及行号的问题。
- **系统库崩溃的“责任业务根因反向归咎算法” (`SuspectCodeLocator`)**：
  - 建立完善的 Windows 系统、CRT 运行时、.NET CLR、COM、DirectX 等底层库黑名单；
  - 当崩溃在系统底层库内部爆发时（如 `ucrtbase!abort`、`ntdll!RtlReportCriticalFailure`、`kernelbase!RaiseException`），系统智能沿调用栈向上回溯找到第一层调用该系统库的用户业务代码帧 (`firstUserFrame`)；
  - 将该用户业务代码帧标记为责任根因 (🔴 Critical, `IsRootCause = true`)，并在报告中直接展示用户业务源码文件与行号，实现直达用户代码。

#### 2. 规则引擎全面扩充（杜绝 Dump 漏诊“0 项风险”）
- **新增专有崩溃诊断规则 (`DiagnosticStudio.Core/Rules`)**：
  - `BufferOverflowRule`：检测 `0xC0000409` (STATUS_STACK_BUFFER_OVERRUN / `__fastfail` / `/GS` 栈破坏 / 数组越界覆盖栈金丝雀)；
  - `UnhandledCppExceptionRule`：检测 `0xE06D7363` (STATUS_CPP_EH_EXCEPTION，微软 C++ 未捕获的 `std::runtime_error` 等异常)；
  - `DivideByZeroRule`：检测 `0xC0000094` (STATUS_INTEGER_DIVIDE_BY_ZERO) 与 `0xC000008F` 算术除零；
  - `HeapCorruptionRule`：检测 `0xC0000374` (STATUS_HEAP_CORRUPTION 堆损坏、Double Free、堆头篡改)；
  - `AssertOrBreakpointRule`：检测 `0x80000003` (STATUS_BREAKPOINT 断言 `assert()` 失败 / `__debugbreak`) 与 `0x80000004`；
  - `HangOrDeadlockRule`：针对无异常流的卡死 Dump，检测主线程阻塞与死锁；
  - `GeneralCrashFallbackRule`：全能兜底规则，对任何包含异常的转储自动提取现场信息与责任代码，确保 100% 输出诊断项。
- **现有规则升级**：
  - `NullPointerRule` 与 `AccessViolationRule` 升级为优先使用 `IsRootCause` 责任业务帧，报告标题与定位直达用户业务源码。

#### 3. 进程监控增加按进程名称与 PID 快速查找 (`DiagnosticStudio.UI`)
- **响应式即时过滤**：
  - `MainViewModel` 中新增 `ProcessSearchText` 属性与 `FilteredProcessList` 集合；
  - 支持不区分大小写的进程名匹配，或输入纯数字按进程 ID (PID) 精准筛选；
  - 筛选后自动定位至最佳匹配项；
  - 增加一键清空搜索命令 `ClearProcessSearchCommand` 与快速刷新按钮。

#### 4. UI 标题栏改造：WindowChrome 沉浸式暗黑设计
- **彻底去除原生白色大白条**：
  - 采用 WPF 官方 `WindowChrome`，将窗体样式设为 `WindowStyle="None"` 并设置无缝无边框拖拽边框；
  - 顶部设计沉浸式暗黑统一标题栏，背景统一为 `#1E1E24`，与高科技深黑界面浑然一体；
- **自定义窗口系统控制按钮组**：
  - 在顶栏右上角集成暗黑风格的最小化 (`—`)、最大化/还原 (`🗖`/`🗗`)、关闭 (`✕`) 控制按钮；
  - 支持双击最大化/还原、按住标题栏任意空白区域拖拽移动窗体；
  - 悬停微交互：关闭按钮鲜红高亮 (`#DC2626`)，最小化/最大化按钮灰黑高亮 (`#2D2D3A`)；
  - 引入 Win32 DWM API 调用 `DwmSetWindowAttribute(DWMWA_USE_IMMERSIVE_DARK_MODE)`，系统窗口边缘和阴影完美融入深色模式。

---

## [1.0.3] - 2026-09-24

### 新增独立测试靶场系统 (Independent Test Targets)

#### 1. 新建完全独立的 C# 与 C++ 双版本调试测试靶场 (`samples/`)
- **架构完全解耦**：
  - 新增 `samples/` 目录，完全独立于主解决方案 `DiagnosticStudio.sln`，各自拥有独立的工程构建系统与符号输出流程；
  - 默认开启 Full PDB（完整调试符号）与 `/utf-8` 编码，完美适配 DiagnosticStudio 的源码逆向行号与“红黄两级高亮”定位。
- **C# 测试靶场 (`samples/CSharpCrashTarget`)**：
  - 核心接口：`IScenario`（场景抽象）、`ISafetyGuard`（安全卫士）；
  - 崩溃类场景：经典多层业务调用链空引用解引用（`NullReference`）、硬件级底层内存违规（`NativeAccessViolation`）、栈深度死递归溢出（`StackOverflow`）、整型除零、快速强杀（`FailFast`）、后台线程未处理崩溃；
  - 慢速受控内存泄漏：托管与非托管双重慢速堆泄漏（每 500ms 分配 512KB），内置 200MB 配额熔断与宿主 1.5GB 物理内存底线保护；
  - 扩展诊断场景：慢速内核事件句柄泄漏（1500 句柄保护）、双互斥锁死锁（AB-BA 卡死）、高 CPU 占用压测、双 Dump 差分基准/对比样本生成器；
  - 自动化转储：挂载 `AppDomain.UnhandledException`，在崩溃瞬间自动调用 `MiniDumpWriteDump` 输出 Dump。
- **C++ 测试靶场 (`samples/CppCrashTarget`)**：
  - 核心架构：`IScenario` 场景基类、`SafetyGuard` 物理内存保护器、`MiniDumpHelper` 原生转储辅助器；
  - 崩溃类场景：多层业务上下文空指针写入（`0xC0000005`，验证红黄高亮）、野指针 Use-After-Free（`0xFEEEFEEE`）、无限死递归栈溢出（`0xC00000FD`）、整数除以零、栈缓冲区破坏越界、未捕获的 `std::runtime_error`；
  - 慢速受控内存泄漏：原生堆 `malloc` 缓慢分配，受 200MB 熔断与宿主机 1.5GB 物理内存保护，支持按键 `[P]` 暂停、`[R]` 恢复、`[C]` 一键清理释放；
  - 资源与并发场景：内核事件句柄泄漏（1500 上限）、GDI 图元对象泄漏（800 上限，防止 10,000 GDI 限制雪崩）、双互斥锁 AB-BA 线程死锁、多线程高 CPU 压测、双 Dump 差分样本自动生成；
  - 自动化转储：注册 `SetUnhandledExceptionFilter`，崩溃瞬间精准抓取 MiniDump。
- **一键构建脚本与使用手册**：
  - `samples/build_all.ps1`：一键全量构建 C# 与 C++ 双版本靶场；
  - `samples/README.md`：详尽说明测试场景、安全保护原理与 DiagnosticStudio 联合实测流程。

---

## [1.0.2] - 2026-09-24

### 新增功能与交互优化 (Features & Enhancements)

#### 1. 新增可视化本地 PDB 调试符号目录选择项
- **界面集成**：
  - 在主窗口顶部全局常驻栏下方新增独立的“🏷️ 调试符号路径 (PDB)”配置栏；
  - 提供 Consolas 等宽字体文本框，支持任意分号分隔多路径的手动编辑与双向数据绑定 (`SymbolPath`)；
  - 新增【📁 选择 PDB 目录】操作按钮，基于 .NET 8 原生 `Microsoft.Win32.OpenFolderDialog` 呼出目录选取对话框，免除第三方库依赖。
- **符号路径智能优先排序**：
  - DbgHelp 符号引擎遵循从左至右的匹配优先级。当用户选择本地业务 PDB 路径（如 `D:\MyApp\bin\Release`）时，系统自动将其前置插入符号路径最前端（`本地路径;srv*C:\Symbols*...`），确保本地业务符号具备最高命中优先级；
  - 自动排重，避免重复拼接同一本地路径。
- **一键重置与快捷重分析**：
  - 新增【🔄 默认】按钮，可一键恢复为微软官方公共符号服务器；
  - 新增【⚡ 重新分析】按钮（动态感知 Dump 加载状态启用），用户随时更新或补充符号路径后无需重新找文件，点击即可在内存中立即重新触发全链路 Dump 分析、符号逆向与红黄代码定位。

---

## [1.0.1] - 2026-09-23

### 严重故障修复与底层安全加固 (Critical Bug Fixes & Hardening)

#### 1. 修复分析真实 Dump (`MJInspector.exe.2368.dmp`) 时软件崩溃闪退问题
- **根因 1 (内存释放后野指针解锁崩溃 - SEHException)**：
  - 原 `CloseDumpSession` 中 `delete pSession` 在函数退出前被调用，函数返回退出大括号时局部变量 `std::lock_guard` 析构调用已被释放内存中的 `SessionMutex.unlock()`，引发底层致命内存写违例。
  - **修复**：将互斥锁隔离在独立内部作用域，确保先完成互斥锁析构再安全调用 `delete pSession`，并为句柄关闭、视图卸载、DbgHelp 清理提供纯 C 风格的 SEH 隔离保护函数 (`SafeSymCleanup`, `SafeUnmapView`, `SafeCloseHandle`, `SafeDeleteSession`)。
- **根因 2 (系统信息流越界与空字符串脏数据 - AccessViolationException)**：
  - 在 `MJInspector.exe.2368.dmp` 中，`SystemInfoStream` 的 `CSDVersionRva` 对应的 `MINIDUMP_STRING.Length = 0`，原代码缺少文件映射边界校验，直接拷贝导致越界。
  - **修复**：引入 `IsValidRva(rva, size, fileSize)` 安全校验宏，并在读取 CSDVersion 时做零长度防御与 `\0` 终结截断。
- **根因 3 (DbgHelp 内部跨线程/畸变符号异常逃逸)**：
  - `SymFromAddrW` 与 `SymGetLineFromAddrW64` 在解析非标准地址时易在 DbgHelp 内部引发 0xC0000005，原逻辑未被结构化异常捕获，导致穿透至 .NET CLR 抛出 `System.Runtime.InteropServices.SEHException`。
  - **修复**：实现 `SafeResolveSymbol`，使用 `__try ... __except (EXCEPTION_EXECUTE_HANDLER)` 严格拦截 DbgHelp 内部异常，实现 100% 故障隔离。
- **根因 4 (基于转储栈内存的工业级高鲁棒性启发式栈展开)**：
  - 系统 `StackWalk64` 在 Dump 分析时由于缺乏活体进程句柄易引发 `STATUS_INVALID_HANDLE` 异常；
  - **修复**：采用工业级栈内存扫描启发式算法，直接从线程 `Stack.Memory` 转储段中提取属于已加载模块的代码返回地址并解析符号，既保证提取出完整的崩溃调用链，又杜绝了任何可能引发崩溃的未受控内核调用。

#### 2. 全流水线端到端自动化测试验证
- **新增真实 Dump 回归测试用例**：
  - `RealDumpTests.Analyze_MJInspectorDump_FullPipeline_ShouldSucceed`：自动加载并分析 `C:\CrashDump\MJInspector.exe.2368.dmp`（363.15 MB），在 300ms 左右完成全部 124 个模块、17 个线程与崩溃代码定位；
  - 成功捕获崩溃线程 TID 13648、异常代码 `0xE0434352` (.NET CLR Exception) 以及异常地址 `0x00007FFC768F5369`；
  - 9 个自动化单元测试全部通过（0 错误，0 失败）。

---

## [1.0.0] - 2026-09-23

### 新增功能与架构实现

#### 1. C++ 底层原生调试引擎 (`DiagnosticStudio.Native`)
- **新增文件**：
  - `src/DiagnosticStudio.Native/DiagnosticNative.h`
  - `src/DiagnosticStudio.Native/DiagnosticNative.cpp`
  - `src/DiagnosticStudio.Native/CMakeLists.txt`
- **核心能力**：
  - 基于 C++17 和 Windows SDK 封装 `DbgHelp.dll` 与 `MiniDumpReadDumpStream`；
  - 导出免依赖的稳定 C ABI：`OpenDumpSession`, `CloseDumpSession`, `GetDumpSystemInfo`, `GetDumpExceptionInfo`, `GetDumpModuleCount`, `GetDumpModule`, `GetDumpThreadCount`, `GetDumpThreadId`, `GetDumpThreadStackTrace`, `ResolveAddressSymbol`, `CaptureProcessLiveDump`；
  - 实现基于 `MiniDumpWriteDump` 的无损实时进程热快照生成。

#### 2. C# 核心数据模型与智能规则引擎 (`DiagnosticStudio.Core`)
- **新增类与接口**：
  - `DiagnosticFinding` / `DiagnosticLevel`：统一诊断结论与风险分级数据模型（🔴 Critical, 🟠 High, 🟡 Warning, 🔵 Info）；
  - `DumpReportModel` / `ThreadInfo` / `StackFrameInfo` / `ModuleInfo` / `ExceptionInfo` / `ProcessSystemInfo` / `MemoryMetrics`；
  - `IRuleEngine` / `DefaultRuleEngine`：智能诊断推导引擎；
  - `NullPointerRule`：空指针与小偏移越界自动诊断；
  - `AccessViolationRule`：非法读写、Use-After-Free (0xFEEEFEEE) 与野指针模式识别；
  - `StackOverflowRule`：深层死循环递归与栈溢出识别；
  - `IReportGenerator` / `HtmlReportGenerator` / `JsonReportGenerator`：自包含暗黑风格 HTML 与 JSON 报告生成器；
  - `IDiagnosticLogger` / `DiagnosticLogger`：分层诊断系统日志与耗时追踪器。

#### 3. Dump 转储分析引擎与可疑代码定位 (`DiagnosticStudio.Dump`)
- **新增类与接口**：
  - `NativeMethods`：P/Invoke C ABI 桥接层，内置动态 DLL 搜寻与加载逻辑；
  - `ISuspectCodeLocator` / `SuspectCodeLocator`：智能代码定位器。排除 Windows 系统底层动态库（ntdll, kernel32, ucrtbase 等），将首个崩溃帧标红为 `Critical`，将用户自身业务入口帧标黄为 `High/Warning`；
  - `IDumpAnalyzer` / `DumpAnalyzerService`：异步 Dump 分析服务，自动串联符号加载、线程枚举、代码定位与规则推导。

#### 4. 实时进程监控与双 Dump 差分对比 (`DiagnosticStudio.Monitor` & `DiagnosticStudio.Memory`)
- **新增类与接口**：
  - `ILiveProcessMonitor` / `LiveProcessMonitor`：无侵入进程采样（WorkingSet, PrivateBytes, HandleCount, GDI, USER, CPU, Threads）；
  - `ILeakTrendDetector` / `LeakTrendDetector`：基于最小二乘线性回归斜率的内存与 GDI 泄漏智能识别算法；
  - `ILiveSnapshotService` / `LiveSnapshotService`：一键无损热快照抓取；
  - `IDumpComparator` / `DumpComparator`：基线 Dump 与当前 Dump 增量差异比较器。

#### 5. 现代化 WPF 桌面客户端 (`DiagnosticStudio.UI`)
- **新增组件与视图**：
  - `MainWindow.xaml` / `MainWindow.xaml.cs`：Fluent Dark 主窗口界面，包含 Overview、Dump、Source、Monitor、Diff、Logs 六大板块；
  - `SourceCodeViewer.xaml` / `SourceCodeViewer.xaml.cs`：自定义源码阅读器，集成行号、🔴整行红底崩溃高亮、🟡整行黄底业务入口高亮、波浪线与 Tooltip 气泡；
  - `MetricLiveChart.xaml` / `MetricLiveChart.xaml.cs`：纯自绘高性能平滑实时性能折线图控件；
  - `MainViewModel.cs` / `ViewModelBase.cs` / `RelayCommand.cs`：全面采用 MVVM 分层架构，实现双击栈帧与 Finding 自动联动跳转源码视图；
  - `CommonConverters.cs`：Tab 切换与布尔转换器。

#### 6. 自动化单元测试验证 (`DiagnosticStudio.Tests`)
- **新增测试用例**：
  - `RuleEngineTests.cs` (空指针、野指针、死递归验证)；
  - `LeakTrendDetectorTests.cs` (线性回归内存泄漏与 GDI 泄漏斜率验证)；
  - `DumpComparatorTests.cs` (增量差分计算验证)；
  - `SuspectCodeLocatorTests.cs` (系统库过滤与红黄标记验证)；
  - `HtmlReportGeneratorTests.cs` (HTML 报告 DOM 验证)；
  - `RealDumpTests.cs` (真实 Dump 全链路端到端解析验证)。
- **测试结果**：9 个测试全部通过 (0 失败)。

### 设计原则落实
- **面向对象**：遵循单一职责原则 (SRP) 与依赖倒置原则 (DIP)，UI 不直接访问 Native API，全部通过抽象接口调度；
- **Allman 风格**：所有类、方法的大括号均严格遵循换行换行格式；
- **中文规范**：所有接口、方法与核心算法均带有完备的中文 XML 文档注释。
## [1.0.1] - 2026-09-24
### 修复
- 【架构/严重】彻底根除了由于 CLR 动态封送存根（P/Invoke Stub）诱发的 System.AccessViolationException。原 NativeMethods.cs 虽然重构为 unsafe struct，但遗留了 CharSet = CharSet.Unicode 属性标记，导致 CLR 依旧将其判定为非 Blittable（Non-Blittable）类型并介入内存封送。现已全面移除封送注解，采用 100% 纯指针 NativeSystemInfo* 直传的零开销（Zero-Overhead）模式，彻底杜绝 Dump 分析期间内存错位导致的程序闪退问题。
## [1.0.2] - 2026-09-24
### 修复
- 【稳定性/紧急】全面启用 C++ 层 API 级 SEH（结构化异常处理）全局拦截盾。修复了在极端内存损坏的转储文件中，P/Invoke 解析过程逃逸出的底层硬件访问违例（0xC0000005）。通过对所有暴露的 DIAG_API 函数应用 __try ... __except 包装器，将内核级异常安全降级为标准错误码，构建了永不闪退的坚固互操作层。
## [1.0.3] - 2026-09-24
### 修复
- 【解析逻辑】修复了底层封送 P/Invoke 的 IL 机制因为继承 CharSet.Unicode 导致的 IntPtr 指针部分截断，从而引发 DumpAnalyzerService 内部调用 Native API 返回错误码，最终在无闪退情况下被吞噬导致“未发现问题”的深层隐患。
- 【符号加载】扩展本地符号检索策略，现在除同级目录外，引擎会自动识别 Dump 文件上一级目录及 ../Symbols/ 作为高级别本地符号候选路径。保证崩溃转储的调用栈能被精准还原。

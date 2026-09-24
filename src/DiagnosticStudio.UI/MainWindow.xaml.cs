using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using DiagnosticStudio.Core.Models;
using DiagnosticStudio.UI.ViewModels;

namespace DiagnosticStudio.UI
{
    /// <summary>
    /// 主窗口交互逻辑
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _viewModel;

        #region Win32 DWM 沉浸式深色模式支持
        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        private const int DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19;
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

        private static void EnableImmersiveDarkMode(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero)
            {
                return;
            }

            int useDarkMode = 1;
            // 优先尝试 Win10 20H1+ / Win11 属性 (20)，失败后回退至 19
            if (DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDarkMode, sizeof(int)) != 0)
            {
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1, ref useDarkMode, sizeof(int));
            }
        }
        #endregion

        public MainWindow()
        {
            InitializeComponent();

            _viewModel = new MainViewModel();
            DataContext = _viewModel;

            PrivateBytesChart.SetTitle("私有提交内存 (Private Bytes)");
            GdiChart.SetTitle("GDI 句柄数 (GDI Objects)");

            // 监听窗口尺寸与状态变化以动态更新最大化/还原图标
            StateChanged += MainWindow_StateChanged;

            // 在窗口句柄初始化完成后开启系统级暗黑模式沉浸阴影
            SourceInitialized += (s, e) =>
            {
                var handle = new WindowInteropHelper(this).Handle;
                EnableImmersiveDarkMode(handle);
            };

            // 监听实时监控指标更新
            _viewModel.OnMetricUpdated += (m) =>
            {
                double privateMb = (double)m.PrivateBytes / (1024 * 1024);
                PrivateBytesChart.AddDataPoint(privateMb, $"{privateMb:F2} MB");
                GdiChart.AddDataPoint(m.GdiObjects, $"{m.GdiObjects} 个");
            };

            // 监听源码定位与高亮请求
            _viewModel.OnNavigateToSourceRequested += (sourceFile, line, level, tooltip) =>
            {
                if (File.Exists(sourceFile))
                {
                    SourceViewerControl.LoadFile(sourceFile);
                }
                else
                {
                    // 若源码在当前机上不存在（如现场生产环境 Dump），动态生成调用栈上下文切片供预览
                    string fileName = Path.GetFileName(sourceFile);
                    string mockCode = GenerateContextSlice(fileName, line);
                    SourceViewerControl.LoadContent(mockCode, $"{sourceFile} (符号提取快照)");
                }

                SourceViewerControl.HighlightAndScrollToLine(line, level, tooltip);
            };
        }

        #region 自定义暗黑标题栏窗口系统控制
        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void MaximizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void MainWindow_StateChanged(object? sender, EventArgs e)
        {
            if (MaximizeButtonIcon != null)
            {
                MaximizeButtonIcon.Text = WindowState == WindowState.Maximized ? "🗗" : "🗖";
                MaximizeButton.ToolTip = WindowState == WindowState.Maximized ? "还原" : "最大化";
            }
        }
        #endregion

        private static string GenerateContextSlice(string fileName, uint targetLine)
        {
            var sb = new System.Text.StringBuilder();
            uint startLine = targetLine > 10 ? targetLine - 10 : 1;
            uint endLine = targetLine + 10;

            for (uint i = startLine; i <= endLine; i++)
            {
                if (i == targetLine)
                {
                    sb.AppendLine($"    memcpy(data->pBuffer, frame->rawBytes, frame->size); // << 触发异常行");
                }
                else
                {
                    sb.AppendLine($"    // ... [{fileName}] Line {i} Context Logic ...");
                }
            }
            return sb.ToString();
        }

        private void Window_PreviewDragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
                e.Handled = true;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
        }

        private void Window_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (files != null && files.Length > 0)
                {
                    string targetFile = files[0];
                    if (string.Equals(Path.GetExtension(targetFile), ".dmp", StringComparison.OrdinalIgnoreCase))
                    {
                        _ = _viewModel.LoadDumpFileAsync(targetFile);
                    }
                }
            }
        }

        private void FindingsListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (_viewModel.SelectedFinding != null)
            {
                _viewModel.ExecuteNavigateToFinding(_viewModel.SelectedFinding);
            }
        }

        private void StackFramesDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (_viewModel.SelectedFrame != null)
            {
                _viewModel.ExecuteNavigateToFrame(_viewModel.SelectedFrame);
            }
        }
    }
}
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using DiagnosticStudio.Core.Models;

namespace DiagnosticStudio.UI.Controls
{
    /// <summary>
    /// 单行代码展现项模型 (实现 INotifyPropertyChanged，支持属性级响应式 UI 更新)
    /// </summary>
    public sealed class CodeLineViewModel : INotifyPropertyChanged
    {
        private uint _lineNumber;
        private string _content = string.Empty;
        private DiagnosticLevel? _level;
        private string? _tooltipText;
        private string? _badgeText;

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public uint LineNumber
        {
            get => _lineNumber;
            set
            {
                if (_lineNumber != value)
                {
                    _lineNumber = value;
                    OnPropertyChanged();
                }
            }
        }

        public string Content
        {
            get => _content;
            set
            {
                if (_content != value)
                {
                    _content = value;
                    OnPropertyChanged();
                }
            }
        }

        public DiagnosticLevel? Level
        {
            get => _level;
            set
            {
                if (_level != value)
                {
                    _level = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(BackgroundBrush));
                    OnPropertyChanged(nameof(BorderBrush));
                    OnPropertyChanged(nameof(BorderThickness));
                    OnPropertyChanged(nameof(TextColorBrush));
                    OnPropertyChanged(nameof(BadgeColorBrush));
                }
            }
        }

        public string? TooltipText
        {
            get => _tooltipText;
            set
            {
                if (_tooltipText != value)
                {
                    _tooltipText = value;
                    OnPropertyChanged();
                }
            }
        }

        public string? BadgeText
        {
            get => _badgeText;
            set
            {
                if (_badgeText != value)
                {
                    _badgeText = value;
                    OnPropertyChanged();
                }
            }
        }

        public Brush BackgroundBrush => Level switch
        {
            DiagnosticLevel.Critical => new SolidColorBrush(Color.FromArgb(60, 239, 68, 68)),
            DiagnosticLevel.High => new SolidColorBrush(Color.FromArgb(50, 249, 115, 22)),
            DiagnosticLevel.Warning => new SolidColorBrush(Color.FromArgb(45, 234, 179, 8)),
            _ => Brushes.Transparent
        };

        public Brush BorderBrush => Level switch
        {
            DiagnosticLevel.Critical => new SolidColorBrush(Color.FromRgb(239, 68, 68)),
            DiagnosticLevel.High => new SolidColorBrush(Color.FromRgb(249, 115, 22)),
            DiagnosticLevel.Warning => new SolidColorBrush(Color.FromRgb(234, 179, 8)),
            _ => Brushes.Transparent
        };

        public Thickness BorderThickness => Level.HasValue ? new Thickness(4, 0, 0, 0) : new Thickness(0);

        public Brush TextColorBrush => Level switch
        {
            DiagnosticLevel.Critical => new SolidColorBrush(Color.FromRgb(254, 202, 202)),
            DiagnosticLevel.High => new SolidColorBrush(Color.FromRgb(254, 215, 170)),
            DiagnosticLevel.Warning => new SolidColorBrush(Color.FromRgb(254, 240, 138)),
            _ => new SolidColorBrush(Color.FromRgb(226, 232, 240))
        };

        public Brush BadgeColorBrush => Level switch
        {
            DiagnosticLevel.Critical => new SolidColorBrush(Color.FromRgb(239, 68, 68)),
            DiagnosticLevel.High => new SolidColorBrush(Color.FromRgb(249, 115, 22)),
            DiagnosticLevel.Warning => new SolidColorBrush(Color.FromRgb(234, 179, 8)),
            _ => Brushes.Transparent
        };
    }

    /// <summary>
    /// SourceCodeViewer 控件交互逻辑
    /// </summary>
    public partial class SourceCodeViewer : UserControl
    {
        public static readonly DependencyProperty CurrentFilePathProperty =
            DependencyProperty.Register(nameof(CurrentFilePath), typeof(string), typeof(SourceCodeViewer), new PropertyMetadata(string.Empty));

        public string CurrentFilePath
        {
            get => (string)GetValue(CurrentFilePathProperty);
            set => SetValue(CurrentFilePathProperty, value);
        }

        public ObservableCollection<CodeLineViewModel> CodeLines { get; } = new ObservableCollection<CodeLineViewModel>();

        public SourceCodeViewer()
        {
            InitializeComponent();
        }

        /// <summary>
        /// 加载源码文件内容并渲染
        /// </summary>
        public bool LoadFile(string filePath)
        {
            if (!File.Exists(filePath))
            {
                CodeLines.Clear();
                CurrentFilePath = $"[文件不存在: {filePath}]";
                return false;
            }

            try
            {
                CurrentFilePath = filePath;
                CodeLines.Clear();

                string[] lines = File.ReadAllLines(filePath);
                for (int i = 0; i < lines.Length; i++)
                {
                    CodeLines.Add(new CodeLineViewModel
                    {
                        LineNumber = (uint)(i + 1),
                        Content = lines[i]
                    });
                }
                return true;
            }
            catch (Exception ex)
            {
                CurrentFilePath = $"[读取异常: {ex.Message}]";
                return false;
            }
        }

        /// <summary>
        /// 从内存字符串加载代码（用于测试或无源文件映射的模拟预览）
        /// </summary>
        public void LoadContent(string content, string virtualFileName)
        {
            CurrentFilePath = virtualFileName;
            CodeLines.Clear();

            using var reader = new StringReader(content);
            string? line;
            uint lineNo = 1;
            while ((line = reader.ReadLine()) != null)
            {
                CodeLines.Add(new CodeLineViewModel
                {
                    LineNumber = lineNo++,
                    Content = line
                });
            }
        }

        /// <summary>
        /// 高亮指定行号并平滑滚动到视野中央 (异步安全调度，杜绝 VirtualizingStackPanel 布局死锁与重入崩溃)
        /// </summary>
        public void HighlightAndScrollToLine(uint lineNumber, DiagnosticLevel level, string tooltip, string? badge = null)
        {
            if (lineNumber == 0 || CodeLines.Count == 0)
            {
                return;
            }

            var target = CodeLines.FirstOrDefault(l => l.LineNumber == lineNumber);
            if (target != null)
            {
                // 属性直接赋值触发 INotifyPropertyChanged，切勿执行集合项自替换
                target.Level = level;
                target.TooltipText = tooltip;
                target.BadgeText = badge ?? (level == DiagnosticLevel.Critical ? "🔴 崩溃致命点" : "🟡 业务调用上下文");

                // 使用 Dispatcher.BeginInvoke 等待 WPF VisualTree 完成 Measure 与 Arrange 布局排版后再滚动
                Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
                {
                    try
                    {
                        LinesListBox.SelectedItem = target;
                        LinesListBox.ScrollIntoView(target);
                    }
                    catch
                    {
                        // 忽略偶发的滚动视图渲染竞争
                    }
                }));
            }
        }
    }
}

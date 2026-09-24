using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using DiagnosticStudio.Core.Logging;

namespace DiagnosticStudio.UI
{
    /// <summary>
    /// 应用程序生命周期与全局未捕获异常防御调度
    /// </summary>
    public partial class App : Application
    {
        private static readonly IDiagnosticLogger _logger = DiagnosticLogger.Instance;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // 1. UI 线程未处理异常捕获 (防止任何界面绑定或布局异常直接导致闪退)
            DispatcherUnhandledException += App_DispatcherUnhandledException;

            // 2. 应用程序域全局未捕获异常捕获 (后台线程或严重故障)
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

            // 3. 异步任务未观察异常捕获
            TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;

            _logger.Info("AppLifecycle", "DiagnosticStudio.UI 已启动，全生命周期异常保护与日志监听已挂载。");
        }

        private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            string errorDetails = $"UI 调度器捕获到未处理异常: {e.Exception.Message}\n堆栈:\n{e.Exception.StackTrace}";
            _logger.Error("DispatcherUnhandledException", errorDetails);

            MessageBox.Show(
                $"程序在渲染或界面调度时发生异常，已进行防御拦截:\n\n{e.Exception.Message}\n\n详细信息已记录至诊断日志中。",
                "运行时异常保护",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            // 标记为已处理，防止程序崩溃闪退
            e.Handled = true;
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception ex)
            {
                string msg = $"AppDomain 致命未捕获异常 (IsTerminating={e.IsTerminating}): {ex.Message}\n堆栈:\n{ex.StackTrace}";
                _logger.Error("AppDomainUnhandledException", msg);
            }
            else
            {
                _logger.Error("AppDomainUnhandledException", $"AppDomain 发生非托管或未知致命异常 (IsTerminating={e.IsTerminating}): {e.ExceptionObject}");
            }
        }

        private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            _logger.Warning("UnobservedTaskException", $"捕获到未观察的任务异常: {e.Exception.Message}");
            // 标记异常已观察，防止进程被崩溃退出
            e.SetObserved();
        }
    }
}


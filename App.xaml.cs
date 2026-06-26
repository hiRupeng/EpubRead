using System.IO;
using System.Windows;
using System.Windows.Threading;
using EpubRead.Services;

namespace EpubRead;

public partial class App : Application
{
    public static string AppDataDir { get; private set; } = string.Empty;
    public static string DbPath { get; private set; } = string.Empty;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 全局未处理异常捕获，避免闪退
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        // 初始化应用数据目录
        AppDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "EpubRead");
        Directory.CreateDirectory(AppDataDir);

        // 封面缓存目录
        var coversDir = Path.Combine(AppDataDir, "covers");
        Directory.CreateDirectory(coversDir);

        // 初始化 SQLite 数据库
        DbPath = Path.Combine(AppDataDir, "epubread.db");
        var bookshelfService = new BookshelfService(DbPath);
        bookshelfService.InitializeDatabase();

        // 阅读设置服务
        var settingsService = new ReadingSettingsService(DbPath);
        settingsService.InitializeTable();

        // 创建服务
        var epubParser = new EpubParser();

        // 启动主窗口
        var mainWindow = new MainWindow(bookshelfService, epubParser, settingsService, AppDataDir);
        mainWindow.Show();
    }

    /// <summary>
    /// UI 线程未处理异常
    /// </summary>
    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(
            $"发生未预期的错误：\n\n{e.Exception.Message}",
            "应用程序错误",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }

    /// <summary>
    /// 非 UI 线程未处理异常
    /// </summary>
    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            MessageBox.Show(
                $"发生严重错误，程序即将关闭：\n\n{ex.Message}",
                "严重错误",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Task 中未观察到的异常
    /// </summary>
    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        e.SetObserved();
    }
}

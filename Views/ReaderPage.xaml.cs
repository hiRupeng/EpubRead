using System.Windows;
using System.Windows.Controls;
using EpubRead.Models;
using EpubRead.ViewModels;

namespace EpubRead.Views;

public partial class ReaderPage : Page
{
    private readonly ReaderViewModel _viewModel;

    public ReaderPage(ReaderViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = _viewModel;

        _viewModel.ContentRequested += OnContentRequested;
        _viewModel.GoBackRequested += OnGoBackRequested;

        // 确保 WebView2 环境就绪后再初始化
        WebView.CoreWebView2InitializationCompleted += OnWebViewReady;
        EnsureWebView2Environment();
    }

    /// <summary>
    /// 由外部调用，传入要打开的书籍
    /// </summary>
    public void OpenBook(Book book)
    {
        _viewModel.LoadBook(book);
    }

    private async void EnsureWebView2Environment()
    {
        try
        {
            // WebView2 需要先确保运行时环境
            var userDataFolder = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "EpubRead", "WebView2");

            var env = await Microsoft.Web.WebView2.Core.CoreWebView2Environment
                .CreateAsync(null, userDataFolder);

            await WebView.EnsureCoreWebView2Async(env);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"WebView2 初始化失败：{ex.Message}\n请确保已安装 Microsoft Edge WebView2 运行时。",
                "初始化错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnWebViewReady(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2InitializationCompletedEventArgs e)
    {
        if (e.IsSuccess)
        {
            WebView.CoreWebView2.Settings.IsScriptEnabled = true;
            WebView.CoreWebView2.Settings.IsWebMessageEnabled = false;
        }
    }

    private void OnContentRequested(object? sender, string html)
    {
        // 确保在 UI 线程上执行
        Dispatcher.Invoke(() =>
        {
            if (WebView.CoreWebView2 != null)
            {
                WebView.CoreWebView2.NavigateToString(html);
            }
        });
    }

    private void OnGoBackRequested(object? sender, EventArgs e)
    {
        var navService = NavigationService;
        if (navService?.CanGoBack == true)
            navService.GoBack();
    }
}

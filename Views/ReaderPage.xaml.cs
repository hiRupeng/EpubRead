using System.Windows;
using System.Windows.Controls;
using EpubRead.Models;
using EpubRead.ViewModels;

namespace EpubRead.Views;

public partial class ReaderPage : Page
{
    private readonly ReaderViewModel _viewModel;
    private string? _pendingHtml;
    private string? _pendingAnchor;
    private bool _navigationCompleted;

    public ReaderPage(ReaderViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = _viewModel;

        _viewModel.ContentRequested += OnContentRequested;
        _viewModel.ScrollToAnchorRequested += OnScrollToAnchorRequested;
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
        if (!e.IsSuccess) return;

        WebView.CoreWebView2.Settings.IsScriptEnabled = true;
        WebView.CoreWebView2.Settings.IsWebMessageEnabled = false;

        // WebView2 就绪后，如果有等待的 HTML 内容则立即渲染
        if (_pendingHtml != null)
        {
            WebView.CoreWebView2.NavigateToString(_pendingHtml);
            _pendingHtml = null;
            // NavigateToString 完成后会触发 NavigationCompleted
            WebView.CoreWebView2.NavigationCompleted += OnNavigationCompleted;
        }
        else
        {
            _navigationCompleted = true;
        }
    }

    private void OnContentRequested(object? sender, string html)
    {
        Dispatcher.Invoke(() =>
        {
            if (WebView.CoreWebView2 != null)
            {
                // 每次 NavigateToString 后订阅 NavigationCompleted
                WebView.CoreWebView2.NavigateToString(html);
                // 先标记为未完成，等待 NavigationCompleted 回调
                _navigationCompleted = false;
                WebView.CoreWebView2.NavigationCompleted += OnNavigationCompleted;
            }
            else
            {
                _pendingHtml = html;
            }
        });
    }

    private void OnScrollToAnchorRequested(object? sender, string? anchor)
    {
        if (string.IsNullOrEmpty(anchor) || WebView.CoreWebView2 == null)
            return;

        if (!_navigationCompleted)
        {
            _pendingAnchor = anchor;
            return;
        }

        _ = ScrollToAnchorAsync(anchor);
    }

    private void OnNavigationCompleted(object? sender, EventArgs e)
    {
        WebView.CoreWebView2!.NavigationCompleted -= OnNavigationCompleted;
        _navigationCompleted = true;

        if (_pendingAnchor != null)
        {
            var anchor = _pendingAnchor;
            _pendingAnchor = null;
            _ = ScrollToAnchorAsync(anchor);
        }
    }

    private async Task ScrollToAnchorAsync(string anchor)
    {
        if (WebView.CoreWebView2 == null) return;

        await Task.Delay(50);
        try
        {
            var escapedAnchor = anchor.Replace("'", "\\'").Replace("\"", "\\\"");
            var script = $@"
                (function() {{
                    var el = document.getElementById('{escapedAnchor}');
                    if (!el) el = document.querySelector('[name=""{escapedAnchor}""]');
                    if (el) {{
                        var top = el.getBoundingClientRect().top + window.scrollY - 16;
                        window.scrollTo({{ top: top, behavior: 'smooth' }});
                    }}
                }})();
            ";
            await WebView.CoreWebView2.ExecuteScriptAsync(script);
        }
        catch
        {
        }
    }

    private void OnGoBackRequested(object? sender, EventArgs e)
    {
        var navService = NavigationService;
        if (navService?.CanGoBack == true)
            navService.GoBack();
    }
}

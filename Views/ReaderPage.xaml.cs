using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using EpubRead.Models;
using EpubRead.ViewModels;

namespace EpubRead.Views;

public partial class ReaderPage : System.Windows.Controls.Page
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
        _viewModel.StyleInjectionRequested += OnStyleInjectionRequested;

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
        if (!e.IsSuccess)
        {
            // 初始化失败：显示错误信息
            _pendingHtml = "<html><body style='background:#1E1E2E;color:#E8E8EC;display:flex;align-items:center;justify-content:center;font-family:sans-serif;font-size:16px;'><p>WebView2 初始化失败，无法显示内容。</p></body></html>";
            WebView.CoreWebView2.NavigateToString(_pendingHtml);
            _pendingHtml = null;
            return;
        }

        WebView.CoreWebView2.Settings.IsScriptEnabled = true;
        WebView.CoreWebView2.Settings.IsWebMessageEnabled = true;

        // 拦截 epub.local 虚拟主机的资源请求（图片、CSS、字体等），从 EPUB ZIP 中读取返回
        WebView.CoreWebView2.AddWebResourceRequestedFilter(
            "https://epub.local/*",
            Microsoft.Web.WebView2.Core.CoreWebView2WebResourceContext.All);
        WebView.CoreWebView2.WebResourceRequested += OnWebResourceRequested;

        // 订阅导航开始事件（用于外部超链接拦截）
        WebView.CoreWebView2.NavigationStarting += OnNavigationStarting;
        // 订阅 Web 消息事件（用于接收 EPUB 内容中内部链接的点击）
        WebView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;

        // WebView2 就绪后，如果有等待的 HTML 内容则立即渲染
        if (_pendingHtml != null)
        {
            WebView.CoreWebView2.NavigateToString(_pendingHtml);
            _pendingHtml = null;
            WebView.CoreWebView2.NavigationCompleted += OnNavigationCompleted;
        }
        else
        {
            _navigationCompleted = true;
        }
    }

    /// <summary>
    /// WebView2 导航拦截：捕捉内容中的超链接点击
    /// </summary>
    private void OnNavigationStarting(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2NavigationStartingEventArgs e)
    {
        // 非用户触发的导航（如 NavigateToString）一律放行
        if (!e.IsUserInitiated)
            return;

        var uri = e.Uri;
        e.Cancel = true;

        // 外部链接：用系统浏览器打开
        if (!string.IsNullOrEmpty(uri) &&
            (uri.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
             uri.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
             uri.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) ||
             uri.StartsWith("tel:", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = uri,
                    UseShellExecute = true
                });
            }
            catch { }
            return;
        }

        // 内部 EPUB 超链接：交给 ViewModel 导航
        // 对于 NavigateToString 加载的内容，相对链接会被 WebView2 解析为
        // about:blank 派生 URI（如 about:blank/chapter2.html），需要提取实际路径
        if (uri != null && uri.StartsWith("about:", StringComparison.OrdinalIgnoreCase))
        {
            // about:blank/chapter2.html -> chapter2.html
            // 先去掉 "about:"，再找到第一个 "/" 后的部分
            var afterAbout = uri["about:".Length..];
            var slashIdx = afterAbout.IndexOf('/');
            var pathPart = slashIdx >= 0 ? afterAbout[(slashIdx + 1)..] : "";
            if (!string.IsNullOrEmpty(pathPart))
            {
                _viewModel.NavigateToHref(pathPart);
            }
            return;
        }

        _viewModel.NavigateToHref(uri ?? "");
    }

    private void OnContentRequested(object? sender, string html)
    {
        Dispatcher.Invoke(() =>
        {
            if (WebView.CoreWebView2 != null)
            {
                WebView.CoreWebView2.NavigateToString(html);
                _navigationCompleted = false;
                WebView.CoreWebView2.NavigationCompleted += OnNavigationCompleted;
            }
            else
            {
                _pendingHtml = html;
            }
        });
    }

    /// <summary>
    /// 收到样式注入请求（阅读设置变化时，通过 JavaScript 动态更新样式）
    /// </summary>
    private async void OnStyleInjectionRequested(object? sender, string script)
    {
        if (string.IsNullOrEmpty(script) || WebView.CoreWebView2 == null)
            return;

        try
        {
            // 等待导航完成后再注入
            if (!_navigationCompleted)
            {
                await Task.Delay(200);
            }
            await WebView.CoreWebView2.ExecuteScriptAsync(script);
        }
        catch
        {
            // 注入失败忽略（页面未加载完成等场景）
        }
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

        // 注入内部链接拦截脚本（每次导航完成后注入，确保覆盖新内容）
        _ = InjectLinkInterceptorAsync();

        // 处理待处理的锚点滚动
        if (_pendingAnchor != null)
        {
            var anchor = _pendingAnchor;
            _pendingAnchor = null;
            _ = ScrollToAnchorAsync(anchor);
        }
        else
        {
            // 导航完成后，应用已保存的阅读设置样式
            var styleScript = GetStyleInjectionScript();
            if (!string.IsNullOrEmpty(styleScript))
            {
                _ = WebView.CoreWebView2.ExecuteScriptAsync(styleScript);
            }
        }
    }

    /// <summary>
    /// 获取当前阅读设置的样式注入脚本
    /// </summary>
    private string? GetStyleInjectionScript()
    {
        // 通过反射获取 ViewModel 的私有 GenerateStyleScript 方法不太理想，
        // 改为在 ViewModel 中缓存最后一次样式脚本，通过事件传递。
        // StyleInjectionRequested 事件会在设置变化时触发，此处我们依赖导航后的再次应用。
        return null;
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

    /// <summary>
    /// 拦截 epub.local 虚拟主机的资源请求，从 EPUB ZIP 中读取图片/CSS/字体等资源返回给 WebView2
    /// </summary>
    private void OnWebResourceRequested(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2WebResourceRequestedEventArgs e)
    {
        var uri = e.Request.Uri;
        if (!uri.StartsWith("https://epub.local/", StringComparison.OrdinalIgnoreCase))
            return;

        // 提取 URL 中虚拟主机之后的路径（已是相对 ZIP 根的路径）
        var resourcePath = Uri.UnescapeDataString(uri["https://epub.local/".Length..]);
        var (data, mediaType) = _viewModel.LoadResource(resourcePath);

        if (data == null)
        {
            e.Response = WebView.CoreWebView2!.Environment.CreateWebResourceResponse(
                null, 404, "Not Found", "");
            return;
        }

        // MemoryStream 由 WebView2 接管，不可提前释放
        var stream = new System.IO.MemoryStream(data);
        e.Response = WebView.CoreWebView2!.Environment.CreateWebResourceResponse(
            stream, 200, "OK", $"Content-Type: {mediaType}");
    }

    /// <summary>
    /// 接收来自 EPUB 内容的内部链接点击消息，交由 ViewModel 导航
    /// </summary>
    private void OnWebMessageReceived(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            var href = e.TryGetWebMessageAsString();
            if (!string.IsNullOrEmpty(href))
            {
                _viewModel.NavigateToHref(href);
            }
        }
        catch
        {
            // 消息非字符串格式，忽略
        }
    }

    /// <summary>
    /// 注入 JavaScript 拦截 EPUB 内容中的内部超链接点击，
    /// 通过 postMessage 通知宿主处理（避免 about:blank 上相对链接被浏览器安全策略阻止）
    /// </summary>
    private async Task InjectLinkInterceptorAsync()
    {
        if (WebView.CoreWebView2 == null) return;

        const string script = @"
(function() {
    if (window.__epubLinkInterceptorInstalled) return;
    window.__epubLinkInterceptorInstalled = true;
    document.addEventListener('click', function(ev) {
        var a = ev.target.closest ? ev.target.closest('a') : null;
        if (!a) return;
        var href = a.getAttribute('href');
        if (!href) return;
        // 外部链接、邮件、电话链接放行，交给 NavigationStarting 用系统浏览器打开
        if (/^(https?:|mailto:|tel:)/i.test(href)) return;
        // 拦截内部链接（相对路径、锚点等），阻止默认导航并通过 postMessage 通知宿主
        ev.preventDefault();
        ev.stopPropagation();
        window.chrome.webview.postMessage(href);
    }, true);
})();
";
        try
        {
            await WebView.CoreWebView2.ExecuteScriptAsync(script);
        }
        catch
        {
            // 页面未就绪等场景忽略
        }
    }

    private void OnGoBackRequested(object? sender, EventArgs e)
    {
        var navService = NavigationService;
        if (navService?.CanGoBack == true)
            navService.GoBack();
    }

    // ════════════════════════════════════════
    //  键盘快捷键
    // ════════════════════════════════════════

    private void OnPageKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Left:
                _viewModel.NavigatePrevCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.Right:
                _viewModel.NavigateNextCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.F11:
            case Key.Escape:
                if (_viewModel.IsSettingsOpen)
                    _viewModel.ToggleSettingsCommand.Execute(null);
                else if (_viewModel.IsTocOpen)
                    _viewModel.ToggleTocCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }
}

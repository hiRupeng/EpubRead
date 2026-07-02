using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
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
        _viewModel.HighlightsRestoreRequested += OnHighlightsRestoreRequested;
        _viewModel.HighlightCreated += OnHighlightCreated;
        _viewModel.HighlightDeleted += OnHighlightDeleted;
        _viewModel.AnnotationUpdated += OnAnnotationUpdated;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;

        // 应用初始主题画刷
        ApplyThemeResources(_viewModel.UiTheme);

        // 确保 WebView2 环境就绪后再初始化
        WebView.CoreWebView2InitializationCompleted += OnWebViewReady;
        EnsureWebView2Environment();
    }

    /// <summary>ViewModel 属性变化时，若为 UiTheme 则同步更新画刷资源</summary>
    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ReaderViewModel.UiTheme))
        {
            Dispatcher.Invoke(() => ApplyThemeResources(_viewModel.UiTheme));
        }
    }

    /// <summary>
    /// 根据界面主题配色更新 Page.Resources 中的画刷资源。
    /// 这些画刷被各 Style.Triggers 以 DynamicResource 引用，替换后自动刷新交互态颜色。
    /// </summary>
    private void ApplyThemeResources(ReaderUITheme theme)
    {
        void SetBrush(string key, string color)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
            brush.Freeze();
            Resources[key] = brush;
        }

        SetBrush("HoverBrush", theme.HoverBackground);
        SetBrush("PressedBrush", theme.PressedBackground);
        SetBrush("OptionBackgroundBrush", theme.OptionBackground);
        SetBrush("OptionHoverBrush", theme.OptionHoverBackground);
        SetBrush("AccentBrush", theme.AccentColor);
        SetBrush("AccentLightBrush", theme.AccentLightColor);
        SetBrush("PrimaryTextBrush", theme.PrimaryText);
        SetBrush("SecondaryTextBrush", theme.SecondaryText);
        SetBrush("ScrollBarThumbBrush", theme.ScrollBarThumb);

        // 进度条前景渐变（强调色 → 亮色）
        var progressGradient = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 0)
        };
        progressGradient.GradientStops.Add(new GradientStop(
            (Color)ColorConverter.ConvertFromString(theme.AccentLightColor), 0));
        progressGradient.GradientStops.Add(new GradientStop(
            (Color)ColorConverter.ConvertFromString(LightenColor(theme.AccentLightColor, 0.12)), 1));
        progressGradient.Freeze();
        Resources["ProgressBarGradient"] = progressGradient;

        // 底部进度胶囊背景渐变（深→浅的面板色）
        var capsuleGradient = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(0, 1)
        };
        capsuleGradient.GradientStops.Add(new GradientStop(
            (Color)ColorConverter.ConvertFromString(DarkenColor(theme.PanelDeepBackground, 0.04)), 0));
        capsuleGradient.GradientStops.Add(new GradientStop(
            (Color)ColorConverter.ConvertFromString(theme.PanelDeepBackground), 1));
        capsuleGradient.Freeze();
        Resources["ProgressCapsuleBrush"] = capsuleGradient;
    }

    /// <summary>将十六进制颜色按比例调亮</summary>
    private static string LightenColor(string hex, double amount)
    {
        var c = (Color)ColorConverter.ConvertFromString(hex);
        return $"#{(byte)Math.Min(255, c.R + (255 - c.R) * amount):X2}{(byte)Math.Min(255, c.G + (255 - c.G) * amount):X2}{(byte)Math.Min(255, c.B + (255 - c.B) * amount):X2}";
    }

    /// <summary>将十六进制颜色按比例调暗</summary>
    private static string DarkenColor(string hex, double amount)
    {
        var c = (Color)ColorConverter.ConvertFromString(hex);
        return $"#{(byte)Math.Max(0, c.R * (1 - amount)):X2}{(byte)Math.Max(0, c.G * (1 - amount)):X2}{(byte)Math.Max(0, c.B * (1 - amount)):X2}";
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

        // 卡片化：让 WebView2 控件背景透明，圆角处显示卡片背景色（与 HTML body 同色，视觉无方角）
        WebView.DefaultBackgroundColor = System.Drawing.Color.Transparent;

        // 订阅导航开始事件（用于外部超链接拦截）
        WebView.CoreWebView2.NavigationStarting += OnNavigationStarting;
        // 订阅 Web 消息事件（用于接收 EPUB 内容中内部链接的点击）
        WebView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;

        // WebView2 就绪后，如果有等待的 HTML 内容则渲染。
        // NavigationCompleted 在首次 NavigateToString 时不稳定触发（受 HTML 大小、
        // 资源加载等因素影响），因此同时订阅事件 + 用短定时器兜底，确保脚本一定被注入。
        if (_pendingHtml != null)
        {
            var html = _pendingHtml;
            _pendingHtml = null;
            _navigationCompleted = false;

            void OnFirstNavCompleted(object? s, EventArgs ev)
            {
                WebView.CoreWebView2!.NavigationCompleted -= OnFirstNavCompleted;
                OnNavigationCompleted(s, ev);
            }

            WebView.CoreWebView2.NavigationCompleted += OnFirstNavCompleted;
            WebView.CoreWebView2.NavigateToString(html);

            // 兜底：NavigationCompleted 未触发时，延迟注入确保脚本生效
            _ = Task.Run(async () =>
            {
                await Task.Delay(300);
                if (!_navigationCompleted)
                {
                    Dispatcher.Invoke(() =>
                    {
                        if (!_navigationCompleted)
                        {
                            WebView.CoreWebView2!.NavigationCompleted -= OnFirstNavCompleted;
                            OnNavigationCompleted(this, EventArgs.Empty);
                        }
                    });
                }
            });
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
        // 注入高亮笔记交互脚本
        _ = InjectHighlightScriptAsync();

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
    /// 接收来自 EPUB 内容的内部链接点击消息，交由 ViewModel 导航
    /// </summary>
    private void OnWebMessageReceived(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            var raw = e.TryGetWebMessageAsString();
            if (string.IsNullOrEmpty(raw)) return;

            // 支持带类型前缀的消息协议："<type>|<payload>"
            // 兼容旧的纯链接消息（无前缀视为内部链接）
            var sep = raw.IndexOf('|');
            if (sep > 0 && sep < 24)
            {
                var type = raw[..sep];
                var payload = raw[(sep + 1)..];
                switch (type)
                {
                    case "link":
                        _viewModel.NavigateToHref(payload);
                        return;
                    case "hl-create":
                        _viewModel.CreateHighlight(payload);
                        return;
                    case "hl-note":
                        _viewModel.CreateAnnotation(payload);
                        return;
                    case "hl-note-edit":
                        _viewModel.UpdateAnnotation(payload);
                        return;
                    case "hl-delete":
                        _viewModel.DeleteHighlight(payload);
                        return;
                    case "hl-refresh":
                        _viewModel.RefreshHighlights();
                        return;
                    case "hl-bar-show":
                        _viewModel.IsToolbarEnabled = false;
                        return;
                    case "hl-bar-hide":
                        _viewModel.IsToolbarEnabled = true;
                        return;
                    // 其他带前缀消息（如调试）一律忽略，不走到兜底导航
                    default:
                        return;
                }
            }

            // 兼容旧格式：纯 href
            _viewModel.NavigateToHref(raw);
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
        window.chrome.webview.postMessage('link|' + href);
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

    /// <summary>
    /// 注入高亮笔记交互脚本：选中文本显示浮动高亮按钮、渲染/删除高亮。
    /// 使用全局字符偏移定位（遍历 .reading-card 内所有文本节点累加），不依赖 DOM 路径。
    /// 通过 postMessage 与宿主通信（消息格式 "hl-create|{json}" / "hl-delete|{noteId}"）。
    /// </summary>
    private async Task InjectHighlightScriptAsync()
    {
        if (WebView.CoreWebView2 == null) return;

        // 注意：此脚本在 C# 逐字字符串 @"..." 中，其中的 "" 表示一个双引号字符。
        // ExecuteScriptAsync 会以 JSON 解析参数，"" 在 JSON 字符串内合法（表示一个双引号）。
        const string script = @"
(function() {
    if (document.getElementById('hl-toolbar')) return;

    function getCard() { return document.querySelector('.reading-card'); }

    // ── 颜色与样式配置（修改此处即可扩展调色板 / 样式） ──
    var HIGHLIGHT_COLORS = ['#FFE082','#A5D6A7','#90CAF9','#F48FB1','#CE93D8'];
    var UNDERLINE_COLOR  = '#FF7043';
    var ANNOTATION_COLOR = '#FFD180';   // 批注样式专用色（左侧色条 + 半透明底）
    var NOTE_MARK_COLOR  = '#E53935';   // 带批注标注的统一标记色（波浪下划线）

    // 注入样式：带批注的标注叠加红色波浪线，一眼区分“这里有笔记”
    if (!document.getElementById('hl-note-style')) {
        var styleEl = document.createElement('style');
        styleEl.id = 'hl-note-style';
        styleEl.textContent = '.hl-has-note{text-decoration:underline wavy ' + NOTE_MARK_COLOR +
            ';text-decoration-thickness:1.5px;text-underline-offset:3px;}';
        document.head.appendChild(styleEl);
    }

    // ── 构建浮动工具栏 ──
    var bar = document.createElement('div');
    bar.id = 'hl-toolbar';
    bar.style.cssText = 'position:fixed;z-index:99999;display:none;flex-direction:column;gap:4px;' +
        'background:#2B2B2B;border-radius:10px;padding:6px;' +
        'box-shadow:0 6px 20px rgba(0,0,0,0.45);user-select:none;' +
        'font:12px/1 ""Microsoft YaHei"",sans-serif;';

    var row1 = document.createElement('div');
    row1.style.cssText = 'display:flex;flex-direction:row;align-items:center;gap:2px;';
    var row2 = document.createElement('div');
    row2.style.cssText = 'display:flex;flex-direction:row;align-items:center;gap:2px;';

    // 笔记输入面板（默认隐藏，点“笔记”时展开）
    var notePanel = document.createElement('div');
    notePanel.style.cssText = 'display:none;flex-direction:column;gap:6px;width:248px;';

    function makeDot(color) {
        var d = document.createElement('span');
        d.style.cssText = 'display:inline-block;width:22px;height:22px;border-radius:50%;' +
            'background:' + color + ';cursor:pointer;border:2px solid rgba(255,255,255,0.25);' +
            'transition:transform .12s ease;';
        d.title = '高亮';
        d.addEventListener('mouseenter', function(){ d.style.transform = 'scale(1.18)'; });
        d.addEventListener('mouseleave', function(){ d.style.transform = 'scale(1)'; });
        d.addEventListener('mousedown', function(ev){ ev.preventDefault(); ev.stopPropagation(); });
        d.addEventListener('click', function(ev){
            ev.preventDefault(); ev.stopPropagation();
            createFromSelection(color, 'highlight', '');
        });
        return d;
    }

    function makeSep() {
        var s = document.createElement('span');
        s.style.cssText = 'display:inline-block;width:1px;height:20px;background:rgba(255,255,255,0.18);margin:0 2px;';
        return s;
    }

    function makeAction(label, title, handler) {
        var b = document.createElement('span');
        b.textContent = label;
        b.title = title;
        b.style.cssText = 'display:inline-flex;align-items:center;justify-content:center;' +
            'min-width:30px;height:26px;line-height:26px;padding:0 8px;border-radius:6px;color:#E8E8EC;' +
            'cursor:pointer;font-weight:600;transition:background .12s ease;';
        b.addEventListener('mouseenter', function(){ b.style.background = 'rgba(255,255,255,0.12)'; });
        b.addEventListener('mouseleave', function(){ b.style.background = 'transparent'; });
        b.addEventListener('mousedown', function(ev){ ev.preventDefault(); ev.stopPropagation(); });
        b.addEventListener('click', function(ev){
            ev.preventDefault(); ev.stopPropagation();
            handler();
        });
        return b;
    }

    // ── 第一行：笔记、复制、删除 ──
    var noteBtn = makeAction('笔记', '添加批注', function(){
        showNotePanel();
    });
    row1.appendChild(noteBtn);
    row1.appendChild(makeSep());
    var copyBtn = makeAction('复制', '复制选中文本', function(){
        copySelection();
    });
    row1.appendChild(copyBtn);
    row1.appendChild(makeSep());
    // ── 删除按钮（仅点击已有无批注高亮时显示） ──
    var deleteHighlightBtn = makeAction('删除', '删除此高亮', function(){
        var idToDelete = currentHighlightId;
        currentHighlightId = null;
        hideBar();
        if (idToDelete) {
            window.chrome.webview.postMessage('hl-delete|' + idToDelete);
        }
    });
    deleteHighlightBtn.style.color = '#FF6B6B';
    deleteHighlightBtn.style.display = 'none';
    row1.appendChild(deleteHighlightBtn);

    // ── 第二行：高亮色、下划线 ──
    HIGHLIGHT_COLORS.forEach(function(c){ row2.appendChild(makeDot(c)); });
    row2.appendChild(makeSep());
    var underlineBtn = makeAction('U', '下划线', function(){
        createFromSelection(UNDERLINE_COLOR, 'underline', '');
    });
    underlineBtn.style.textDecoration = 'underline';
    underlineBtn.style.textDecorationThickness = '2px';
    underlineBtn.style.textUnderlineOffset = '2px';
    row2.appendChild(underlineBtn);

    // 当前点击的已有高亮 id（仅点击已有无批注高亮弹工具栏时设置）
    var currentHighlightId = null;

    // ── 笔记输入面板：文本框 + 样式选择器 + 保存/取消 ──
    var noteTextarea = document.createElement('textarea');
    noteTextarea.placeholder = '写下你的批注…';
    noteTextarea.style.cssText = 'width:100%;height:72px;resize:none;border-radius:6px;' +
        'border:1px solid rgba(255,255,255,0.2);background:#1E1E1E;color:#E8E8EC;' +
        'padding:6px 8px;font:12px/1.5 ""Microsoft YaHei"",sans-serif;outline:none;box-sizing:border-box;';
    noteTextarea.addEventListener('mousedown', function(ev){ ev.stopPropagation(); });
    noteTextarea.addEventListener('keydown', function(ev){ ev.stopPropagation(); });
    notePanel.appendChild(noteTextarea);

    var styleRow = document.createElement('div');
    styleRow.style.cssText = 'display:flex;flex-direction:row;align-items:center;gap:5px;flex-wrap:wrap;';
    var selectedStyle = { color: HIGHLIGHT_COLORS[0], style: 'highlight' };
    var savedNoteRange = null;   // 笔记面板显示时保存的选区 Range（textarea 聚焦会丢失原选区，保存后用于创建标注）

    function makeStyleOption(color, style, label, isAnnotation) {
        var opt = document.createElement('span');
        opt.title = label;
        opt.__color = color;
        opt.__style = style;
        if (isAnnotation) {
            opt.style.cssText = 'display:inline-block;width:28px;height:22px;border-radius:4px;cursor:pointer;' +
                'background:' + color + '22;border-left:3px solid ' + color + ';' +
                'border-top:1px solid rgba(255,255,255,0.15);border-right:1px solid rgba(255,255,255,0.15);' +
                'border-bottom:1px solid rgba(255,255,255,0.15);transition:transform .12s ease;box-sizing:border-box;';
        } else if (style === 'underline') {
            opt.style.cssText = 'display:inline-flex;align-items:center;justify-content:center;' +
                'min-width:30px;height:22px;border-radius:4px;cursor:pointer;color:#E8E8EC;' +
                'font-weight:600;text-decoration:underline;text-decoration-thickness:2px;text-underline-offset:2px;' +
                'border:1px solid rgba(255,255,255,0.15);transition:transform .12s ease;box-sizing:border-box;';
            opt.textContent = 'U';
        } else {
            opt.style.cssText = 'display:inline-block;width:22px;height:22px;border-radius:50%;cursor:pointer;' +
                'background:' + color + ';border:2px solid rgba(255,255,255,0.25);' +
                'transition:transform .12s ease;box-sizing:border-box;';
        }
        opt.addEventListener('mouseenter', function(){ opt.style.transform = 'scale(1.12)'; });
        opt.addEventListener('mouseleave', function(){ opt.style.transform = 'scale(1)'; });
        opt.addEventListener('mousedown', function(ev){ ev.preventDefault(); ev.stopPropagation(); });
        opt.addEventListener('click', function(ev){
            ev.preventDefault(); ev.stopPropagation();
            selectedStyle.color = color;
            selectedStyle.style = style;
            updateStyleSelection();
        });
        return opt;
    }

    function updateStyleSelection() {
        var opts = styleRow.children;
        for (var i = 0; i < opts.length; i++) {
            var o = opts[i];
            var isSelected = (o.__color === selectedStyle.color && o.__style === selectedStyle.style);
            o.style.outline = isSelected ? '2px solid #60CDFF' : 'none';
            o.style.outlineOffset = '1px';
        }
    }

    HIGHLIGHT_COLORS.forEach(function(c){
        styleRow.appendChild(makeStyleOption(c, 'highlight', '高亮', false));
    });
    styleRow.appendChild(makeStyleOption(UNDERLINE_COLOR, 'underline', '下划线', false));
    styleRow.appendChild(makeStyleOption(ANNOTATION_COLOR, 'annotation', '批注', true));
    notePanel.appendChild(styleRow);

    var noteBtnRow = document.createElement('div');
    noteBtnRow.style.cssText = 'display:flex;flex-direction:row;justify-content:flex-end;gap:6px;';
    notePanel.appendChild(noteBtnRow);

    // 编辑模式状态：点击已有批注标注时进入
    var editMode = null;  // null=新建模式, {id, span} = 编辑模式

    function renderNoteButtons() {
        noteBtnRow.innerHTML = '';
        if (editMode) {
            // 编辑模式：删除、取消
            noteBtnRow.appendChild(makeAction('删除', '删除此标注', function(){
                var idToDelete = editMode.id;
                showConfirmDialog('删除此标注？', function(){
                    window.chrome.webview.postMessage('hl-delete|' + idToDelete);
                });
                editMode = null;
                hideBar();
            }));
            noteBtnRow.appendChild(makeAction('取消', '取消编辑', function(){
                editMode = null;
                hideBar();
            }));
            noteBtnRow.appendChild(makeAction('保存', '保存修改', function(){
                var comment = noteTextarea.value.trim();
                window.chrome.webview.postMessage('hl-note-edit|' + JSON.stringify({
                    id: editMode.id,
                    comment: comment,
                    color: selectedStyle.color,
                    style: selectedStyle.style
                }));
                editMode = null;
                hideBar();
            }));
        } else {
            // 新建模式：取消、保存
            noteBtnRow.appendChild(makeAction('取消', '取消', function(){ hideBar(); }));
            noteBtnRow.appendChild(makeAction('保存', '保存批注', function(){
                var comment = noteTextarea.value.trim();
                if (!savedNoteRange) { hideBar(); return; }
                var info = buildSelectionInfo(savedNoteRange, selectedStyle.color, selectedStyle.style);
                if (info) {
                    info.comment = comment || '';
                    if (comment) {
                        window.chrome.webview.postMessage('hl-note|' + JSON.stringify(info));
                    } else {
                        window.chrome.webview.postMessage('hl-create|' + JSON.stringify(info));
                    }
                }
                savedNoteRange = null;
                var sel = window.getSelection();
                if (sel) sel.removeAllRanges();
                hideBar();
            }));
        }
    }
    renderNoteButtons();

    bar.appendChild(row1);
    bar.appendChild(row2);
    bar.appendChild(notePanel);
    document.body.appendChild(bar);

    // ── 笔记面板显示/隐藏 ──
    function showNotePanel() {
        editMode = null;
        deleteHighlightBtn.style.display = 'none';
        currentHighlightId = null;
        // 保存当前选区：textarea 聚焦后原选区会丢失，保存后用于创建标注
        var sel = window.getSelection();
        if (sel && sel.rangeCount > 0) {
            savedNoteRange = sel.getRangeAt(0).cloneRange();
        }
        row1.style.display = 'none';
        row2.style.display = 'none';
        notePanel.style.display = 'flex';
        noteTextarea.value = '';
        selectedStyle.color = HIGHLIGHT_COLORS[0];
        selectedStyle.style = 'highlight';
        updateStyleSelection();
        renderNoteButtons();
        // 重新定位浮窗（面板比操作行更高更宽）
        repositionBar();
        setTimeout(function(){ noteTextarea.focus(); }, 0);
    }

    // 编辑已有批注标注：复用笔记面板，回填内容与样式，按钮切换为编辑模式
    function showNotePanelForEdit(span, note) {
        editMode = { id: note.id, span: span };
        deleteHighlightBtn.style.display = 'none';
        currentHighlightId = null;
        row1.style.display = 'none';
        row2.style.display = 'none';
        notePanel.style.display = 'flex';
        noteTextarea.value = note.comment || '';
        selectedStyle.color = note.color;
        selectedStyle.style = note.style;
        updateStyleSelection();
        renderNoteButtons();
        // 定位到被点击的标注 span 下方
        var rect = span.getBoundingClientRect();
        bar.style.display = 'flex';
        bar.style.visibility = 'hidden';
        var bw = bar.offsetWidth, bh = bar.offsetHeight;
        bar.style.visibility = 'visible';
        var gap = 8;
        var left = rect.left;
        var top = rect.bottom + gap;
        if (top + bh > window.innerHeight - 4) top = rect.top - bh - gap;
        if (top < 4) top = 4;
        if (left + bw > window.innerWidth - 4) left = window.innerWidth - bw - 4;
        if (left < 4) left = 4;
        bar.style.left = left + 'px';
        bar.style.top = top + 'px';
        setTimeout(function(){
            noteTextarea.focus();
            window.chrome.webview.postMessage('hl-bar-show|');
        }, 0);
    }

    function hideNotePanel() {
        notePanel.style.display = 'none';
        row1.style.display = 'flex';
        row2.style.display = 'flex';
    }

    // 根据当前可见内容重新定位浮窗（选区最后一行右下角）
    function repositionBar() {
        var sel = window.getSelection();
        if (!sel || sel.rangeCount === 0 || sel.isCollapsed) return;
        var rects = sel.getRangeAt(0).getClientRects();
        if (rects.length === 0) return;
        var rect = rects[rects.length - 1];
        if (rect.width === 0 && rect.height === 0) return;
        bar.style.visibility = 'hidden';
        var bw = bar.offsetWidth, bh = bar.offsetHeight;
        bar.style.visibility = 'visible';
        var gap = 8;
        var left = rect.right - bw;
        var top = rect.bottom + gap;
        if (top + bh > window.innerHeight - 4) top = rect.top - bh - gap;
        if (top < 4) top = 4;
        if (left < 4) left = 4;
        if (left + bw > window.innerWidth - 4) left = window.innerWidth - bw - 4;
        bar.style.left = left + 'px';
        bar.style.top = top + 'px';
    }

    // ── 复制选中文本 ──
    function copySelection() {
        var sel = window.getSelection();
        if (!sel || sel.rangeCount === 0 || sel.isCollapsed) { hideBar(); return; }
        var text = sel.toString();
        if (!text) { hideBar(); return; }
        try {
            if (navigator.clipboard && navigator.clipboard.writeText) {
                navigator.clipboard.writeText(text).then(function(){
                    hideBar();
                }, function(){ fallbackCopy(text); });
            } else {
                fallbackCopy(text);
            }
        } catch(e) {
            fallbackCopy(text);
        }
    }

    function fallbackCopy(text) {
        try {
            var ta = document.createElement('textarea');
            ta.value = text;
            ta.style.cssText = 'position:fixed;left:-9999px;top:0;opacity:0;';
            document.body.appendChild(ta);
            ta.focus();
            ta.select();
            var ok = document.execCommand('copy');
            document.body.removeChild(ta);
        } catch(e) {}
        hideBar();
    }

    // ── 由选区创建标注（有 comment 走批注流程，无 comment 走普通高亮） ──
    function createFromSelection(color, style, comment) {
        var sel = window.getSelection();
        if (!sel || sel.rangeCount === 0 || sel.isCollapsed) { hideBar(); return; }
        var range = sel.getRangeAt(0);
        var info = buildSelectionInfo(range, color, style);
        if (info) {
            info.comment = comment || '';
            if (comment) {
                window.chrome.webview.postMessage('hl-note|' + JSON.stringify(info));
            } else {
                window.chrome.webview.postMessage('hl-create|' + JSON.stringify(info));
            }
        }
        sel.removeAllRanges();
        hideBar();
    }

    function hideBar() {
        var wasVisible = (bar.style.display !== 'none');
        bar.style.display = 'none';
        editMode = null;
        deleteHighlightBtn.style.display = 'none';
        currentHighlightId = null;
        hideNotePanel();
        if (wasVisible) window.chrome.webview.postMessage('hl-bar-hide|');
    }

    // ── 自定义居中确认弹窗（替换原生 confirm，样式与浮窗统一） ──
    var confirmOverlay = null;
    function showConfirmDialog(message, onConfirm) {
        if (confirmOverlay) { document.body.removeChild(confirmOverlay); confirmOverlay = null; }
        var ov = document.createElement('div');
        ov.style.cssText = 'position:fixed;left:0;top:0;right:0;bottom:0;z-index:100000;' +
            'display:flex;align-items:center;justify-content:center;background:rgba(0,0,0,0.4);';
        var dlg = document.createElement('div');
        dlg.style.cssText = 'background:#2B2B2B;border-radius:10px;padding:18px 20px;' +
            'box-shadow:0 8px 32px rgba(0,0,0,0.5);min-width:240px;max-width:320px;' +
            'font:13px/1.5 ""Microsoft YaHei"",sans-serif;color:#E8E8EC;text-align:center;';
        var msg = document.createElement('div');
        msg.textContent = message;
        msg.style.marginBottom = '16px';
        dlg.appendChild(msg);
        var btnRow = document.createElement('div');
        btnRow.style.cssText = 'display:flex;justify-content:center;gap:10px;';
        var cancelBtnEl = document.createElement('span');
        cancelBtnEl.textContent = '取消';
        cancelBtnEl.style.cssText = 'display:inline-flex;align-items:center;justify-content:center;' +
            'min-width:64px;height:30px;padding:0 12px;border-radius:6px;cursor:pointer;' +
            'background:rgba(255,255,255,0.1);color:#E8E8EC;font-weight:600;transition:background .12s ease;';
        cancelBtnEl.addEventListener('mouseenter', function(){ cancelBtnEl.style.background = 'rgba(255,255,255,0.18)'; });
        cancelBtnEl.addEventListener('mouseleave', function(){ cancelBtnEl.style.background = 'rgba(255,255,255,0.1)'; });
        cancelBtnEl.addEventListener('click', function(){
            document.body.removeChild(ov); confirmOverlay = null;
        });
        var okBtnEl = document.createElement('span');
        okBtnEl.textContent = '确认';
        okBtnEl.style.cssText = 'display:inline-flex;align-items:center;justify-content:center;' +
            'min-width:64px;height:30px;padding:0 12px;border-radius:6px;cursor:pointer;' +
            'background:#E53935;color:#FFFFFF;font-weight:600;transition:background .12s ease;';
        okBtnEl.addEventListener('mouseenter', function(){ okBtnEl.style.background = '#EF5350'; });
        okBtnEl.addEventListener('mouseleave', function(){ okBtnEl.style.background = '#E53935'; });
        okBtnEl.addEventListener('click', function(){
            document.body.removeChild(ov); confirmOverlay = null;
            if (onConfirm) onConfirm();
        });
        // 遮罩拦截交互，避免穿透到阅读区
        ov.addEventListener('mousedown', function(ev){ ev.stopPropagation(); });
        ov.addEventListener('wheel', function(ev){ ev.preventDefault(); ev.stopPropagation(); }, { passive: false });
        ov.addEventListener('contextmenu', function(ev){ ev.preventDefault(); });
        btnRow.appendChild(cancelBtnEl);
        btnRow.appendChild(okBtnEl);
        dlg.appendChild(btnRow);
        ov.appendChild(dlg);
        document.body.appendChild(ov);
        confirmOverlay = ov;
    }

    // 点击已有无批注高亮：显示工具栏（带删除按钮），定位到 span 下方
    function showBarForExistingHighlight(span, noteId) {
        currentHighlightId = noteId;
        editMode = null;
        hideNotePanel();
        deleteHighlightBtn.style.display = '';
        bar.style.display = 'flex';
        bar.style.visibility = 'hidden';
        var bw = bar.offsetWidth, bh = bar.offsetHeight;
        bar.style.visibility = 'visible';
        var rect = span.getBoundingClientRect();
        var gap = 8;
        var left = rect.left;
        var top = rect.bottom + gap;
        if (top + bh > window.innerHeight - 4) top = rect.top - bh - gap;
        if (top < 4) top = 4;
        if (left + bw > window.innerWidth - 4) left = window.innerWidth - bw - 4;
        if (left < 4) left = 4;
        bar.style.left = left + 'px';
        bar.style.top = top + 'px';
        setTimeout(function(){ window.chrome.webview.postMessage('hl-bar-show|'); }, 0);
    }

    function showBarForSelection() {
        var card = getCard();
        if (!card) { hideBar(); return; }
        var sel = window.getSelection();
        if (!sel || sel.rangeCount === 0 || sel.isCollapsed) { hideBar(); return; }
        var range = sel.getRangeAt(0);
        if (!card.contains(range.commonAncestorContainer)) { hideBar(); return; }

        var rects = range.getClientRects();
        if (rects.length === 0) { hideBar(); return; }
        var rect = rects[rects.length - 1];
        if (rect.width === 0 && rect.height === 0) { hideBar(); return; }

        // 每次显示浮窗都回到操作行模式（隐藏已有高亮的删除按钮）
        hideNotePanel();
        deleteHighlightBtn.style.display = 'none';
        currentHighlightId = null;
        bar.style.display = 'flex';
        bar.style.visibility = 'hidden';
        var bw = bar.offsetWidth, bh = bar.offsetHeight;
        bar.style.visibility = 'visible';

        var gap = 8;
        var left = rect.right - bw;
        var top = rect.bottom + gap;
        if (top + bh > window.innerHeight - 4) top = rect.top - bh - gap;
        if (top < 4) top = 4;
        if (left < 4) left = 4;
        if (left + bw > window.innerWidth - 4) left = window.innerWidth - bw - 4;
        bar.style.left = left + 'px';
        bar.style.top = top + 'px';
        setTimeout(function(){ window.chrome.webview.postMessage('hl-bar-show|'); }, 0);
    }

    document.addEventListener('mouseup', function(ev) {
        // 点击发生在浮窗内部（如点“笔记”按钮）：不触发重新定位，避免覆盖面板
        if (bar.contains(ev.target)) return;
        var sel = window.getSelection();
        if (!sel || sel.isCollapsed) { hideBar(); return; }
        setTimeout(showBarForSelection, 0);
    });

    document.addEventListener('keyup', function(ev) {
        if (ev.shiftKey || ev.key === 'Shift' ||
            ev.key === 'ArrowLeft' || ev.key === 'ArrowRight' ||
            ev.key === 'ArrowUp' || ev.key === 'ArrowDown') {
            var sel = window.getSelection();
            if (!sel || sel.isCollapsed) { hideBar(); return; }
            setTimeout(showBarForSelection, 0);
        }
    });

    // ── 浮窗显示期间的全局交互锁 ──
    function isOverlayVisible() {
        return bar.style.display !== 'none';
    }

    document.addEventListener('mousedown', function(ev) {
        if (bar.contains(ev.target)) return;
        if (isOverlayVisible()) {
            ev.preventDefault();
            ev.stopPropagation();
            var sel = window.getSelection();
            if (sel) sel.removeAllRanges();
            hideBar();
        }
    }, true);

    document.addEventListener('keydown', function(ev) {
        if (isOverlayVisible() && !bar.contains(ev.target)) {
            ev.preventDefault();
            ev.stopPropagation();
        }
    }, true);

    document.addEventListener('wheel', function(ev) {
        if (isOverlayVisible()) {
            ev.preventDefault();
            ev.stopPropagation();
        }
    }, { capture: true, passive: false });

    document.addEventListener('contextmenu', function(ev) {
        if (isOverlayVisible()) ev.preventDefault();
    }, true);

    document.addEventListener('dblclick', function(ev) {
        if (isOverlayVisible() && !bar.contains(ev.target)) {
            ev.preventDefault();
            ev.stopPropagation();
        }
    }, true);

    document.addEventListener('selectstart', function(ev) {
        if (isOverlayVisible() && !bar.contains(ev.target)) {
            ev.preventDefault();
        }
    }, true);
    document.addEventListener('dragstart', function(ev) {
        if (isOverlayVisible()) ev.preventDefault();
    }, true);

    function getGlobalOffset(node, offset) {
        var card = getCard();
        if (!card) return -1;
        var walker = document.createTreeWalker(card, NodeFilter.SHOW_TEXT);
        var total = 0;
        if (node.nodeType === 3) {
            while (walker.nextNode()) {
                if (walker.currentNode === node) return total + offset;
                total += walker.currentNode.length;
            }
        } else {
            var childNodes = node.childNodes;
            for (var i = 0; i < offset && i < childNodes.length; i++) {
                var subWalker = document.createTreeWalker(childNodes[i], NodeFilter.SHOW_TEXT);
                while (subWalker.nextNode()) total += subWalker.currentNode.length;
            }
            walker = document.createTreeWalker(card, NodeFilter.SHOW_TEXT);
            var foundNode = false;
            while (walker.nextNode()) {
                if (foundNode) break;
                if (walker.currentNode.parentNode === node || node.contains(walker.currentNode)) {
                    foundNode = true;
                    continue;
                }
                total += walker.currentNode.length;
            }
        }
        return total;
    }

    function findPosition(globalOffset) {
        var card = getCard();
        if (!card) return null;
        var walker = document.createTreeWalker(card, NodeFilter.SHOW_TEXT);
        var total = 0;
        while (walker.nextNode()) {
            var tn = walker.currentNode;
            if (total + tn.length >= globalOffset) {
                return { node: tn, offset: globalOffset - total };
            }
            total += tn.length;
        }
        return null;
    }

    function buildSelectionInfo(range, color, style) {
        try {
            var text = range.toString();
            if (!text.trim()) return null;
            var startOff = getGlobalOffset(range.startContainer, range.startOffset);
            var endOff = getGlobalOffset(range.endContainer, range.endOffset);
            if (startOff < 0 || endOff < 0) return null;
            return { startOffset: startOff, endOffset: endOff, text: text, color: color, style: style };
        } catch(e) { return null; }
    }

    function getTextNodesInRange(range) {
        var nodes = [];
        var root = range.commonAncestorContainer;
        var walkerRoot = root.nodeType === 3 ? root.parentNode : root;
        var walker = document.createTreeWalker(walkerRoot, NodeFilter.SHOW_TEXT);
        while (walker.nextNode()) {
            var tn = walker.currentNode;
            var nodeRange = document.createRange();
            nodeRange.selectNodeContents(tn);
            var startCmp = range.compareBoundaryPoints(Range.START_TO_END, nodeRange);
            var endCmp = range.compareBoundaryPoints(Range.END_TO_START, nodeRange);
            if (startCmp >= 0 && endCmp <= 0) nodes.push(tn);
        }
        return nodes;
    }

    window.__epubRenderHighlight = function(n) {
        try {
            var startPos = findPosition(n.startOffset);
            var endPos = findPosition(n.endOffset);
            if (!startPos || !endPos) return false;
            var range = document.createRange();
            range.setStart(startPos.node, startPos.offset);
            range.setEnd(endPos.node, endPos.offset);
            var actual = range.toString().replace(/\s+/g, '');
            var expect = (n.text || '').replace(/\s+/g, '');
            if (actual !== expect) return false;
            var textNodes = getTextNodesInRange(range);
            if (textNodes.length === 0) return false;
            var comment = n.comment || '';
            textNodes.forEach(function(tn) {
                var sOff = (tn === range.startContainer) ? range.startOffset : 0;
                var eOff = (tn === range.endContainer) ? range.endOffset : tn.length;
                if (sOff >= eOff) return;
                var span = document.createElement('span');
                // 带批注的标注追加 hl-has-note 类，叠加红色波浪线标识“这里有笔记”
                span.className = 'hl-note' + (comment ? ' hl-has-note' : '');
                span.setAttribute('data-note-id', n.id);
                span.setAttribute('data-comment', comment);
                if (n.style === 'underline') {
                    span.style.cssText = 'border-bottom:2px solid ' + n.color + ';cursor:pointer;';
                } else if (n.style === 'annotation') {
                    span.style.cssText = 'background:' + n.color + '22;border-left:3px solid ' + n.color +
                        ';border-radius:2px;cursor:pointer;padding-left:2px;';
                } else {
                    span.style.cssText = 'background:' + n.color + ';border-radius:2px;cursor:pointer;';
                }
                span.addEventListener('click', function(ev) {
                    ev.stopPropagation();
                    var currentComment = this.getAttribute('data-comment') || '';
                    // 再次点击同一标注的笔记面板则关闭
                    if (editMode && editMode.id === n.id) {
                        hideBar();
                        return;
                    }
                    // 有批注内容：复用笔记面板进入编辑模式（保留标注，绝不删除）
                    if (currentComment) {
                        showNotePanelForEdit(this, {
                            id: n.id,
                            comment: currentComment,
                            color: n.color,
                            style: n.style
                        });
                    } else {
                        // 无批注的纯高亮：弹出工具栏（含删除按钮），可改色/下划线或直接删除
                        showBarForExistingHighlight(this, n.id);
                    }
                });
                var innerRange = document.createRange();
                innerRange.setStart(tn, sOff);
                innerRange.setEnd(tn, eOff);
                innerRange.surroundContents(span);
            });
            return true;
        } catch(e) { return false; }
    };

    window.__epubRestoreHighlights = function(notesJson) {
        try {
            // 渲染前先清除所有现有标注 span，避免重复叠加（编辑批注后重渲染时使用）
            var existing = document.querySelectorAll('.hl-note');
            existing.forEach(function(span) {
                var parent = span.parentNode;
                while (span.firstChild) parent.insertBefore(span.firstChild, span);
                parent.removeChild(span);
                parent.normalize();
            });
            var notes = JSON.parse(notesJson);
            notes.forEach(function(n) { window.__epubRenderHighlight(n); });
        } catch(e) {}
    };

    window.__epubRemoveHighlight = function(noteId) {
        var spans = document.querySelectorAll('.hl-note[data-note-id=""' + noteId + '""]');
        spans.forEach(function(span) {
            var parent = span.parentNode;
            while (span.firstChild) parent.insertBefore(span.firstChild, span);
            parent.removeChild(span);
            parent.normalize();
        });
    };

    // 更新批注：移除旧 span 后用新 color/style/comment 重新渲染
    window.__epubUpdateAnnotationComment = function(payload) {
        try {
            var id = payload.id;
            var comment = payload.comment || '';
            var color = payload.color || '';
            var style = payload.style || '';
            // 先移除旧 span
            var spans = document.querySelectorAll('.hl-note[data-note-id=""' + id + '""]');
            spans.forEach(function(span) {
                var parent = span.parentNode;
                while (span.firstChild) parent.insertBefore(span.firstChild, span);
                parent.removeChild(span);
                parent.normalize();
            });
            // 用新属性重新渲染（复用 __epubRenderHighlight，需查回原 Note 的 offset/text）
            // 通过遍历 DOM 上的标注无法拿到 offset，因此改为直接就地重建 span：
            // 此处依赖宿主传入完整 note 信息；若仅有 id+comment 则只更新文本属性
            if (color && style) {
                // 重新查找该 note 的渲染数据（从已渲染节点已丢失，故走重渲染需要 offset，
                // 但宿主未传 offset，这里改为：仅当传入 color/style 时就地包裹新 span）
                // 实际重渲染由 __epubRenderHighlight 在 RestoreHighlights 时完成，
                // 这里改为通知宿主重新拉取本章笔记并重渲染
                window.chrome.webview.postMessage('hl-refresh|' + id);
            }
        } catch(e) {}
    };
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

    /// <summary>章节加载后恢复高亮：注入笔记 JSON 并调用渲染函数</summary>
    private async void OnHighlightsRestoreRequested(object? sender, string notesJson)
    {
        if (WebView.CoreWebView2 == null || string.IsNullOrEmpty(notesJson)) return;
        if (!_navigationCompleted)
        {
            await Task.Delay(250);
        }
        try
        {
            // notesJson 是 JSON 数组字符串，直接作为 JS 字面量传入
            var script = $"window.__epubRestoreHighlights && window.__epubRestoreHighlights('{notesJson}');";
            await WebView.CoreWebView2.ExecuteScriptAsync(script);
        }
        catch { }
    }

    /// <summary>创建高亮成功后渲染该高亮</summary>
    private async void OnHighlightCreated(object? sender, string renderJson)
    {
        if (WebView.CoreWebView2 == null || string.IsNullOrEmpty(renderJson)) return;
        try
        {
            // renderJson 是合法 JSON，同时也是合法的 JS 对象字面量，直接嵌入即可
            var script = $"window.__epubRenderHighlight && window.__epubRenderHighlight({renderJson});";
            await WebView.CoreWebView2.ExecuteScriptAsync(script);
        }
        catch { }
    }

    /// <summary>删除高亮后移除 DOM 标记</summary>
    private async void OnHighlightDeleted(object? sender, string noteId)
    {
        if (WebView.CoreWebView2 == null || string.IsNullOrEmpty(noteId)) return;
        try
        {
            var escaped = noteId.Replace("'", "\\'");
            var script = $"window.__epubRemoveHighlight && window.__epubRemoveHighlight('{escaped}');";
            await WebView.CoreWebView2.ExecuteScriptAsync(script);
        }
        catch { }
    }

    /// <summary>批注内容更新后同步 DOM 的 data-comment 与气泡文本</summary>
    private async void OnAnnotationUpdated(object? sender, string payloadJson)
    {
        if (WebView.CoreWebView2 == null || string.IsNullOrEmpty(payloadJson)) return;
        try
        {
            // payloadJson 是合法 JSON {id, comment, color, style}，作为 JS 对象字面量嵌入
            var script = $"window.__epubUpdateAnnotationComment && window.__epubUpdateAnnotationComment({payloadJson});";
            await WebView.CoreWebView2.ExecuteScriptAsync(script);
        }
        catch { }
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

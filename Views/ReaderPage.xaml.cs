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
                    case "hl-delete":
                        _viewModel.DeleteHighlight(payload);
                        return;
                    case "hl-bar-show":
                        _viewModel.IsToolbarEnabled = false;
                        return;
                    case "hl-bar-hide":
                        _viewModel.IsToolbarEnabled = true;
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

    // ── 构建浮动工具栏（两行布局：第一行操作按钮，第二行颜色与下划线） ──
    var bar = document.createElement('div');
    bar.id = 'hl-toolbar';
    bar.style.cssText = 'position:fixed;z-index:99999;display:none;flex-direction:column;gap:4px;' +
        'background:#2B2B2B;border-radius:10px;padding:6px;' +
        'box-shadow:0 6px 20px rgba(0,0,0,0.45);user-select:none;' +
        'font:12px/1 ""Microsoft YaHei"",sans-serif;';

    // 第一行容器：笔记、复制等操作
    var row1 = document.createElement('div');
    row1.style.cssText = 'display:flex;flex-direction:row;align-items:center;gap:2px;';

    // 第二行容器：高亮色、下划线
    var row2 = document.createElement('div');
    row2.style.cssText = 'display:flex;flex-direction:row;align-items:center;gap:2px;';

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
            createFromSelection(color, 'highlight');
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
            'min-width:30px;height:26px;padding:0 8px;border-radius:6px;color:#E8E8EC;' +
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

    // ── 第一行：笔记、复制 ──
    var noteBtn = makeAction('笔记', '笔记', function(){
        // 笔记功能预留：当前仅占位，后续实现
    });
    noteBtn.style.fontSize = '12px';
    row1.appendChild(noteBtn);
    row1.appendChild(makeSep());
    var copyBtn = makeAction('复制', '复制选中文本', function(){
        copySelection();
    });
    copyBtn.style.fontSize = '12px';
    row1.appendChild(copyBtn);

    // ── 第二行：高亮色、下划线 ──
    HIGHLIGHT_COLORS.forEach(function(c){ row2.appendChild(makeDot(c)); });
    row2.appendChild(makeSep());
    var underlineBtn = makeAction('U', '下划线', function(){
        createFromSelection(UNDERLINE_COLOR, 'underline');
    });
    underlineBtn.style.textDecoration = 'underline';
    underlineBtn.style.textDecorationThickness = '2px';
    underlineBtn.style.textUnderlineOffset = '2px';
    row2.appendChild(underlineBtn);

    bar.appendChild(row1);
    bar.appendChild(row2);
    document.body.appendChild(bar);

    // ── 复制选中文本 ──
    function copySelection() {
        var sel = window.getSelection();
        if (!sel || sel.rangeCount === 0 || sel.isCollapsed) { hideBar(); return; }
        var text = sel.toString();
        if (!text) { hideBar(); return; }
        try {
            // 优先用 Clipboard API
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

    // 兜底复制：用临时 textarea + document.execCommand
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

    // ── 由选区创建标注 ──
    function createFromSelection(color, style) {
        var sel = window.getSelection();
        if (!sel || sel.rangeCount === 0 || sel.isCollapsed) { hideBar(); return; }
        var range = sel.getRangeAt(0);
        var info = buildSelectionInfo(range, color, style);
        if (info) {
            window.chrome.webview.postMessage('hl-create|' + JSON.stringify(info));
        }
        sel.removeAllRanges();
        hideBar();
    }

    function hideBar() { bar.style.display = 'none'; window.chrome.webview.postMessage('hl-bar-hide|'); }

    function showBarForSelection() {
        var card = getCard();
        if (!card) { hideBar(); return; }
        var sel = window.getSelection();
        if (!sel || sel.rangeCount === 0 || sel.isCollapsed) { hideBar(); return; }
        var range = sel.getRangeAt(0);
        if (!card.contains(range.commonAncestorContainer)) { hideBar(); return; }

        // 取选区每一行的客户端矩形，最后一个即为选中结束所在行，
        // 用它定位浮窗到“最后一个字的右下角”，避免跨多行时位置偏远。
        var rects = range.getClientRects();
        if (rects.length === 0) { hideBar(); return; }
        var rect = rects[rects.length - 1];
        if (rect.width === 0 && rect.height === 0) { hideBar(); return; }

        // 先以不可见方式渲染以测量尺寸
        bar.style.display = 'flex';
        bar.style.visibility = 'hidden';
        var bw = bar.offsetWidth, bh = bar.offsetHeight;
        bar.style.visibility = 'visible';

        // 定位到选中结束的右下角：右边缘对齐最后一行右端，紧贴其下方
        var gap = 8;
        var left = rect.right - bw;
        var top = rect.bottom + gap;
        // 越界修正：下方放不下则上移到最后一行上方
        if (top + bh > window.innerHeight - 4) top = rect.top - bh - gap;
        if (top < 4) top = 4;
        if (left < 4) left = 4;
        if (left + bw > window.innerWidth - 4) left = window.innerWidth - bw - 4;
        bar.style.left = left + 'px';
        bar.style.top = top + 'px';
        // 通知宿主禁用 WPF 工具栏/面板交互
        window.chrome.webview.postMessage('hl-bar-show|');
    }

    // 仅在选区动作结束时（鼠标抬起 / 按键抬起）弹出浮窗，
    // 避免选择过程中浮窗跟随选区实时移动。
    document.addEventListener('mouseup', function() {
        var sel = window.getSelection();
        if (!sel || sel.isCollapsed) { hideBar(); return; }
        // 延迟一帧，确保 getBoundingClientRect 反映最终选区位置
        setTimeout(showBarForSelection, 0);
    });

    // 支持键盘选择（Shift + 方向键）结束后也能弹出
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
    // 仅允许两种操作：点击浮窗内按钮、点击浮窗外取消浮窗；
    // 其余一切交互（拖动选区、键盘改选区、滚动、右键菜单、双击选词等）一律屏蔽。
    function isBarVisible() { return bar.style.display !== 'none'; }

    document.addEventListener('mousedown', function(ev) {
        // 点击浮窗内部：放行（按钮可正常响应）
        if (bar.contains(ev.target)) return;
        // 浮窗显示时点击外部：取消浮窗并清除选区，同时阻止开始任何新选区拖动
        if (isBarVisible()) {
            ev.preventDefault();
            ev.stopPropagation();
            var sel = window.getSelection();
            if (sel) sel.removeAllRanges();
            hideBar();
        }
    }, true);

    // 屏蔽键盘：防止方向键/Shift 修改选区、Space/PageDown 滚动等
    document.addEventListener('keydown', function(ev) {
        if (isBarVisible() && !bar.contains(ev.target)) {
            ev.preventDefault();
            ev.stopPropagation();
        }
    }, true);

    // 屏蔽滚轮：浮窗显示期间禁止滚动页面
    document.addEventListener('wheel', function(ev) {
        if (isBarVisible()) {
            ev.preventDefault();
            ev.stopPropagation();
        }
    }, { capture: true, passive: false });

    // 屏蔽右键菜单
    document.addEventListener('contextmenu', function(ev) {
        if (isBarVisible()) ev.preventDefault();
    }, true);

    // 屏蔽双击选词
    document.addEventListener('dblclick', function(ev) {
        if (isBarVisible() && !bar.contains(ev.target)) {
            ev.preventDefault();
            ev.stopPropagation();
        }
    }, true);

    // 屏蔽新的选区拖动与文本拖拽
    document.addEventListener('selectstart', function(ev) {
        if (isBarVisible() && !bar.contains(ev.target)) {
            ev.preventDefault();
        }
    }, true);
    document.addEventListener('dragstart', function(ev) {
        if (isBarVisible()) ev.preventDefault();
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
            textNodes.forEach(function(tn) {
                var sOff = (tn === range.startContainer) ? range.startOffset : 0;
                var eOff = (tn === range.endContainer) ? range.endOffset : tn.length;
                if (sOff >= eOff) return;
                var span = document.createElement('span');
                span.className = 'hl-note';
                span.setAttribute('data-note-id', n.id);
                if (n.style === 'underline') {
                    span.style.cssText = 'border-bottom:2px solid ' + n.color + ';cursor:pointer;';
                } else {
                    span.style.cssText = 'background:' + n.color + ';border-radius:2px;cursor:pointer;';
                }
                span.addEventListener('click', function(ev) {
                    ev.stopPropagation();
                    if (confirm('Delete this highlight?')) {
                        window.chrome.webview.postMessage('hl-delete|' + n.id);
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

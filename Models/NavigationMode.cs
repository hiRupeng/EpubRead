namespace EpubRead.Models;

/// <summary>
/// 阅读导航模式
/// </summary>
public enum NavigationMode
{
    /// <summary>小节模式：按 TOC 全部条目顺序导航（包含卷/章/节所有层级）</summary>
    BySection,

    /// <summary>章节文件模式：只跳转到不同的 HTML 物理文件开头</summary>
    BySpineFile
}

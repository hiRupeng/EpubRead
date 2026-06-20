namespace EpubRead.Models;

/// <summary>
/// EPUB 章节结构，包含标题、相对路径、HTML 内容和阅读顺序
/// 支持层级树结构（Level/Children）用于目录面板展示和双模式导航
/// </summary>
public class Chapter
{
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// 相对于 OEBPS 目录的路径（不含 #fragment）
    /// </summary>
    public string Href { get; set; } = string.Empty;

    /// <summary>
    /// 锚点标识符（#fragment 部分），用于导航到章节内具体位置
    /// </summary>
    public string? Anchor { get; set; }

    /// <summary>
    /// 章节 HTML 内容（按需加载）
    /// </summary>
    public string? Content { get; set; }

    /// <summary>
    /// 在展平阅读列表中的顺序索引
    /// </summary>
    public int Order { get; set; }

    /// <summary>
    /// 目录层级深度（0=顶层卷/部, 1=章, 2=节...）
    /// </summary>
    public int Level { get; set; }

    /// <summary>
    /// 是否为一个新 HTML 文件的起始条目
    /// （同一 href 首次出现时为 true，后续锚点章节为 false）
    /// </summary>
    public bool IsSpineRoot { get; set; }

    /// <summary>
    /// 子章节列表（用于目录树展示）
    /// </summary>
    public List<Chapter> Children { get; set; } = [];
}

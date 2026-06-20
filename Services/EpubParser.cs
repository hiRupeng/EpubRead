using System.IO;
using System.IO.Compression;
using System.Xml.Linq;
using EpubRead.Models;

namespace EpubRead.Services;

/// <summary>
/// EPUB 文件解析服务 —— 负责解析 ZIP 封装的 EPUB，提取元数据、封面和章节结构
/// </summary>
public class EpubParser
{
    /// <summary>
    /// 解析 EPUB 文件，返回 EpubBook 对象
    /// </summary>
    public EpubBook Parse(string epubFilePath, Stream? coverOutputStream = null)
    {
        if (!File.Exists(epubFilePath))
            throw new FileNotFoundException("EPUB 文件不存在", epubFilePath);

        using var archive = ZipFile.OpenRead(epubFilePath);

        // 1. 解析 container.xml → 获取 OPF 路径
        var opfPath = GetOpfPath(archive);

        // 2. 切割基础路径（OPF 所在目录）
        var basePath = Path.GetDirectoryName(opfPath)?.Replace('\\', '/') ?? string.Empty;
        if (basePath.Length > 0 && !basePath.EndsWith('/'))
            basePath += '/';

        // 3. 解析 OPF → 元数据、manifest、spine
        var (title, author, coverId, manifest, spine) = ParseOpf(archive, opfPath);

        // 4. 解析目录（NCX / NAV），保留层级结构
        var chapters = ParseNav(archive, basePath, manifest, spine);

        // 5. 展平为阅读顺序列表
        var flatChapters = FlattenChapters(chapters);

        // 6. 提取封面图片
        byte[]? coverImage = null;
        if (coverId != null && manifest.TryGetValue(coverId, out var coverHref))
        {
            coverImage = ReadEntryBytes(archive, basePath + coverHref);
        }

        // 如果提供了输出流，写入封面（用于缓存到本地）
        if (coverOutputStream != null && coverImage != null)
        {
            coverOutputStream.Write(coverImage, 0, coverImage.Length);
        }

        return new EpubBook
        {
            Title = title,
            Author = author,
            CoverImage = coverImage,
            Chapters = chapters,
            FlatChapters = flatChapters,
            BasePath = basePath
        };
    }

    /// <summary>
    /// 按需加载指定章节的 HTML 内容
    /// </summary>
    public string LoadChapterContent(string epubFilePath, string basePath, string href)
    {
        using var archive = ZipFile.OpenRead(epubFilePath);
        var fullPath = NormalizeZipPath(basePath + href);
        var entry = archive.GetEntry(fullPath);

        if (entry == null)
        {
            // 尝试移除 basePath 前缀（兼容 href 已包含 basePath 的情况）
            if (!string.IsNullOrEmpty(basePath) && fullPath.StartsWith(basePath, StringComparison.OrdinalIgnoreCase))
            {
                var altPath = fullPath[basePath.Length..];
                entry = archive.GetEntry(altPath);
            }

            // 尝试 ZIP 根路径直接查找
            if (entry == null)
            {
                var hrefClean = href.Replace("\\", "/").TrimStart('/');
                entry = archive.GetEntry(hrefClean);
            }

            if (entry == null)
                return "<p>内容加载失败</p>";
        }

        using var stream = entry.Open();
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// 从 EPUB ZIP 中读取任意资源（图片、CSS、字体等），返回字节数据和 MIME 类型
    /// </summary>
    public (byte[]? data, string mediaType) LoadResource(string epubFilePath, string resourcePath)
    {
        if (!File.Exists(epubFilePath) || string.IsNullOrEmpty(resourcePath))
            return (null, "");

        using var archive = ZipFile.OpenRead(epubFilePath);
        var fullPath = NormalizeZipPath(resourcePath);
        var entry = archive.GetEntry(fullPath);

        // 兜底：尝试原始路径（去除前导斜杠）
        if (entry == null)
        {
            var raw = resourcePath.Replace("\\", "/").TrimStart('/');
            entry = archive.GetEntry(raw);
        }

        if (entry == null)
            return (null, "");

        using var stream = entry.Open();
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return (ms.ToArray(), GetMediaType(fullPath));
    }

    /// <summary>根据文件扩展名推断 MIME 类型</summary>
    private static string GetMediaType(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".bmp" => "image/bmp",
            ".webp" => "image/webp",
            ".svg" => "image/svg+xml",
            ".css" => "text/css",
            ".js" => "application/javascript",
            ".ttf" => "font/ttf",
            ".otf" => "font/otf",
            ".woff" => "font/woff",
            ".woff2" => "font/woff2",
            ".html" or ".htm" or ".xhtml" => "text/html",
            ".xml" => "application/xml",
            _ => "application/octet-stream"
        };
    }

    /// <summary>
    /// 规范化 ZIP 内部路径：去除 ./、解析 ../、统一使用正斜杠
    /// </summary>
    private static string NormalizeZipPath(string path)
    {
        if (string.IsNullOrEmpty(path)) return path;

        // 统一分隔符
        path = path.Replace('\\', '/');

        // 移除开头的 './'
        while (path.StartsWith("./"))
            path = path[2..];

        // 移除开头的 '/'
        path = path.TrimStart('/');

        // 处理 ../ 路径
        var parts = path.Split('/').ToList();
        var result = new List<string>();
        foreach (var part in parts)
        {
            if (part == ".." && result.Count > 0)
                result.RemoveAt(result.Count - 1);
            else if (part != "." && part != "")
                result.Add(part);
        }

        return string.Join("/", result);
    }

    // ──────────────── 公开辅助方法 ────────────────

    /// <summary>
    /// 规范化相对路径：统一使用正斜杠、解析 ./ 与 ../、去除前导斜杠。
    /// 用于将 EPUB 内容中相对于当前文件的链接解析为相对于 OPF 基础路径的链接。
    /// </summary>
    public static string NormalizePath(string path)
        => NormalizeZipPath(path);

    /// <summary>
    /// 从层级章节树中展平出可导航的扁平列表（只包含含有 Href 的章节）
    /// </summary>
    public static List<Chapter> FlattenChapters(List<Chapter> tree)
    {
        var result = new List<Chapter>();
        int order = 0;
        FlattenRecursive(tree, result, ref order);
        return result;
    }

    private static void FlattenRecursive(List<Chapter> nodes, List<Chapter> result, ref int order)
    {
        foreach (var ch in nodes)
        {
            // 只有含有 Href 的章节才能导航（纯分组标题如"第一卷"无 href 则跳过）
            if (!string.IsNullOrEmpty(ch.Href))
            {
                ch.Order = order++;
                result.Add(ch);
            }
            FlattenRecursive(ch.Children, result, ref order);
        }
    }

    /// <summary>
    /// 根据 href（包含文件名和可选锚点）在扁平章节列表中查找匹配章节
    /// </summary>
    public static Chapter? ResolveHref(string href, string basePath, List<Chapter> flatChapters)
    {
        if (string.IsNullOrEmpty(href)) return null;

        string searchHref;
        string? searchAnchor = null;

        // 分离锚点
        var hashIdx = href.IndexOf('#');
        if (hashIdx >= 0)
        {
            searchAnchor = href[(hashIdx + 1)..];
            searchHref = href[..hashIdx];
        }
        else
        {
            searchHref = href;
        }

        // 去掉 basePath 前缀
        if (searchHref.StartsWith(basePath, StringComparison.OrdinalIgnoreCase))
            searchHref = searchHref[basePath.Length..];

        // 如果搜索目标是 #anchor-only（当前文件内锚点），返回 null 表示只在当前文件内滚动
        if (string.IsNullOrEmpty(searchHref))
            return null;

        // 精确匹配 href + anchor
        if (searchAnchor != null)
        {
            var exact = flatChapters.FirstOrDefault(c =>
                string.Equals(c.Href, searchHref, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(c.Anchor, searchAnchor, StringComparison.OrdinalIgnoreCase));
            if (exact != null) return exact;
        }

        // 匹配 href（有锚点时找第一个同文件的条目，无锚点时精确匹配）
        var match = flatChapters.FirstOrDefault(c =>
            string.Equals(c.Href, searchHref, StringComparison.OrdinalIgnoreCase));
        return match;
    }

    // ─── 内部解析步骤 ───

    private static string GetOpfPath(ZipArchive archive)
    {
        var containerEntry = archive.GetEntry("META-INF/container.xml")
            ?? throw new InvalidDataException("无效的 EPUB：缺少 META-INF/container.xml");

        using var stream = containerEntry.Open();
        var doc = XDocument.Load(stream);
        XNamespace ns = "urn:oasis:names:tc:opendocument:xmlns:container";
        var rootfile = doc.Descendants(ns + "rootfile").FirstOrDefault()
            ?? throw new InvalidDataException("container.xml 中未找到 rootfile 元素");

        return rootfile.Attribute("full-path")?.Value
            ?? throw new InvalidDataException("rootfile 缺少 full-path 属性");
    }

    private static (string title, string author, string? coverId,
        Dictionary<string, string> manifest, List<string> spine)
        ParseOpf(ZipArchive archive, string opfPath)
    {
        var entry = archive.GetEntry(opfPath)
            ?? throw new InvalidDataException($"找不到 OPF 文件：{opfPath}");

        using var stream = entry.Open();
        var doc = XDocument.Load(stream);
        XNamespace dc = "http://purl.org/dc/elements/1.1/";
        XNamespace opf = "http://www.idpf.org/2007/opf";

        var root = doc.Root!;

        // 元数据
        var title = root.Descendants(dc + "title").FirstOrDefault()?.Value.Trim() ?? "未知书名";
        var author = root.Descendants(dc + "creator").FirstOrDefault()?.Value.Trim() ?? "未知作者";

        // Manifest：id → href
        var manifest = root.Element(opf + "manifest")!;
        var items = new Dictionary<string, string>();
        string? coverId = null;

        foreach (var item in manifest.Elements(opf + "item"))
        {
            var id = item.Attribute("id")?.Value;
            var href = item.Attribute("href")?.Value;
            var properties = item.Attribute("properties")?.Value;

            if (id != null && href != null)
            {
                items[id] = href;
                if (properties == "cover-image")
                    coverId = id;
            }
        }

        // 如果没通过 properties 找到封面，尝试通过 meta 标签
        if (coverId == null)
        {
            var metaCover = root.Descendants(opf + "meta")
                .FirstOrDefault(m => m.Attribute("name")?.Value == "cover");
            if (metaCover != null)
                coverId = metaCover.Attribute("content")?.Value;
        }

        // Spine：阅读顺序
        var spineElement = root.Element(opf + "spine")!;
        var spine = spineElement.Elements(opf + "itemref")
            .Select(itemref => itemref.Attribute("idref")?.Value)
            .Where(id => id != null)
            .Select(id => id!)
            .ToList();

        return (title, author, coverId, items, spine);
    }

    // ─── 目录解析（保留层级结构） ───

    private static List<Chapter> ParseNav(ZipArchive archive, string basePath,
        Dictionary<string, string> manifest, List<string> spine)
    {
        // 构建 spine href 集合（用于标记 spine 边界）
        var spineHrefs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in spine)
        {
            if (manifest.TryGetValue(id, out var href))
            {
                var hashIdx = href.IndexOf('#');
                if (hashIdx >= 0) href = href[..hashIdx];
                spineHrefs.Add(href);
            }
        }

        // 跟踪已出现的 href，标记 IsSpineRoot
        var seenHrefs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        List<Chapter> chapters;

        // 1. 尝试解析 NCX 目录（保留层级）
        var ncxHref = manifest.Values
            .FirstOrDefault(v => v.EndsWith(".ncx", StringComparison.OrdinalIgnoreCase));
        if (ncxHref != null)
        {
            chapters = ParseNcx(archive, basePath + ncxHref, basePath, spineHrefs, seenHrefs);
        }
        else
        {
            chapters = [];
        }

        // 2. 如果 NCX 无结果，尝试解析 EPUB 3 HTML NAV 目录
        if (chapters.Count == 0)
        {
            chapters = ParseNavHtml(archive, basePath, manifest, spineHrefs, seenHrefs);
        }

        // 3. 如果都无结果，用 spine 顺序作为 fallback
        if (chapters.Count == 0)
        {
            int order = 0;
            for (int i = 0; i < spine.Count; i++)
            {
                var id = spine[i];
                if (manifest.TryGetValue(id, out var href))
                {
                    var ch = new Chapter
                    {
                        Title = $"第 {i + 1} 章",
                        Href = href,
                        Order = order++,
                        Level = 0,
                        IsSpineRoot = true
                    };
                    chapters.Add(ch);
                    seenHrefs.Add(href);
                }
            }
        }

        return chapters;
    }

    /// <summary>
    /// 递归解析 NCX 目录，保留层级结构
    /// </summary>
    private static List<Chapter> ParseNcx(ZipArchive archive, string ncxPath, string basePath,
        HashSet<string> spineHrefs, HashSet<string> seenHrefs)
    {
        var entry = archive.GetEntry(ncxPath);
        if (entry == null) return [];

        using var stream = entry.Open();
        var doc = XDocument.Load(stream);
        XNamespace ncx = "http://www.daisy.org/z3986/2005/ncx/";

        var navMap = doc.Root?.Element(ncx + "navMap");
        if (navMap == null) return [];

        // 计算 NCX 所在目录（用于相对路径解析）
        var ncxDir = Path.GetDirectoryName(ncxPath)?.Replace('\\', '/');
        if (!string.IsNullOrEmpty(ncxDir))
            ncxDir += '/';
        else
            ncxDir = string.Empty;

        int flatOrder = 0;
        return ParseNcxNodes(navMap.Elements(ncx + "navPoint"), ncx, basePath, ncxDir, 0,
            spineHrefs, seenHrefs, ref flatOrder);
    }

    private static List<Chapter> ParseNcxNodes(IEnumerable<XElement> navPoints, XNamespace ncx,
        string basePath, string ncxDir, int level, HashSet<string> spineHrefs, HashSet<string> seenHrefs,
        ref int flatOrder)
    {
        var chapters = new List<Chapter>();

        foreach (var navPoint in navPoints)
        {
            var label = navPoint.Element(ncx + "navLabel")?.Element(ncx + "text")?.Value?.Trim();
            var src = navPoint.Element(ncx + "content")?.Attribute("src")?.Value;

            string? href = null;
            string? anchor = null;

            if (!string.IsNullOrWhiteSpace(src))
            {
                // 拼接 NCX 目录，使 src 成为相对于 OPF 目录的路径
                src = ncxDir + src;
                (href, anchor) = ParseSrc(src, basePath);
            }

            // 标记 IsSpineRoot：首次出现的 href 即为新文件起点
            bool isSpineRoot = false;
            if (!string.IsNullOrEmpty(href))
            {
                isSpineRoot = seenHrefs.Add(href);
            }

            var ch = new Chapter
            {
                Title = label ?? string.Empty,
                Href = href ?? string.Empty,
                Anchor = anchor,
                Order = flatOrder,
                Level = level,
                IsSpineRoot = isSpineRoot
            };

            // 递归解析子 navPoint
            var childNavPoints = navPoint.Elements(ncx + "navPoint");
            if (childNavPoints.Any())
            {
                ch.Children = ParseNcxNodes(childNavPoints, ncx, basePath, ncxDir, level + 1,
                    spineHrefs, seenHrefs, ref flatOrder);
            }

            chapters.Add(ch);
        }

        return chapters;
    }

    /// <summary>
    /// 解析 EPUB 3 HTML NAV 目录，保留嵌套层级
    /// </summary>
    private static List<Chapter> ParseNavHtml(ZipArchive archive, string basePath,
        Dictionary<string, string> manifest, HashSet<string> spineHrefs,
        HashSet<string> seenHrefs)
    {
        // 查找 nav.xhtml 文件
        var navHref = manifest.Values
            .FirstOrDefault(v => v.EndsWith("nav.xhtml", StringComparison.OrdinalIgnoreCase)
                              || v.EndsWith("nav.html", StringComparison.OrdinalIgnoreCase));
        if (navHref == null) return [];

        var entry = archive.GetEntry(basePath + navHref);
        if (entry == null) return [];

        try
        {
            using var stream = entry.Open();
            using var reader = new StreamReader(stream);
            var html = reader.ReadToEnd();

            var doc = XDocument.Parse(html);
            XNamespace xhtml = "http://www.w3.org/1999/xhtml";

            // 查找 epub:type="toc" 的 nav 元素
            var navElement = doc.Descendants(xhtml + "nav")
                .FirstOrDefault(n => n.Attribute("epub:type")?.Value == "toc"
                                  || n.Attribute("type")?.Value == "toc");

            if (navElement == null) return [];

            // 计算 nav 所在目录（用于调整相对路径）
            var navDir = Path.GetDirectoryName(navHref)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(navDir))
                navDir += '/';
            else
                navDir = string.Empty;

            // 找到 nav 下的第一个 ol
            var topOl = navElement.Element(xhtml + "ol");
            if (topOl == null) return [];

            int flatOrder = 0;
            return ParseNavOl(topOl, xhtml, basePath, navDir, 0, spineHrefs, seenHrefs, ref flatOrder);
        }
        catch
        {
            // HTML 解析失败，忽略
            return [];
        }
    }

    private static List<Chapter> ParseNavOl(XElement ol, XNamespace xhtml, string basePath,
        string navDir, int level, HashSet<string> spineHrefs, HashSet<string> seenHrefs,
        ref int flatOrder)
    {
        var chapters = new List<Chapter>();

        foreach (var li in ol.Elements(xhtml + "li"))
        {
            var link = li.Element(xhtml + "a");
            if (link == null) continue;

            var label = link.Value?.Trim();
            var src = link.Attribute("href")?.Value;

            if (string.IsNullOrWhiteSpace(label)) continue;

            // 解析 href
            string? href = null;
            string? anchor = null;

            if (!string.IsNullOrWhiteSpace(src))
            {
                // 外部链接跳过
                if (src.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    continue;

                // 调整相对 nav.xhtml 的路径
                if (!src.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    src = navDir + src;

                var (parsedHref, parsedAnchor) = ParseSrc(src, basePath);
                href = parsedHref;
                anchor = parsedAnchor;
            }

            bool isSpineRoot = false;
            if (!string.IsNullOrEmpty(href))
            {
                isSpineRoot = seenHrefs.Add(href);
            }

            var ch = new Chapter
            {
                Title = label,
                Href = href ?? string.Empty,
                Anchor = anchor,
                Order = flatOrder,
                Level = level,
                IsSpineRoot = isSpineRoot
            };

            // 检查是否有嵌套的 ol（子章节）
            var nestedOl = li.Element(xhtml + "ol");
            if (nestedOl != null)
            {
                ch.Children = ParseNavOl(nestedOl, xhtml, basePath, navDir, level + 1,
                    spineHrefs, seenHrefs, ref flatOrder);
            }

            chapters.Add(ch);
        }

        return chapters;
    }

    // ─── 辅助方法 ───

    /// <summary>
    /// 解析 src 属性，分离 href 和 anchor，去掉 basePath 前缀，规范化路径
    /// </summary>
    private static (string? href, string? anchor) ParseSrc(string? src, string basePath)
    {
        if (string.IsNullOrWhiteSpace(src))
            return (null, null);

        var result = src.Replace('\\', '/');

        // 分离 #fragment 锚点（在路径处理之前进行，避免 # 被路径篡改）
        string? anchor = null;
        var hashIndex = result.IndexOf('#');
        if (hashIndex >= 0)
        {
            anchor = result[(hashIndex + 1)..];
            result = result[..hashIndex];
        }

        // 去掉开头的 ./
        while (result.StartsWith("./"))
            result = result[2..];

        // 去掉开头的 /
        result = result.TrimStart('/');

        // 去掉 basePath 前缀
        if (result.StartsWith(basePath, StringComparison.OrdinalIgnoreCase))
            result = result[basePath.Length..];

        return (result, anchor);
    }

    private static byte[]? ReadEntryBytes(ZipArchive archive, string entryPath)
    {
        var entry = archive.GetEntry(entryPath);
        if (entry == null) return null;
        using var stream = entry.Open();
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }
}

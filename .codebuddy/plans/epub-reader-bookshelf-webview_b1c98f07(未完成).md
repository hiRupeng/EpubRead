---
name: epub-reader-bookshelf-webview
overview: 为 EpubRead WPF 项目实现书架界面、EPUB 文件解析和 WebView2 内容渲染三大核心功能。
design:
  styleKeywords:
    - 现代简约
    - 深色主题
    - 沉浸式阅读
    - 圆角卡片
    - 柔和阴影
  fontSystem:
    fontFamily: Microsoft YaHei
    heading:
      size: 24px
      weight: 600
    subheading:
      size: 16px
      weight: 500
    body:
      size: 14px
      weight: 400
  colorSystem:
    primary:
      - "#4A90D9"
      - "#357ABD"
      - "#2C6AA0"
    background:
      - "#1E1E2E"
      - "#2D2D3F"
      - "#F5F0E8"
    text:
      - "#E8E8EC"
      - "#A0A0B0"
      - "#333333"
    functional:
      - "#4CAF50"
      - "#FF5252"
      - "#FFC107"
todos:
  - id: add-nuget-packages
    content: 修改 EpubRead.csproj，添加 Microsoft.Web.WebView2 和 CommunityToolkit.Mvvm NuGet 包引用
    status: pending
  - id: create-models
    content: 创建 Models 目录及三个模型类：Book.cs（书架书籍）、EpubBook.cs（EPUB 解析结果）、Chapter.cs（章节）
    status: pending
  - id: create-services
    content: 创建 Services 目录及 EpubParser.cs（EPUB 解析服务）和 BookshelfService.cs（JSON 书架持久化服务）
    status: pending
    dependencies:
      - create-models
  - id: create-converters
    content: 创建 Converters/CoverPathConverter.cs，实现封面路径到 BitmapImage 的值转换，处理缺失封面时返回默认占位图
    status: pending
    dependencies:
      - create-models
  - id: create-bookshelf-view
    content: 创建 BookshelfPage.xaml/cs 书架视图和 BookshelfViewModel.cs，实现书籍网格展示、导入按钮、删除功能和封面显示
    status: pending
    dependencies:
      - create-services
      - create-converters
  - id: create-reader-view
    content: 创建 ReaderPage.xaml/cs 阅读视图和 ReaderViewModel.cs，实现 WebView2 内容渲染、目录侧边栏、上下翻页和返回书架
    status: pending
    dependencies:
      - create-services
  - id: update-mainwindow-navigation
    content: 修改 MainWindow.xaml/cs，将空 Grid 替换为 Frame 导航容器，设置默认加载 BookshelfPage，实现页面间导航
    status: pending
    dependencies:
      - create-bookshelf-view
      - create-reader-view
  - id: update-app-startup
    content: 修改 App.xaml 全局样式资源和 App.xaml.cs 启动逻辑，初始化应用数据目录并配置窗口深色主题
    status: pending
    dependencies:
      - update-mainwindow-navigation
---

## 产品概述

一个基于 WPF 的 EPUB 电子书阅读器，提供书架管理界面与阅读器界面。用户可从本地导入 EPUB 文件，书架以封面缩略图形式展示已导入的书籍；点击书籍进入阅读视图，通过 WebView2 渲染 EPUB 内容，支持章节目录导航与上下翻页。

## 核心功能

- **书架展示**：以网格布局展示已导入的 EPUB 书籍，每本书显示封面缩略图、书名、作者；无封面的书籍显示默认占位图
- **导入书籍**：通过"导入"按钮打开本地文件对话框，选择 .epub 文件后自动解析元数据与封面，加入书架并持久化
- **删除书籍**：支持从书架中移除书籍（不删除原始文件），同步更新持久化数据
- **EPUB 阅读**：点击书架上的书籍进入阅读视图，WebView2 渲染当前章节的 XHTML/HTML 内容，顶部显示书名
- **章节导航**：左侧或弹出式章节目录，点击章节跳转；支持上一章/下一章按钮翻页
- **返回书架**：阅读视图中点击返回按钮回到书架界面

## 技术选型

| 层级 | 技术选择 | 说明 |
| --- | --- | --- |
| **UI 框架** | WPF (.NET 10.0) | 项目现有框架，使用 XAML + C# |
| **Web 渲染** | Microsoft.Web.WebView2 | 基于 Edge Chromium，支持现代 HTML5/CSS3，完美渲染 EPUB 内容 |
| **架构模式** | MVVM | 视图-视图模型-模型分离，利用 WPF 数据绑定和命令 |
| **EPUB 解析** | System.IO.Compression + System.Xml.Linq | EPUB 本质是 ZIP 压缩包，包含标准化 XML 文件，手动解析无需第三方库 |
| **数据持久化** | System.Text.Json | 书架数据序列化为本地 bookshelf.json 文件 |
| **MVVM 基础设施** | CommunityToolkit.Mvvm | 轻量级 MVVM 工具包，提供 ObservableObject、RelayCommand 等，减少样板代码 |


## 实现方案

### EPUB 解析策略

EPUB 文件是 ZIP 压缩包，通过以下步骤手动解析：

1. 解压读取 `META-INF/container.xml`，获取 `.opf` 文件路径
2. 解析 OPF 文件，提取 `<metadata>`（书名、作者、封面ID）、`<manifest>`（资源清单）、`<spine>`（阅读顺序）
3. 根据 cover ID 在 manifest 中定位封面图片，解压到临时目录
4. 解析 NCX/NAV 文件获取章节目录结构
5. 按 spine 顺序加载各章节 XHTML 内容，注入基础样式后交由 WebView2 渲染

### 书架数据持久化

- 文件路径：`%AppData%/EpubRead/bookshelf.json`
- 存储内容：书籍列表（文件路径、书名、作者、封面路径、导入时间）
- 封面图片缓存到 `%AppData%/EpubRead/covers/` 目录
- 应用启动时自动加载，添加/删除书籍时自动保存

### 阅读器内容渲染

- 从 EPUB 中提取的 XHTML 章节内容注入基础阅读样式（字体、行高、边距）
- 通过 WebView2 的 `NavigateToString()` 加载 HTML 字符串
- 支持目录侧边栏的显示/隐藏切换

## 架构设计

### 系统架构图

```mermaid
graph TD
    A[App.xaml.cs - 应用入口] --> B[MainWindow - 主窗口]
    B --> C[Frame - 导航容器]
    C --> D[BookshelfPage - 书架视图]
    C --> E[ReaderPage - 阅读视图]
    
    D --> F[BookshelfViewModel]
    F --> G[BookshelfService]
    G --> H[(bookshelf.json)]
    
    F --> I[EpubParser]
    I --> J[EPUB 文件]
    
    E --> K[ReaderViewModel]
    K --> I
    K --> L[WebView2 控件]
```

### 数据流

```mermaid
flowchart LR
    A[用户点击导入] --> B[OpenFileDialog]
    B --> C[EpubParser.Parse]
    C --> D[提取封面到缓存目录]
    C --> E[提取元数据]
    D --> F[BookshelfService.AddBook]
    E --> F
    F --> G[持久化 JSON]
    F --> H[BookshelfViewModel 刷新]
    H --> I[UI 更新显示]
```

## 实现细节

### 目录结构

```
EpubRead/
├── Models/
│   ├── Book.cs                    # [NEW] 书籍数据模型：Title, Author, FilePath, CoverPath, ImportDate
│   ├── EpubBook.cs                # [NEW] EPUB 解析结果模型：Metadata, CoverImage, Chapters 列表
│   └── Chapter.cs                 # [NEW] 章节模型：Title, Href, Content, Order
├── ViewModels/
│   ├── BookshelfViewModel.cs      # [NEW] 书架 VM：Books 集合，ImportCommand，DeleteCommand，OpenBookCommand
│   └── ReaderViewModel.cs         # [NEW] 阅读器 VM：CurrentChapter, Chapters, NavigateCommand, GoBackCommand
├── Views/
│   ├── BookshelfPage.xaml         # [NEW] 书架视图：WrapPanel/UniformGrid 布局，BookCard 模板，导入按钮
│   ├── BookshelfPage.xaml.cs      # [NEW] 书架视图代码后置（最小化，仅 InitializeComponent）
│   ├── ReaderPage.xaml            # [NEW] 阅读视图：WebView2 + 目录侧边栏 + 上下翻页按钮
│   └── ReaderPage.xaml.cs         # [NEW] 阅读视图代码后置（初始化 WebView2，处理导航完成事件）
├── Services/
│   ├── EpubParser.cs              # [NEW] EPUB 解析服务：Parse() 解压并解析容器/OPF/NCX，ExtractCover() 提取封面
│   └── BookshelfService.cs        # [NEW] 书架持久化服务：LoadBooks()/SaveBooks()/AddBook()/RemoveBook()
├── Converters/
│   └── CoverPathConverter.cs      # [NEW] 值转换器：封面路径 → BitmapImage，处理缺省占位图
├── App.xaml                       # [MODIFY] 配置全局样式资源，注册服务容器（如有需要）
├── App.xaml.cs                    # [MODIFY] 应用启动逻辑：初始化数据目录
├── MainWindow.xaml                # [MODIFY] 替换空 Grid 为 Frame 导航容器，配置窗口标题和尺寸
├── MainWindow.xaml.cs             # [MODIFY] 添加导航服务逻辑，默认加载 BookshelfPage
└── EpubRead.csproj                # [MODIFY] 添加 NuGet 引用：Microsoft.Web.WebView2, CommunityToolkit.Mvvm
```

### 关键接口设计

```
// EPUB 解析结果
public class EpubBook
{
    public string Title { get; set; }
    public string Author { get; set; }
    public byte[]? CoverImage { get; set; }   // 封面图片原始数据
    public List<Chapter> Chapters { get; set; }
    public string BasePath { get; set; }       // EPUB 内 OEBPS 基础路径
}

// 章节结构
public class Chapter
{
    public string Title { get; set; }
    public string Href { get; set; }           // 相对于 OEBPS 的路径
    public string? Content { get; set; }       // 章节 HTML 内容（按需加载）
    public int Order { get; set; }
}

// 书架中的书籍条目
public class Book
{
    public string Id { get; set; }              // GUID
    public string Title { get; set; }
    public string Author { get; set; }
    public string FilePath { get; set; }
    public string? CoverPath { get; set; }      // 封面缓存路径
    public DateTime ImportDate { get; set; }
}
```

### 性能注意事项

- EPUB 章节内容按需加载：仅在导航到章节时才从 EPUB 解压对应 HTML，避免一次性加载全部内容导致内存压力
- 封面图片解压后缓存到本地，后续直接从缓存读取，避免重复解压
- 大型 EPUB 文件（>50MB）处理：使用流式解压而非全部加载到内存
- WebView2 需确保运行时已安装（Windows 11 内置，Windows 10 可通过 Evergreen Bootstrapper 自动安装）

## 设计风格

采用现代简约桌面应用风格，深色背景搭配柔和圆角卡片，营造沉浸式阅读氛围。

### 书架页面

- **顶部导航栏**：应用标题"EPUB 阅读器"居左，大号圆角"导入"按钮居右，按钮带加号图标和悬停变色效果
- **书架网格区**：使用响应式 UniformGrid 布局，每本书为独立卡片，包含封面图片（200x280，带圆角和轻微阴影）、书名（单行截断）、作者（灰色小字）；空状态时居中显示"书架空空，点击导入按钮添加书籍"提示
- **封面占位图**：默认深灰色背景 + 书本图标居中，保持与其他卡片一致的尺寸
- **交互**：鼠标悬停卡片时轻微上浮 + 阴影加深；右键菜单含"删除"选项

### 阅读器页面

- **顶部工具栏**：左侧返回箭头按钮 + 书名；右侧章节目录按钮
- **左侧可折叠目录面板**：半透明深色背景，列表显示所有章节标题，高亮当前章节，点击跳转
- **阅读区域**：WebView2 占满剩余空间，内容区最大宽度 800px 居中，阅读背景色为柔和米白，字体使用系统默认衬线字体
- **底部导航栏**：上一章/下一章按钮，带进度指示（如"第 3/12 章"）
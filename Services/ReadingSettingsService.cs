using Microsoft.Data.Sqlite;
using EpubRead.Models;

namespace EpubRead.Services;

/// <summary>
/// 阅读设置持久化服务 —— 基于 SQLite，存储全局阅读偏好
/// 使用单行表存储全局统一的阅读设置
/// </summary>
public class ReadingSettingsService
{
    private readonly string _connectionString;

    public ReadingSettingsService(string dbPath)
    {
        _connectionString = $"Data Source={dbPath}";
    }

    /// <summary>
    /// 创建 ReadingSettings 表（如不存在）
    /// </summary>
    public void InitializeTable()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        var cmd = connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS ReadingSettings (
                Id                    TEXT PRIMARY KEY DEFAULT 'default',
                Theme                 INTEGER NOT NULL DEFAULT 0,
                FontSize              INTEGER NOT NULL DEFAULT 17,
                FontFamily            INTEGER NOT NULL DEFAULT 0,
                LineHeight            INTEGER NOT NULL DEFAULT 1,
                PageWidth             INTEGER NOT NULL DEFAULT 1,
                NavigationMode        INTEGER NOT NULL DEFAULT 0
            )
            """;
        cmd.ExecuteNonQuery();

        // 确保默认行存在
        var insertCmd = connection.CreateCommand();
        insertCmd.CommandText = """
            INSERT OR IGNORE INTO ReadingSettings (Id, Theme, FontSize, FontFamily, LineHeight, PageWidth, NavigationMode)
            VALUES ('default', 0, 17, 0, 1, 1, 0)
            """;
        insertCmd.ExecuteNonQuery();
    }

    /// <summary>
    /// 加载阅读设置
    /// </summary>
    public ReadingSettings LoadSettings()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT Theme, FontSize, FontFamily, LineHeight, PageWidth, NavigationMode FROM ReadingSettings WHERE Id = 'default'";

        using var reader = cmd.ExecuteReader();
        if (reader.Read())
        {
            return new ReadingSettings
            {
                Theme = (ThemeType)reader.GetInt32(0),
                FontSize = reader.GetInt32(1),
                FontFamily = (FontFamilyOption)reader.GetInt32(2),
                LineHeight = (LineHeightOption)reader.GetInt32(3),
                PageWidth = (PageWidthOption)reader.GetInt32(4),
                NavigationMode = (NavigationMode)reader.GetInt32(5)
            };
        }

        return new ReadingSettings();
    }

    /// <summary>
    /// 保存阅读设置
    /// </summary>
    public void SaveSettings(ReadingSettings settings)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        var cmd = connection.CreateCommand();
        cmd.CommandText = """
            UPDATE ReadingSettings SET
                Theme = @theme,
                FontSize = @fontSize,
                FontFamily = @fontFamily,
                LineHeight = @lineHeight,
                PageWidth = @pageWidth,
                NavigationMode = @navigationMode
            WHERE Id = 'default'
            """;
        cmd.Parameters.AddWithValue("@theme", (int)settings.Theme);
        cmd.Parameters.AddWithValue("@fontSize", settings.FontSize);
        cmd.Parameters.AddWithValue("@fontFamily", (int)settings.FontFamily);
        cmd.Parameters.AddWithValue("@lineHeight", (int)settings.LineHeight);
        cmd.Parameters.AddWithValue("@pageWidth", (int)settings.PageWidth);
        cmd.Parameters.AddWithValue("@navigationMode", (int)settings.NavigationMode);
        cmd.ExecuteNonQuery();
    }
}

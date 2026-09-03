using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mew.Launcher;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true)]
[JsonSerializable(typeof(LauncherDataFileDto))]
[JsonSerializable(typeof(List<LegacyItem>))] // 旧格式迁移时反序列化
internal sealed partial class LauncherJsonContext : JsonSerializerContext;

/// <summary>磁盘上的启动项 DTO:新结构字段(categoryIds 数组);categoryId 为旧字段,仅读取用于单值 → 数组迁移,写入时省略。</summary>
internal sealed record LauncherItemDto(
    string? Id,
    string? Name,
    string? Command,
    string? Args = null,
    string? WorkingDirectory = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? CategoryId = null,
    List<string>? CategoryIds = null,
    string? Icon = null,
    string? Hotkey = null,
    string? Description = null);

/// <summary>数据文件根结构:分类树 + 启动项。</summary>
internal sealed record LauncherDataFileDto(
    List<LauncherCategory>? Categories,
    List<LauncherItemDto>? Items);

/// <summary>
/// 启动项数据源:单一 JSON 配置文件(分类树 + 启动项),首次运行生成默认示例,变更后由调用方保存。
/// 旧格式(平铺数组)首启自动迁移并写回新格式。读写使用源生成序列化,避免运行时反射(AOT/Trim 兼容)。
/// </summary>
internal sealed class LauncherStore
{
    private List<LauncherCategory> _categories = [];

    public LauncherStore(string? filePath = null)
    {
        FilePath = filePath
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Mew", "launcher.json");
    }

    public string FilePath { get; }

    public IReadOnlyList<LauncherCategory> Categories => _categories;

    public List<LauncherItem> Load()
    {
        if (!File.Exists(FilePath))
        {
            var (categories, items) = LauncherData.MigrateLegacy(DefaultItems());
            _categories = categories;
            Save(items);
            return items;
        }

        try
        {
            var json = File.ReadAllText(FilePath);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                return MigrateAndRewrite(json);
            }

            var file = JsonSerializer.Deserialize(json, LauncherJsonContext.Default.LauncherDataFileDto)
                ?? new LauncherDataFileDto(null, null);
            var hasLegacyMembership = file.Items?.Any(d => d.CategoryId is not null) == true;
            _categories = LauncherData.NormalizeTree(file.Categories ?? []);
            var items = LauncherData.NormalizeCategoryRefs(
                _categories, (file.Items ?? []).Select(FromDto).ToList());
            if (hasLegacyMembership)
            {
                Save(items); // 单值归属迁移后立即写回,与旧平铺格式迁移一致
            }
            return items;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            var (_, items) = LauncherData.MigrateLegacy(DefaultItems());
            return items;
        }
    }

    public void Save(List<LauncherItem> items)
    {
        var file = new LauncherDataFileDto(
            _categories,
            items.Select(ToDto).ToList());
        Write(file);
    }

    /// <summary>更新内存分类树(分类管理操作后调用);真正落盘由随后的 <see cref="Save"/> 全量写入。</summary>
    public void UpdateCategories(List<LauncherCategory> categories)
    {
        _categories = LauncherData.NormalizeTree(categories);
    }

    private void Write(LauncherDataFileDto file)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);

            // 缩进 + 保留中文可读,便于手写与 diff;序列化元数据仍走源生成
            var options = new JsonWriterOptions
            {
                Indented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            };
            using var stream = File.Create(FilePath);
            using var writer = new Utf8JsonWriter(stream, options);
            JsonSerializer.Serialize(writer, file, LauncherJsonContext.Default.LauncherDataFileDto);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>旧格式(平铺数组)→ 新结构,迁移后立即写回。</summary>
    private List<LauncherItem> MigrateAndRewrite(string legacyJson)
    {
        var legacy = JsonSerializer.Deserialize(legacyJson, LauncherJsonContext.Default.ListLegacyItem) ?? [];
        var (categories, items) = LauncherData.MigrateLegacy(legacy);
        _categories = categories;
        Save(items);
        return items;
    }

    /// <summary>DTO → 内存模型;旧字段 categoryId 单值迁移为数组,categoryIds 优先。</summary>
    private static LauncherItem FromDto(LauncherItemDto dto) => new(
        dto.Id ?? "item-" + Guid.NewGuid().ToString("N")[..8],
        dto.Name ?? "",
        dto.Command ?? "",
        Args: dto.Args,
        WorkingDirectory: dto.WorkingDirectory,
        CategoryIds: dto.CategoryIds ?? (dto.CategoryId is not null ? [dto.CategoryId] : []),
        Icon: dto.Icon,
        Hotkey: dto.Hotkey,
        Description: dto.Description);

    private static LauncherItemDto ToDto(LauncherItem item) => new(
        item.Id,
        item.Name,
        item.Command,
        item.Args,
        item.WorkingDirectory,
        CategoryIds: item.CategoryIds ?? [],
        Icon: item.Icon,
        Hotkey: item.Hotkey,
        Description: item.Description);

    private static List<LegacyItem> DefaultItems() =>
    [
        new LegacyItem("vs-code", "VS Code", "code", Category: "开发"),
        new LegacyItem("terminal", "Terminal", "wt", Category: "开发"),
        new LegacyItem("github", "GitHub", "https://github.com", Category: "工具"),
        new LegacyItem("notepad", "记事本", "notepad", Category: "工具"),
    ];
}

using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;

namespace Mew.Workbench;

/// <summary>
/// 分节设置服务:%APPDATA%\Mew\settings.json。根节为宿主级设置(主题模式、浮层呼出键),
/// 工具模块设置按模块 Id 分节(如 launcher 节的列表形态);旧扁平结构首次加载自动迁移。
/// 内部用 JsonObject DOM(无反射,AOT/Trim 兼容),模块节经调用方源生成类型往返。
/// </summary>
public sealed class SettingsService : ISettingsService
{
    private JsonObject _root = [];

    public SettingsService(string? filePath = null)
    {
        FilePath = filePath
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Mew", "settings.json");
    }

    public string FilePath { get; }

    /// <summary>宿主级设置:主题模式,取值 ThemeVariant 枚举名:System/Light/Dark。</summary>
    public string? ThemeMode { get => GetString("themeMode"); set => SetString("themeMode", value); }

    /// <summary>宿主级设置:浮层呼出热键,形如 Alt+Space。</summary>
    public string? OverlayHotkey { get => GetString("overlayHotkey"); set => SetString("overlayHotkey", value); }

    /// <summary>宿主级设置:呼出热键是否启用；缺省（未配置）视为启用。</summary>
    public bool OverlayHotkeyEnabled { get => GetBool("overlayHotkeyEnabled", true); set => SetBool("overlayHotkeyEnabled", value); }

    /// <summary>宿主级设置:插件目录列表（设置→插件→插件目录维护的完整列表，第一项为默认目录；
    /// 为空或缺省时回退到用户目录；安装目录由宿主恒追加）。与 `MEW_PLUGINS_EXTRA` 合并生效。</summary>
    public List<string>? PluginDirs { get => GetStringArray("pluginDirs"); set => SetStringArray("pluginDirs", value); }

    /// <summary>读取工具模块设置节:无该节、节类型不匹配或反序列化失败时返回 null(损坏的模块节不阻塞启动)。</summary>
    public T? ReadSection<T>(string moduleId, JsonTypeInfo<T> typeInfo) where T : class
    {
        if (_root[moduleId] is not JsonObject section)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize(section, typeInfo);
        }
        catch (JsonException)
        {
            return null; // 模块节字段类型不符(如 itemsViewMode 为数字)时静默回退默认值
        }
    }

    /// <summary>写入工具模块设置节(覆盖该模块整节)。</summary>
    public void WriteSection<T>(string moduleId, T value, JsonTypeInfo<T> typeInfo)
        => _root[moduleId] = JsonSerializer.SerializeToNode(value, typeInfo);

    /// <summary>从磁盘加载设置;旧扁平结构(itemsViewMode 在根节)自动迁移到 launcher 模块节并回写。</summary>
    public void Load()
    {
        try
        {
            _root = File.Exists(FilePath)
                ? JsonNode.Parse(File.ReadAllText(FilePath)) as JsonObject ?? []
                : [];
            MigrateLegacyFlatSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            _root = [];
        }
    }

    /// <summary>写回磁盘;目录缺失时创建,保留缩进与中文可读。</summary>
    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, _root.ToJsonString(new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            }));
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>旧扁平结构迁移:根节的 itemsViewMode(旧 Launcher 形态偏好)移入 launcher 模块节。</summary>
    private void MigrateLegacyFlatSettings()
    {
        if (_root.TryGetPropertyValue("itemsViewMode", out var legacy) && legacy is not null)
        {
            _root.Remove("itemsViewMode");
            var section = _root["launcher"] as JsonObject ?? [];
            _root["launcher"] = section;
            section["itemsViewMode"] = legacy.DeepClone();
            Save();
        }
    }

    private string? GetString(string key)
    {
        if (!_root.TryGetPropertyValue(key, out var node) || node is null)
        {
            return null;
        }

        try
        {
            return node.GetValue<string>();
        }
        catch (InvalidOperationException)
        {
            return null; // 根值类型与字符串不符(如 "overlayHotkey": 123)时静默回退默认值
        }
    }

    private void SetString(string key, string? value)
    {
        if (value is null)
        {
            _root.Remove(key);
        }
        else
        {
            _root[key] = value;
        }
    }

    private bool GetBool(string key, bool defaultValue)
    {
        if (!_root.TryGetPropertyValue(key, out var node) || node is null)
        {
            return defaultValue;
        }

        try
        {
            return node.GetValue<bool>();
        }
        catch (InvalidOperationException)
        {
            return defaultValue; // 类型不符时回退缺省
        }
    }

    private void SetBool(string key, bool value)
    {
        _root[key] = value;
    }

    private List<string>? GetStringArray(string key)
    {
        if (!_root.TryGetPropertyValue(key, out var node) || node is not JsonArray arr)
        {
            return null;
        }

        try
        {
            return arr.Where(e => e is not null).Select(e => e!.GetValue<string>()).ToList();
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException)
        {
            return null; // 数组元素类型不符时静默回退 null
        }
    }

    private void SetStringArray(string key, List<string>? value)
    {
        if (value is null)
        {
            _root.Remove(key);
        }
        else
        {
            var arr = new JsonArray();
            foreach (var s in value) arr.Add(s);
            _root[key] = arr;
        }
    }
}

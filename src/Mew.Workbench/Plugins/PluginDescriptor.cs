namespace Mew.Workbench.Plugins;

/// <summary>
/// 发现结果：清单 + 来源路径 + 校验状态。
/// 未通过校验的项标红且不加载，后发现的重复 id 被标记为 Duplicate。
/// </summary>
public sealed class PluginDescriptor
{
    public PluginDescriptor(PluginManifest manifest, string manifestPath, IReadOnlyList<string> validationErrors, bool isDuplicate = false)
    {
        Manifest = manifest;
        ManifestPath = manifestPath;
        ValidationErrors = validationErrors;
        IsDuplicate = isDuplicate;
    }

    public PluginManifest Manifest { get; }
    public string ManifestPath { get; }
    public IReadOnlyList<string> ValidationErrors { get; }
    public bool IsDuplicate { get; }

    public bool IsValid => ValidationErrors.Count == 0 && !IsDuplicate;
    public string Id => Manifest.Id;
    public bool RequiresJit => Manifest.RequiresJit;

    /// <summary>用于设置→插件列表的展示态：有效/标红/重复/需 JIT 置灰由调用方结合运行环境判定。</summary>
    public PluginHealth Health(bool isJitAvailable)
    {
        if (IsDuplicate) return PluginHealth.DuplicateId;
        if (!IsValid) return PluginHealth.InvalidManifest;
        if (RequiresJit && !isJitAvailable) return PluginHealth.NeedsJit;
        return PluginHealth.Healthy;
    }
}

public enum PluginHealth
{
    Healthy,
    InvalidManifest,
    DuplicateId,
    NeedsJit,
    Disabled,
    Crashed,
}

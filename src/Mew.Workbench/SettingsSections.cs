using Aprillz.MewUI.Controls;

namespace Mew.Workbench;

/// <summary>
/// 设置文档的模块设置节注册表(宿主):工具模块经 <see cref="ToolModuleContext.SettingsSections"/>
/// 贡献「设置节」(如 Launcher 的「热键」「数据」),宿主在设置侧边栏统一列出并在设置文档中按需构建内容。
/// 外观(主题)节为宿主保留节,不经本注册表。
/// </summary>
public sealed class SettingsSectionRegistry
{
    private readonly List<SettingsSection> _sections = [];

    /// <summary>注册一个模块设置节;id 重复时拒绝,内容在选中该节时惰性构建。</summary>
    public void Add(string id, string label, Func<UIElement> build)
    {
        if (_sections.Any(section => section.Id == id))
        {
            throw new ArgumentException($"设置节“{id}”已注册。", nameof(id));
        }

        _sections.Add(new SettingsSection(id, label, build));
    }

    public IReadOnlyList<SettingsSection> Sections => _sections;

    /// <summary>构建指定设置节的内容;未注册的 id 拒绝。</summary>
    public UIElement Build(string id)
    {
        var section = _sections.FirstOrDefault(section => section.Id == id)
            ?? throw new ArgumentException($"不存在设置节“{id}”。", nameof(id));
        return section.Build();
    }
}

/// <summary>一个模块设置节:id(设置导航键)、label(侧边栏按钮文案)与内容构建。</summary>
public sealed record SettingsSection(string Id, string Label, Func<UIElement> Build);

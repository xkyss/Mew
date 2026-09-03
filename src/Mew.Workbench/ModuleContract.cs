using Aprillz.MewUI;

namespace Mew.Workbench;

/// <summary>
/// 工具模块契约:模块实现本接口,宿主在组合根显式注册(编译期组合,ADR-000101-03/ADR-000200)。
/// <see cref="Configure"/> 内经 <see cref="ToolModuleContext"/> 贡献五区与服务;
/// 边界:宿主独占 <see cref="Workbench.Build()"/>,模块只贡献、不组装。
/// </summary>
public interface IMewToolModule
{
    /// <summary>稳定 Id,跨模块唯一;用于设置分节、热键冲突检测与浮层来源标记。</summary>
    string Id { get; }

    /// <summary>显示名(活动栏/设置等场景)。</summary>
    string DisplayName { get; }

    /// <summary>贡献阶段:宿主在窗口组装前调用,全部模块贡献完后宿主统一 Build()。</summary>
    void Configure(ToolModuleContext context);
}

/// <summary>
/// 工具模块的贡献上下文:宿主在模块注册时传入。
/// 表面 = 裸 Workbench(五区贡献 + 运行时方法)+ 窗口(句柄 + 宿主窗口对象)+ 四个服务接口
/// + 设置节注册表;托盘菜单贡献与自定义浮层行渲染按规格暂缓,不进契约。
/// </summary>
public sealed class ToolModuleContext
{
    public ToolModuleContext(
        Workbench workbench,
        IntPtr windowHandle,
        Window? window,
        IHotkeyService hotkeys,
        ISettingsService settings,
        IOverlayService overlay,
        IThemeService theme,
        SettingsSectionRegistry settingsSections)
    {
        Workbench = workbench;
        WindowHandle = windowHandle;
        Window = window;
        Hotkeys = hotkeys;
        Settings = settings;
        Overlay = overlay;
        Theme = theme;
        SettingsSections = settingsSections;
    }

    /// <summary>五区贡献(Theme/ActivityBar/SideBar/EditorArea/Panel/StatusBar)与运行时方法;Build() 归宿主。</summary>
    public Workbench Workbench { get; }

    /// <summary>宿主窗口的原生句柄(全局热键注册等场景)。</summary>
    public IntPtr WindowHandle { get; }

    /// <summary>宿主窗口对象:模态对话框/Toast/焦点判定等 UI 归属场景;无窗口的宿主组装测试中为 null。</summary>
    public Window? Window { get; }

    /// <summary>全局热键中央注册表:注册/注销/冲突检测(含跨模块)。实现接线在票据 07。</summary>
    public IHotkeyService Hotkeys { get; }

    /// <summary>设置文档:模块按 Id 分节读写。</summary>
    public ISettingsService Settings { get; }

    /// <summary>浮层服务:注册本模块的搜索源。</summary>
    public IOverlayService Overlay { get; }

    /// <summary>主题服务:读取/切换主题模式,订阅模式变更。</summary>
    public IThemeService Theme { get; }

    /// <summary>设置文档的模块设置节注册表:模块按 Id 贡献设置节(如 Launcher 的「数据」),宿主统一呈现。</summary>
    public SettingsSectionRegistry SettingsSections { get; }
}

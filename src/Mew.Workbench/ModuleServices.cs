using System.Text.Json.Serialization.Metadata;
using Aprillz.MewUI;

namespace Mew.Workbench;

/// <summary>
/// 全局热键中央注册表(宿主):注册/注销 + 冲突检测(含跨模块)。
/// 实现为 <see cref="HotkeyService"/>:浮层呼出键为宿主热键,模块每项热键经此注册,WM_HOTKEY 按 id 分发回调。
/// </summary>
public interface IHotkeyService
{
    /// <summary>注册全局热键;成功返回 true,格式非法/已被占用(含跨模块冲突)返回 false。
    /// <paramref name="label"/> 为占用方描述(如「浮层呼出键」/「启动项『X』」),供冲突提示点名。</summary>
    bool Register(IntPtr hwnd, string hotkey, Action callback, string? label = null);

    /// <summary>注销指定热键;未注册时静默。</summary>
    void Unregister(string hotkey);

    /// <summary>该热键是否已被注册(含本模块与其他模块)。</summary>
    bool IsRegistered(string hotkey);

    /// <summary>返回占用指定组合的注册描述(注册时给的 label),未占用返回 null;用于冲突提示点名。</summary>
    string? FindOwner(string hotkey);
}

/// <summary>
/// 设置文档(宿主):模块按 Id 分节读写,根节归宿主(主题/呼出键)。
/// 模块节经调用方源生成类型往返,无反射(AOT/Trim 兼容)。
/// </summary>
public interface ISettingsService
{
    /// <summary>读取模块设置节;无该节、节类型不匹配或反序列化失败时返回 null。</summary>
    T? ReadSection<T>(string moduleId, JsonTypeInfo<T> typeInfo) where T : class;

    /// <summary>写入模块设置节(覆盖该模块整节)。</summary>
    void WriteSection<T>(string moduleId, T value, JsonTypeInfo<T> typeInfo);

    /// <summary>将设置文档写回磁盘;模块写完设置节后调用以持久化,宿主在退出/改设置时也调用。</summary>
    void Save();
}

/// <summary>
/// 浮层服务(宿主):注册搜索源;跨源结果扁平混排 + 来源标记,
/// 唯一搜索源时行为与单源时代一致。
/// </summary>
public interface IOverlayService
{
    /// <summary>注册本模块的搜索源;运行期注册后立即按当前查询刷新结果。</summary>
    void AddSearchSource(ISearchSource source);
}

/// <summary>
/// 主题服务(宿主):读取/切换主题模式,订阅模式变更(如标题栏按钮、状态栏同步)。
/// </summary>
public interface IThemeService
{
    /// <summary>当前主题模式:System/Light/Dark。</summary>
    ThemeVariant Mode { get; }

    /// <summary>切换主题模式;同模式调用不触发事件。</summary>
    void SetMode(ThemeVariant mode);

    /// <summary>主题模式实际变更后触发。</summary>
    event Action? ThemeModeChanged;
}

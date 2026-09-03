using Aprillz.MewUI;
using Mew.Workbench;

namespace Mew.Launcher;

/// <summary>
/// 启动项图标解析扩展:把 LauncherItem 适配到框架 IconResolver 的通用签名;
/// URL 判定来自启动类型推导(LauncherData.KindOf),保持单一事实来源。
/// </summary>
internal static class IconResolverExtensions
{
    public static ImageSource? Resolve(this IconResolver resolver, LauncherItem item) =>
        resolver.Resolve(item.Icon, item.Command, LauncherData.KindOf(item.Command) == LauncherData.ItemKind.Url);
}

using Aprillz.MewUI;
using Mew.Workbench;
using WorkbenchType = Mew.Workbench.Workbench;
using Xunit;

namespace Mew.Launcher.Tests;

/// <summary>
/// IThemeService 契约衔接:WorkbenchThemeContext 实现契约,SetMode 切换模式并触发 ThemeModeChanged。
/// </summary>
public class ThemeServiceTests
{
    private static IThemeService CreateThemeService() => new WorkbenchType().ThemeContext;

    [Fact]
    public void SetMode_变更模式_触发事件且Mode反映()
    {
        var service = CreateThemeService();
        var changed = 0;
        service.ThemeModeChanged += () => changed++;

        service.SetMode(ThemeVariant.Dark);

        Assert.Equal(1, changed);
        Assert.Equal(ThemeVariant.Dark, service.Mode);
    }

    [Fact]
    public void SetMode_同模式_不触发事件()
    {
        var service = CreateThemeService();
        var changed = 0;
        service.ThemeModeChanged += () => changed++;

        service.SetMode(ThemeVariant.Dark);
        service.SetMode(ThemeVariant.Dark);

        Assert.Equal(1, changed);
    }

    [Fact]
    public void 契约实例_模式往返()
    {
        var service = CreateThemeService();

        service.SetMode(ThemeVariant.Light);
        Assert.Equal(ThemeVariant.Light, service.Mode);

        service.SetMode(ThemeVariant.System);
        Assert.Equal(ThemeVariant.System, service.Mode);
    }
}

using System.Text.Json.Serialization.Metadata;
using Aprillz.MewUI.Controls;
using Mew.Workbench;
using Xunit;
using OverlayServiceContract = Mew.Workbench.IOverlayService;
using WorkbenchType = Mew.Workbench.Workbench;

namespace Mew.Launcher.Tests;

/// <summary>
/// Launcher 模块化(票据 05/06):LauncherModule 实现 IMewToolModule,Configure 经 ToolModuleContext
/// 贡献五区(启动上下文)、浮层搜索源与设置节;窗口/设置文档等宿主职责归 Mew.Host,
/// 本测试以宿主姿态补上设置上下文后验证组装可运行。
/// 与 WorkbenchConfigurationTests 同集合串行:两者都操作真实 %APPDATA%\Mew 布局文件,
/// 且 MewDock 在 Build 后持有文件句柄,并行会互相踩文件。
/// </summary>
[Collection("IsolatedUserFiles")]
public class LauncherModuleTests
{
    /// <summary>记录注册的搜索源的浮层契约替身(测试环境不建 MewUI 窗口)。</summary>
    private sealed class RecordingOverlay : OverlayServiceContract
    {
        public List<ISearchSource> Sources { get; } = [];

        public void AddSearchSource(ISearchSource source) => Sources.Add(source);
    }

    /// <summary>非 SettingsService 的设置契约替身:验证模块只按契约读写,不依赖具体宿主类型(评审修复)。</summary>
    private sealed class RecordingSettings : ISettingsService
    {
        public T? ReadSection<T>(string moduleId, JsonTypeInfo<T> typeInfo) where T : class => null;

        public void WriteSection<T>(string moduleId, T value, JsonTypeInfo<T> typeInfo)
        {
        }

        public void Save()
        {
        }
    }

    /// <summary>评审修复:宿主传入非 SettingsService 的实现时模块照常 Configure(不再向下转型,不崩溃)。</summary>
    [Fact]
    public void Configure_非SettingsService实现_仅按契约读写不崩溃()
    {
        using var _ = IsolateUserFiles();

        var workbench = new WorkbenchType();
        var context = new ToolModuleContext(
            workbench,
            windowHandle: IntPtr.Zero,
            window: null,
            hotkeys: new HotkeyService(),
            settings: new RecordingSettings(),
            overlay: new RecordingOverlay(),
            theme: workbench.ThemeContext,
            settingsSections: new SettingsSectionRegistry());

        var module = new LauncherModule();
        module.Configure(context); // 不抛:模块不再依赖 SettingsService 具体类型

        Assert.Equal("launcher", module.Id);
        Assert.Equal(["data"], context.SettingsSections.Sections.Select(section => section.Id));
    }

    [Fact]
    public void Configure_贡献五区与设置节_注册搜索源_Build通过()
    {
        using var _ = IsolateUserFiles();

        var workbench = new WorkbenchType();
        var settings = new SettingsService(Path.Combine(Path.GetTempPath(), "mew-module-tests", Guid.NewGuid().ToString("N") + ".json"));
        var overlay = new RecordingOverlay();
        var settingsSections = new SettingsSectionRegistry();
        var context = new ToolModuleContext(
            workbench,
            windowHandle: IntPtr.Zero,
            window: null,
            hotkeys: new HotkeyService(),
            settings: settings,
            overlay: overlay,
            theme: workbench.ThemeContext,
            settingsSections: settingsSections);

        var module = new LauncherModule();
        module.Configure(context);

        // 宿主补上设置上下文(票据 06:设置活动栏/侧边栏/文档归宿主)
        workbench
            .ActivityBar(bar => bar.Item("settings", "设置", GlyphKind.Hamburger))
            .SideBar(side => side.View("settings", "设置", new StackPanel()))
            .EditorArea(editor => editor.Document("settings-document", "设置", new StackPanel()));

        // 五区配对/ID 唯一校验通过;设置文档可运行时打开(宿主注册生效)
        workbench.Build();
        workbench.OpenDocument("settings-document");

        Assert.Equal("launcher", module.Id);
        Assert.Equal("启动项", module.DisplayName);
        Assert.Equal(["data"], settingsSections.Sections.Select(section => section.Id)); // 「热键」节票据 07 起归宿主
        var source = Assert.Single(overlay.Sources);
        Assert.Equal("launcher", source.Id);
    }

    /// <summary>临时移走用户真实数据/布局文件,保证用例不污染用户数据且任意本机可复现。</summary>
    private static IDisposable IsolateUserFiles()
    {
        var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Mew");
        var names = new[] { "launcher.json", "layout.json", "presentation.json" };
        var saved = names.ToDictionary(name => name, name => File.Exists(Path.Combine(appData, name))
            ? File.ReadAllBytes(Path.Combine(appData, name))
            : null);
        foreach (var name in names)
        {
            File.Delete(Path.Combine(appData, name));
        }

        return new Disposable(() =>
        {
            foreach (var (name, bytes) in saved)
            {
                var path = Path.Combine(appData, name);
                if (bytes is not null)
                {
                    Directory.CreateDirectory(appData);
                    File.WriteAllBytes(path, bytes);
                }
                else
                {
                    File.Delete(path);
                }
            }
        });
    }

    private sealed class Disposable(Action dispose) : IDisposable
    {
        public void Dispose() => dispose();
    }
}

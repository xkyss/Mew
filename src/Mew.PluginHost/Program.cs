using Aprillz.MewUI;
using Mew.PluginHost;

Win32Platform.Register();
Direct2DBackend.Register();

// 崩溃可观测：未处理异常记入 plugin-host.log，否则进程只剩退出码可查
AppDomain.CurrentDomain.UnhandledException += (_, e) =>
{
    try
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Mew");
        Directory.CreateDirectory(dir);
        File.AppendAllText(Path.Combine(dir, "plugin-host.log"), $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 未处理异常：{e.ExceptionObject}{Environment.NewLine}");
    }
    catch { }
};

try
{
    var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Mew");
    Directory.CreateDirectory(dir);
    File.AppendAllText(Path.Combine(dir, "plugin-host.log"), $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 扩展主机启动{Environment.NewLine}");
}
catch { }

var app = new PluginHostApp();
app.Run();

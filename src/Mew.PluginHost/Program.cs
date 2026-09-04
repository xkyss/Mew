using Aprillz.MewUI;
using Mew.PluginHost;

Win32Platform.Register();
Direct2DBackend.Register();

// 崩溃可观测：未处理异常记入 plugin-host.log，否则进程只剩退出码可查
AppDomain.CurrentDomain.UnhandledException += (_, e) =>
{
    PluginHostLog.Write($"未处理异常：{e.ExceptionObject}");
};

PluginHostLog.Write("主界面启动");

var app = new PluginHostApp();
app.Run();

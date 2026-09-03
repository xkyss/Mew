using System.ComponentModel;
using System.Diagnostics;

namespace Mew.Launcher;

/// <summary>
/// 一次启动尝试的结果:是否成功与面向用户的消息。
/// </summary>
public sealed record LaunchResult(bool Success, string Message);

/// <summary>
/// 启动执行器:程序/脚本/URL 统一走 ShellExecute,URL 不带参数;失败原因透出给日志与状态栏。
/// 窗口内列表与呼出浮层共用。
/// </summary>
internal sealed class LauncherRunner
{
    public LaunchResult Launch(LauncherItem item)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = item.Command,
                WorkingDirectory = item.WorkingDirectory ?? "",
                UseShellExecute = true,
            };

            if (!IsUrl(item.Command) && !string.IsNullOrWhiteSpace(item.Args))
            {
                startInfo.Arguments = item.Args;
            }

            Process.Start(startInfo);
            return new LaunchResult(true, $"{item.Name} 已启动");
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            return new LaunchResult(false, $"{item.Name} 启动失败:{ex.Message}");
        }
    }

    private static bool IsUrl(string command) =>
        LauncherData.KindOf(command) == LauncherData.ItemKind.Url;
}

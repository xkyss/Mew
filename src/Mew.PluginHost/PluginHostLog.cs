namespace Mew.PluginHost;

/// <summary>扩展主机日志：`%APPDATA%\Mew\plugin-host.log` 落盘 + 内存环（供底部 Panel「插件日志」视图展示启动期生命周期），静默失败不影响常驻。</summary>
internal static class PluginHostLog
{
    private static readonly List<(DateTime Time, string Message)> _lines = [];
    private static readonly object _lock = new();
    private const int MaxLines = 500;

    public static void Write(string message)
    {
        var now = DateTime.Now;
        lock (_lock)
        {
            _lines.Add((now, message));
            while (_lines.Count > MaxLines) _lines.RemoveAt(0);
        }
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Mew");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "plugin-host.log"), $"[{now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
        }
        catch { }
    }

    public static IReadOnlyList<(DateTime Time, string Message)> SnapshotLines()
    {
        lock (_lock) return _lines.ToList();
    }
}

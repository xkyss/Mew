namespace Mew.Workbench;

/// <summary>
/// 列表单击启动的双击防重:同一启动项在时间窗内的重复启动请求被忽略(双击只启动一次),
/// 时间窗结束后重新放行;不同启动项互不影响。时间由调用方注入以便测试。
/// </summary>
public sealed class LaunchDebouncer(TimeSpan window)
{
    private readonly Dictionary<string, DateTime> _lastLaunchByItem = [];

    /// <summary>请求启动指定启动项;时间窗内已启动过则返回 false 并忽略本次。</summary>
    public bool ShouldLaunch(string itemId, DateTime now)
    {
        if (_lastLaunchByItem.TryGetValue(itemId, out var last) && now - last < window)
        {
            return false;
        }

        _lastLaunchByItem[itemId] = now;
        return true;
    }
}

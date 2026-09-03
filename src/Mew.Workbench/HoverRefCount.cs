namespace Mew.Workbench;

/// <summary>
/// 悬停计数:主按钮与悬停浮现的编辑按钮共用同一计数,进入任一递增、离开任一递减;
/// 计数在 0↔1 边界变化时回调。保证在两按钮之间移动时悬停态不丢失,且与事件先后顺序无关。
/// </summary>
public sealed class HoverRefCount
{
    private int _count;

    /// <summary>悬停态是否激活(计数大于 0)。</summary>
    public bool IsRaised => _count > 0;

    /// <summary>悬停态在激活/未激活之间翻转时触发。</summary>
    public event Action? RaisedChanged;

    public void Enter() => Set(_count + 1);

    public void Leave() => Set(Math.Max(0, _count - 1));

    private void Set(int next)
    {
        var wasRaised = _count > 0;
        _count = next;

        if (wasRaised != _count > 0)
        {
            RaisedChanged?.Invoke();
        }
    }
}

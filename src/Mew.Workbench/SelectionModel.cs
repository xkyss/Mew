namespace Mew.Workbench;

/// <summary>
/// 列表选中模型:↑/↓ 移动(环绕)、刷新后收敛到可见范围内;空列表保持无选中。
/// 启动项列表与呼出浮层的循环选择共用。
/// </summary>
public sealed class SelectionModel
{
    /// <summary>当前选中索引;空列表时为 0(无可见选中)。</summary>
    public int Selected { get; private set; }

    /// <summary>下移一项,越过末位环绕到首位;空列表保持无选中。</summary>
    public void MoveDown(int count) => Move(count, +1);

    /// <summary>上移一项,越过首位环绕到末位;空列表保持无选中。</summary>
    public void MoveUp(int count) => Move(count, -1);

    /// <summary>刷新后收敛:空列表归零;否则把选中夹回 [0, count-1],不越界。</summary>
    public void Clamp(int count) => Selected = count <= 0 ? 0 : Math.Clamp(Selected, 0, count - 1);

    private void Move(int count, int delta)
    {
        if (count <= 0)
        {
            Selected = 0;
            return;
        }

        Selected = ((Selected + delta) % count + count) % count;
    }
}

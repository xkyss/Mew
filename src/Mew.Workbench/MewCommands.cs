using Aprillz.MewUI;

namespace Mew.Workbench;

/// <summary>
/// 0.20 命令模型助手:菜单点击不再挂 MenuItem,而是注册 Command 到 CommandScope 由菜单派发。
/// 本助手消除「注册 + 构造」样板的三处重复（标题栏/分类树/启动项菜单）。
/// </summary>
public static class MewCommands
{
    /// <summary>构造命令并在作用域注册处理器;菜单项引用返回的 Command 即可点击。</summary>
    public static Command Register(CommandScope scope, string id, string text, Action execute)
    {
        var command = new Command(id, text);
        scope.Register(command, execute, null);
        return command;
    }
}

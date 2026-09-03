using System.Runtime.InteropServices;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace Mew.Workbench;

/// <summary>
/// 全局热键呼出的悬浮搜索浮层(框架级):跨搜索源结果扁平混排 + 行尾来源标记,
/// 输入即过滤、回车激活选中项、上下键切换、Esc/失焦关闭。
/// 行渲染框架统一(图标 + 主行 + 副行),契约不含自定义行渲染。
/// </summary>
public sealed class OverlayWindow : IOverlayService
{
    private const int Width = 640;
    private const int Height = 420;
    private static readonly Color White = Color.FromArgb(255, 255, 255, 255);

    private readonly Window _window;
    private readonly Window _owner;
    private readonly WorkbenchThemeContext _theme;
    private readonly List<ISearchSource> _sources = [];
    private readonly TextBox _searchBox = new() { Placeholder = "输入以搜索", CanDrag = false };
    private readonly StackPanel _resultPanel = new();
    private List<OverlayResultEntry> _results = [];
    private readonly SelectionModel _selection = new();

    public OverlayWindow(Window owner, WorkbenchThemeContext theme)
    {
        _owner = owner;
        _theme = theme;

        _window = new Window
        {
            Borderless = true,
            Topmost = true,
            ShowInTaskbar = false,
        };

        _window.Content = BuildContent();
        _window.PreviewKeyDown += OnKeyDown;
        _searchBox.TextChanged += text => Refresh(text);
        _window.Deactivated += () => _window.Hide();
    }

    /// <summary>注册搜索源;运行期注册后立即按当前查询刷新结果。</summary>
    public void AddSearchSource(ISearchSource source)
    {
        _sources.Add(source);
        Refresh(_searchBox.Text);
    }

    public void ShowOverlay()
    {
        _window.Show(_owner);
        PositionOverlay();
        _window.Activate();
        _searchBox.Text = "";
        _searchBox.Focus();
        Refresh("");
    }

    private UIElement BuildContent() => new Border()
        .WithTheme((_, border) => border.Background(_theme.EditorArea.Background))
        .Child(
            new StackPanel()
                .Padding(16)
                .Spacing(8)
                .Children(
                    _searchBox,
                    _resultPanel
                )
        );

    private void PositionOverlay()
    {
        var screenWidth = User32.GetSystemMetrics(0); // SM_CXSCREEN
        var screenHeight = User32.GetSystemMetrics(1); // SM_CYSCREEN
        var x = (screenWidth - Width) / 2;
        var y = Math.Max(40, (screenHeight - Height) / 5);
        User32.SetWindowPos(_window.Handle, User32.HwndTopmost, x, y, Width, Height, 0x0040);
    }

    private void OnKeyDown(KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                _window.Hide();
                e.Handled = true;
                break;

            case Key.Enter when _results.Count > 0:
                Activate(_results[_selection.Selected]);
                e.Handled = true;
                break;

            case Key.Up when _results.Count > 0:
                _selection.MoveUp(_results.Count);
                Refresh(_searchBox.Text);
                e.Handled = true;
                break;

            case Key.Down when _results.Count > 0:
                _selection.MoveDown(_results.Count);
                Refresh(_searchBox.Text);
                e.Handled = true;
                break;
        }
    }

    private void Refresh(string query)
    {
        _results = OverlaySearchAggregator.Aggregate(_sources, query).ToList();
        _selection.Clamp(_results.Count);

        _resultPanel.Clear();
        for (var i = 0; i < _results.Count; i++)
        {
            _resultPanel.Add(Row(_results[i], i == _selection.Selected));
        }
    }

    private UIElement Row(OverlayResultEntry entry, bool selected)
    {
        var result = entry.Result;
        var children = new List<UIElement>
        {
            ShellIcons.IconElement(result.Icon),
            new StackPanel()
                .Spacing(2)
                .Children(
                    new Label().Text(result.Title)
                        .WithTheme((_, label) => label.Foreground(selected ? White : _theme.EditorArea.Foreground)),
                    new Label().Text(result.Subtitle).FontSize(11)
                        .WithTheme((_, label) => label.Foreground(selected ? White : _theme.EditorArea.Foreground))
                ),
        };

        // 多搜索源时行尾打来源标记;唯一搜索源时不显示,与单源时代行为一致
        if (_sources.Count > 1)
        {
            children.Add(new Label().Text(entry.Source.DisplayName).FontSize(10)
                .WithTheme((_, label) => label.Foreground(selected ? White : _theme.EditorArea.Foreground)));
        }

        return new Button()
            .Content(
                new StackPanel()
                    .Orientation(Orientation.Horizontal)
                    .Spacing(8)
                    .Children(children.ToArray()))
            .OnClick(() => Activate(entry))
            .CanDrag(false)
            .WithTheme((_, button) => button.Background(selected ? _theme.EditorArea.Accent : _theme.EditorArea.Background));
    }

    private void Activate(OverlayResultEntry entry)
    {
        entry.Result.Activate();
        _window.Hide();
    }

    private static class User32
    {
        internal static readonly IntPtr HwndTopmost = new(-1);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

        [DllImport("user32.dll")]
        internal static extern int GetSystemMetrics(int nIndex);
    }
}

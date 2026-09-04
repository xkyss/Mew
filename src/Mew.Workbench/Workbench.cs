using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.MewDock;

namespace Mew.Workbench;

/// <summary>
/// Compile-time, type-safe builder for a five-zone MewUI workbench.
/// </summary>
public sealed class Workbench
{
    private readonly ActivityBar _activityBar = new();
    private readonly SideBar _sideBar = new();
    private readonly EditorArea _editorArea = new();
    private readonly BottomPanel _panel = new();
    private readonly StatusBar _statusBar = new();
    private readonly WorkbenchThemeContext _theme = new();
    private DockingManager? _docking;
    private WorkbenchView? _view;
    private bool _activityBarVisible = true;
    private bool _sideBarVisible = true;
    private bool _panelVisible = true;
    private bool _statusBarVisible = true;
    private string? _activeActivityId;
    private readonly Dictionary<string, DocumentReveal> _documentReveals = [];

    /// <summary>工作台呈现状态变更通知;订阅方据此同步区域外观与 View 菜单文案。</summary>
    public event Action? PresentationChanged;

    /// <summary>宿主主窗口的按键事件转发(模块键盘导航、详情定位快捷键等场景)。</summary>
    public event Action<KeyEventArgs>? WindowKeyDown;

    /// <summary>宿主把主窗口按键事件转发给订阅方(模块键盘导航)。</summary>
    public void NotifyWindowKeyDown(KeyEventArgs e) => WindowKeyDown?.Invoke(e);

    public bool IsActivityBarVisible => _activityBarVisible;
    public bool IsSideBarVisible => _sideBarVisible;
    public bool IsPanelVisible => _panelVisible;
    public bool IsStatusBarVisible => _statusBarVisible;
    public string? ActiveActivityId => _activeActivityId;

    /// <summary>当前激活的编辑器文档 id(编辑器区无激活文档时为 null),供应用按激活文档路由键盘操作。</summary>
    public string? ActiveDocumentId =>
        _docking?.ActiveGroup?.ActivePane is { IsDocument: true } pane ? pane.Component : null;

    public void ToggleActivityBar() { _activityBarVisible = !_activityBarVisible; PresentationChanged?.Invoke(); }
    public void ToggleSideBar() { _sideBarVisible = !_sideBarVisible; PresentationChanged?.Invoke(); }
    public void TogglePanel() { _panelVisible = !_panelVisible; PresentationChanged?.Invoke(); }
    public void ToggleStatusBar() { _statusBarVisible = !_statusBarVisible; PresentationChanged?.Invoke(); }

    /// <summary>将 MewDock 工具窗格被用户关闭后的实际存在状态回写到 View 菜单的区域状态。</summary>
    internal void SynchronizeToolPaneVisibility(bool? sideBarVisible, bool? panelVisible)
    {
        var changed = false;
        if (sideBarVisible is { } sideBar && _sideBarVisible != sideBar)
        {
            _sideBarVisible = sideBar;
            changed = true;
        }

        if (panelVisible is { } panel && _panelVisible != panel)
        {
            _panelVisible = panel;
            changed = true;
        }

        if (changed)
        {
            PresentationChanged?.Invoke();
        }
    }

    /// <summary>在宿主窗口挂载后重新应用当前工作台展示状态。</summary>
    public void RefreshPresentation() => PresentationChanged?.Invoke();

    /// <summary>
    /// 选择活动栏上下文。选择当前项时切换侧边栏显隐；选择其他项时显示其唯一对应的侧边栏视图。
    /// </summary>
    public void SelectActivity(string id)
    {
        EnsureActivityContext(id);

        if (_activeActivityId == id)
        {
            _sideBarVisible = !_sideBarVisible;
        }
        else
        {
            _activeActivityId = id;
            _sideBarVisible = true;
        }

        PresentationChanged?.Invoke();
    }

    public WorkbenchThemeContext ThemeContext => _theme;

    public Workbench Theme(Action<WorkbenchThemeContext> configure)
    {
        configure(_theme);
        return this;
    }

    public Workbench ActivityBar(Action<ActivityBar> configure)
    {
        configure(_activityBar);
        return this;
    }

    public Workbench SideBar(Action<SideBar> configure)
    {
        configure(_sideBar);
        return this;
    }

    public Workbench EditorArea(Action<EditorArea> configure)
    {
        configure(_editorArea);
        return this;
    }

    public Workbench Panel(Action<BottomPanel> configure)
    {
        configure(_panel);
        return this;
    }

    public Workbench StatusBar(Action<StatusBar> configure)
    {
        configure(_statusBar);
        return this;
    }

    public UIElement Build()
    {
        ValidateActivityContexts();
        _activeActivityId ??= _activityBar.Items.FirstOrDefault()?.Id;

        var view = new WorkbenchView(this);
        var result = view.Build();
        _docking = view.Docking;
        _view = view;
        return result;
    }

    /// <summary>
    /// 激活(必要时先创建)编辑器区指定文档标签,供运行时按需打开文档(如设置页)。
    /// </summary>
    public void OpenDocument(string id)
    {
        if (_docking is not { } docking)
        {
            return;
        }

        var pane = docking.DocumentPanes.FirstOrDefault(p => p.Component == id);
        if (pane is not null)
        {
            pane.Activate();
            return;
        }

        // 持久化布局可能不含新文档(如升级引入的设置页),按注册信息补建后激活。
        // 显式内容必须与 ContentFactory 解析到同一实例(EditorPaneContent),否则 MewDock
        // SyncContent 会在显式内容与 factory 内容实例不一致时分离内容且无法重新挂接,导致 tab 空白。
        var document = _editorArea.Documents.FirstOrDefault(d => d.Id == id);
        if (document is not null)
        {
            docking.AddDocumentPane(document.Title, _view!.EditorPaneContent(id), document.Id).Activate();
        }
    }

    /// <summary>更新编辑器文档的标签标题(如详情文档随当前对象变化);已打开的标签即时生效,未打开时仅记录校验。</summary>
    public void SetDocumentTitle(string id, string title)
    {
        if (_editorArea.Documents.All(document => document.Id != id))
        {
            throw new ArgumentException($"不存在编辑器文档“{id}”。", nameof(id));
        }

        if (_docking is { } docking)
        {
            var pane = docking.DocumentPanes.FirstOrDefault(p => p.Component == id);
            if (pane is not null)
            {
                pane.Title = title;
            }
        }
    }

    /// <summary>设置状态栏项的文本颜色(如启动失败红色醒目);null 恢复区前景色。已打开的状态栏即时生效。</summary>
    public void SetStatusTextColor(string id, Color? color)
    {
        if (_statusBar.Items.All(item => item.Id != id))
        {
            throw new ArgumentException($"不存在状态栏项“{id}”。", nameof(id));
        }

        _view?.SetStatusTextColor(id, color);
    }

    /// <summary>为编辑器文档声明显式的侧边栏定位行为。</summary>
    public void SetDocumentReveal(string documentId, string activityId, Action selectObject)
    {
        EnsureActivityContext(activityId);
        if (_editorArea.Documents.All(document => document.Id != documentId))
        {
            throw new ArgumentException($"不存在编辑器文档“{documentId}”。", nameof(documentId));
        }

        _documentReveals[documentId] = new DocumentReveal(activityId, selectObject);
    }

    internal ActivityBar ActivityBarModel => _activityBar;

    internal SideBar SideBarModel => _sideBar;

    internal EditorArea EditorAreaModel => _editorArea;

    internal BottomPanel PanelModel => _panel;

    internal StatusBar StatusBarModel => _statusBar;

    internal SideBarView? ActiveSideBarView => _activeActivityId is null
        ? null
        : _sideBar.Views.Single(view => view.Id == _activeActivityId);

    internal bool CanRevealDocument(string? id) => id is not null && _documentReveals.ContainsKey(id);

    /// <summary>显式定位已注册的编辑器文档；成功时显示其所属活动栏上下文。</summary>
    public bool RevealDocument(string id)
    {
        if (!_documentReveals.TryGetValue(id, out var reveal))
        {
            return false;
        }

        _activeActivityId = reveal.ActivityId;
        _sideBarVisible = true;
        PresentationChanged?.Invoke();
        reveal.SelectObject();
        return true;
    }

    internal void RestorePresentation(WorkbenchPresentationState state)
    {
        _activeActivityId = _activityBar.Items.Any(item => item.Id == state.ActiveActivityId)
            ? state.ActiveActivityId
            : _activityBar.Items.FirstOrDefault()?.Id;
        // 旧 presentation.json 仅记录侧边栏;缺失的新字段必须保持历史默认值「显示」。
        _activityBarVisible = state.IsActivityBarVisible ?? true;
        _sideBarVisible = state.IsSideBarVisible ?? true;
        _panelVisible = state.IsPanelVisible ?? true;
        _statusBarVisible = state.IsStatusBarVisible ?? true;
    }

    private void ValidateActivityContexts()
    {
        var activityIds = _activityBar.Items.Select(item => item.Id).ToList();
        var sideBarIds = _sideBar.Views.Select(view => view.Id).ToList();
        var dockComponentIds = sideBarIds
            .Concat(_editorArea.Documents.Select(document => document.Id))
            .Concat(_panel.Views.Select(view => view.Id))
            .ToList();

        if (activityIds.Count != activityIds.Distinct(StringComparer.Ordinal).Count()
            || sideBarIds.Count != sideBarIds.Distinct(StringComparer.Ordinal).Count()
            || !activityIds.Order(StringComparer.Ordinal).SequenceEqual(sideBarIds.Order(StringComparer.Ordinal)))
        {
            throw new InvalidOperationException("活动栏项必须与侧边栏视图按唯一 ID 一对一配对。");
        }

        if (dockComponentIds.Count != dockComponentIds.Distinct(StringComparer.Ordinal).Count())
        {
            throw new InvalidOperationException("侧边栏、编辑器区与底部面板的停靠组件 ID 必须全局唯一。");
        }
    }

    private void EnsureActivityContext(string id)
    {
        if (_activityBar.Items.All(item => item.Id != id)
            || _sideBar.Views.All(view => view.Id != id))
        {
            throw new ArgumentException($"不存在活动栏上下文“{id}”。", nameof(id));
        }
    }

    private sealed record DocumentReveal(string ActivityId, Action SelectObject);
}

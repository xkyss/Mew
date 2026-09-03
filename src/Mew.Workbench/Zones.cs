using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace Mew.Workbench;

/// <summary>
/// Declares the icons shown in the activity bar.
/// </summary>
public sealed class ActivityBar
{
    private readonly List<ActivityBarItem> _items = [];

    public IReadOnlyList<ActivityBarItem> Items => _items;

    public ActivityBar Item(string id, string title, GlyphKind glyph)
    {
        _items.Add(new ActivityBarItem(id, title, glyph));
        return this;
    }

    /// <summary>注册自定义图标的活动栏项(内置 GlyphKind 无合适图标时,如设置齿轮)。</summary>
    public ActivityBar Item(string id, string title, UIElement glyph)
    {
        _items.Add(new ActivityBarItem(id, title, GlyphKind.Hamburger, glyph));
        return this;
    }
}

public sealed record ActivityBarItem(
    string Id, string Title, GlyphKind Glyph, UIElement? CustomGlyph = null);

/// <summary>
/// Declares the tool views shown in the side bar.
/// </summary>
public sealed class SideBar
{
    private readonly List<SideBarView> _views = [];

    public IReadOnlyList<SideBarView> Views => _views;

    public SideBar View(string id, string title, UIElement content)
    {
        _views.Add(new SideBarView(id, title, content));
        return this;
    }
}

public sealed record SideBarView(string Id, string Title, UIElement Content);

/// <summary>
/// Declares the document tabs shown in the editor area.
/// </summary>
public sealed class EditorArea
{
    private readonly List<EditorDocument> _documents = [];

    public IReadOnlyList<EditorDocument> Documents => _documents;

    public EditorArea Document(string id, string title, UIElement content)
    {
        _documents.Add(new EditorDocument(id, title, content));
        return this;
    }
}

public sealed record EditorDocument(string Id, string Title, UIElement Content);

/// <summary>
/// Declares the output-style views shown in the bottom panel.
/// </summary>
public sealed class BottomPanel
{
    private readonly List<PanelView> _views = [];

    public IReadOnlyList<PanelView> Views => _views;

    public BottomPanel View(string id, string title, UIElement content)
    {
        _views.Add(new PanelView(id, title, content));
        return this;
    }
}

public sealed record PanelView(string Id, string Title, UIElement Content);

/// <summary>
/// Declares the context items shown in the status bar.
/// </summary>
public sealed class StatusBar
{
    private readonly List<StatusBarItem> _items = [];

    public IReadOnlyList<StatusBarItem> Items => _items;

    public StatusBar Item(string id, string text)
    {
        _items.Add(new StatusBarItem(id, new ObservableValue<string>(text)));
        return this;
    }

    /// <summary>
    /// 注册可实时更新的状态项:持有 ObservableValue 引用即可在运行时改文本。
    /// </summary>
    public StatusBar Item(string id, ObservableValue<string> text)
    {
        _items.Add(new StatusBarItem(id, text));
        return this;
    }
}

public sealed record StatusBarItem(string Id, ObservableValue<string> Text);

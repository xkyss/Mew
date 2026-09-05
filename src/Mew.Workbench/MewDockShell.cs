using System.Reflection;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.MewDock;

namespace Mew.Workbench;

/// <summary>
/// MewDock 适配层（ADR-000205）：对 MewDock internal/私有成员与字符串表的全部触碰集中于此，
/// 使 MewUI 升级评审的排查面收缩为一个文件。边界判定线：触碰 MewDock 内部的进本类；
/// 协调 Workbench 模型（布局恢复/区域显隐/内容实例缓存）的留 WorkbenchView。
/// </summary>
public sealed class MewDockShell
{
    private readonly DockingManager _docking;
    // 已接线「悬浮显示关闭按钮」的 tab 实例:布局变更重扫时去重,避免重复订阅鼠标事件。
    private readonly HashSet<object> _configuredTabClose = [];
    // 停靠区边框覆盖样式按 StyleSheet.Define 追加注册(GetByType 从后往前匹配),只允许注册一次。
    private bool _zoneStylesApplied;

    /// <summary>已接线关闭悬浮的 tab 数（测试缝：断言 Tune 幂等不重复接线）。</summary>
    internal int ConfiguredTabCloseCount => _configuredTabClose.Count;

    /// <summary>边框覆盖样式是否已成功注册（测试缝：子树未就绪时应为 false 并在后续 Tune 重试）。</summary>
    internal bool ZoneStylesApplied => _zoneStylesApplied;

    public MewDockShell(DockingManager docking) => _docking = docking;

    /// <summary>
    /// 重申 MewDock 行为调参。幂等契约（ADR-000205）：可安全反复调用——每次 docking.Changed
    /// 或构建完成后调用,重复调用不重复接线、不重复注册样式。覆盖：停靠区边框覆盖样式（仅首次）、
    /// TabSet 最大化禁用、细分隔条尺寸、最大化按钮视图层隐藏、tab 关闭按钮悬浮接线、分隔条光标。
    /// </summary>
    public void Tune()
    {
        // 子树未就绪(空布局/全关)时注册不了,守卫不置位,首个 Changed 重试——与票据承诺一致
        if (!_zoneStylesApplied && DisableDockZoneBorders())
        {
            _zoneStylesApplied = true;
        }
        DisableTabSetMaximize();
        ThinDockSplitters();
        HideMaximizeButtons();
        ConfigureTabCloseHover();
        WireSplitterCursors();
    }

    /// <summary>MewDock 界面文案中文化 + 空标题真正留白（空标题 tab 不显示 [Unnamed Tab]）。须在首个 DockingManager 构造前调用。</summary>
    public static void Localize()
    {
        MewUIDockString.TitleUnnamedTab.Value = "";
        MewUIDockString.MenuFloat.Value = "浮动";
        MewUIDockString.MenuAutoHide.Value = "自动隐藏";
        MewUIDockString.MenuClose.Value = "关闭";
        MewUIDockString.MenuCloseOthers.Value = "关闭其他";
        MewUIDockString.MenuCloseAll.Value = "关闭全部";
        MewUIDockString.MenuNewVerticalTabGroup.Value = "新建垂直选项卡组";
        MewUIDockString.MenuNewHorizontalTabGroup.Value = "新建水平选项卡组";
        MewUIDockString.MenuMoveToNextTabGroup.Value = "移到下一个选项卡组";
        MewUIDockString.MenuMoveToPreviousTabGroup.Value = "移到上一个选项卡组";
        MewUIDockString.MenuMaximize.Value = "最大化";
        MewUIDockString.MenuRestore.Value = "还原";
        MewUIDockString.MemuDock.Value = "停靠";
        MewUIDockString.ToolTipClose.Value = "关闭";
        MewUIDockString.ToolTipAutoHide.Value = "自动隐藏";
        MewUIDockString.ToolTipDock.Value = "停靠";
        MewUIDockString.ToolTipMaximize.Value = "最大化";
        MewUIDockString.ToolTipRestore.Value = "还原";
        MewUIDockString.ToolTipHiddenTabs.Value = "隐藏的选项卡";
    }

    /// <summary>去掉分组菜单里与标题栏独立按钮同义的项（自动隐藏/关闭），保留浮动等无独立按钮的入口。</summary>
    public static void PruneGroupMenu(ContextMenu menu)
    {
        var redundant = RedundantGroupMenuTexts();
        var kept = PruneGroupMenuTexts(menu.Items.OfType<MenuItem>().Select(item => item.Text).ToList(), redundant);
        foreach (var entry in menu.Items.ToList())
        {
            if (entry is MenuItem item && !kept.Contains(item.Text))
            {
                menu.Items.Remove(entry);
            }
        }
    }

    /// <summary>分组菜单冗余文案的当前集合：取字符串表现值而非硬编码（单一事实来源，本地化改动不再两处同步）。</summary>
    public static IReadOnlySet<string> RedundantGroupMenuTexts() => new HashSet<string>(StringComparer.Ordinal)
    {
        MewUIDockString.MenuAutoHide.Value,
        MewUIDockString.MenuClose.Value,
    };

    /// <summary>修剪纯函数：给定菜单项文本与冗余集合，返回应保留的文本（测试缝）。</summary>
    public static IReadOnlyList<string> PruneGroupMenuTexts(IReadOnlyList<string> texts, IReadOnlySet<string> redundant)
        => texts.Where(text => !redundant.Contains(text)).ToList();

    private static readonly MethodInfo DefineStyleRule = typeof(StyleSheet)
        .GetMethods()
        .FirstOrDefault(m => m.Name == nameof(StyleSheet.Define)
            && m.IsGenericMethodDefinition
            && m.GetParameters() is [{ ParameterType: var p }] && p == typeof(Style))
        ?? throw new MissingMethodException(nameof(StyleSheet), nameof(StyleSheet.Define));

    /// <summary>DockingManager 不公开模型引用,按私有字段 _model 反射获取(首次布局前为 null)。</summary>
    private object? TryGetModel() => typeof(DockingManager)
        .GetField("_model", BindingFlags.NonPublic | BindingFlags.Instance)
        ?.GetValue(_docking);

    private UIElement? TryGetRoot() => _docking.Children.FirstOrDefault() as UIElement;

    /// <summary>
    /// MewDock 内置 DockStyles 给 tabset / 侧边栏 / Tab 按钮画边框(默认 ControlBorder,焦点时 ControlBorder→Accent 75% 混合)。
    /// 五个工作台区按设计不显示边框:FlexLayoutView 的 StyleSheet 按类型注册 rule 且 GetByType 从后往前匹配——
    /// 向其中追加覆盖 rule 即可关闭边框。目标控件类型在 MewDock 中是 internal,无法静态引用,故经反射按名解析类型。
    /// 子树未就绪(尚无带 StyleSheet 的根)时返回 false,守卫不置位、下次 Tune 重试。
    /// </summary>
    private bool DisableDockZoneBorders()
    {
        if (TryGetRoot() is not FrameworkElement { StyleSheet: { } sheet })
        {
            return false;
        }

        var assembly = typeof(DockingManager).Assembly;
        OverrideStyle(assembly, sheet, "Aprillz.MewUI.MewDock.Controls.FlexTabSetView", CreateBorderlessTabSetStyle);
        OverrideStyle(assembly, sheet, "Aprillz.MewUI.MewDock.Extended.ExtendedBorderBar", CreateBorderlessBorderBarStyle);
        OverrideStyle(assembly, sheet, "Aprillz.MewUI.MewDock.Controls.FlexTabButton", CreateBorderlessTabButtonStyle);
        OverrideStyle(assembly, sheet, "Aprillz.MewUI.MewDock.Controls.FlexSplitter", CreateThinSplitterStyle);
        return true;
    }

    /// <summary>
    /// 拖动分隔条(FlexSplitter)做到最细:常驻 grip 线去掉(平时不可见),悬停/拖动时仅显示
    /// 细的 accent 高亮;与 SplitterSize=1 配合,避免细尺寸下 grip 线(长度按宽度-8 计算)溢出。
    /// </summary>
    private static Style CreateThinSplitterStyle(Type type) => new(type)
    {
        Transitions = [Transition.Create(Control.BackgroundProperty, 200, t => t)],
        Setters =
        [
            Setter.Create(Control.BackgroundProperty, Color.Transparent),
            Setter.Create(Control.BorderBrushProperty, Color.Transparent),
        ],
        Triggers =
        [
            new StateTrigger
            {
                Match = VisualStateFlags.Hot,
                Setters = [Setter.Create(Control.BackgroundProperty, t => t.Palette.Accent.WithAlpha(26))],
            },
            new StateTrigger
            {
                Match = VisualStateFlags.Pressed,
                Setters = [Setter.Create(Control.BackgroundProperty, t => t.Palette.Accent.WithAlpha(48))],
            },
        ],
    };

    /// <summary>
    /// 编辑器区/侧边栏/底部面板的 tabset:无边框。BorderThickness 置 0 后 FlexTabSetView 的 body
    /// 只画背景不画边框;圆角一并清零,避免 body 背景与相邻区之间出现缺角。
    /// </summary>
    private static Style CreateBorderlessTabSetStyle(Type type) => new(type)
    {
        Setters =
        [
            Setter.Create(Control.BackgroundProperty, t => t.Palette.ContainerBackground),
            Setter.Create(Control.BorderBrushProperty, Color.Transparent),
            Setter.Create(Control.CornerRadiusProperty, 0.0),
            Setter.Create(Control.BorderThicknessProperty, 0.0),
        ],
    };

    /// <summary>
    /// 自动隐藏边缘条(ExtendedBorderBar)的折叠面板:其边框在 OnRender 里硬编码取
    /// Theme.Metrics.ControlBorderThickness,无法用 BorderThickness 关闭——把 BorderBrush 设为
    /// 透明即可让 DrawBackgroundAndBorder 跳过边框绘制(背景仍按原样填充)。
    /// </summary>
    private static Style CreateBorderlessBorderBarStyle(Type type) => new(type)
    {
        Setters = [Setter.Create(Control.BorderBrushProperty, Color.Transparent)],
    };

    /// <summary>
    /// Tab 按钮(FlexTabButton):不显示边框。未选中项背景与 tab 栏一致(ContainerBackground),
    /// 仅选中项用编辑器区背景(WindowBackground)区分,并与下方编辑内容连成一体。
    /// </summary>
    private static Style CreateBorderlessTabButtonStyle(Type type) => new(type)
    {
        Transitions = [Transition.Create(Control.BackgroundProperty, 200, t => t)],
        Setters =
        [
            Setter.Create(Control.BackgroundProperty, t => t.Palette.ContainerBackground),
            Setter.Create(Control.BorderBrushProperty, Color.Transparent),
            Setter.Create(TextElement.ForegroundProperty, t => t.Palette.WindowText),
            Setter.Create(Control.PaddingProperty, new Thickness(8.0, 2.0, 8.0, 2.0)),
            Setter.Create(Control.CornerRadiusProperty, t => t.Metrics.ControlCornerRadius),
            Setter.Create(Control.BorderThicknessProperty, 0.0),
        ],
        Triggers =
        [
            new StateTrigger
            {
                Match = VisualStateFlags.Hot,
                Setters = [Setter.Create(Control.BackgroundProperty, t => t.Palette.ButtonHoverBackground)],
            },
            new StateTrigger
            {
                Match = VisualStateFlags.Selected,
                Setters = [Setter.Create(Control.BackgroundProperty, t => t.Palette.WindowBackground)],
            },
        ],
    };

    private static void OverrideStyle(Assembly assembly, StyleSheet sheet, string typeName, Func<Type, Style> factory)
    {
        if (assembly.GetType(typeName) is not { } type)
        {
            return;
        }

        DefineStyleRule.MakeGenericMethod(type).Invoke(sheet, [factory(type)]);
    }

    /// <summary>
    /// 标签栏「最大化/恢复」按钮无实际效果(MewDock 最大化未完整接线),关闭模型级 TabSetEnableMaximize
    /// 使 TabSetNode.IsEnableMaximize / CanMaximize 为假,按钮不再渲染。DockingManager 不公开模型引用,
    /// 故按私有字段 _model 反射获取;模型在首次布局时才创建,故每次 Tune 重申。
    /// </summary>
    private void DisableTabSetMaximize()
    {
        if (TryGetModel() is not { } model) return;
        model.GetType()
            .GetProperty("TabSetEnableMaximize")
            ?.SetValue(model, false);
    }

    /// <summary>
    /// 侧边栏/编辑器区、编辑器区/底部面板之间的拖动分隔条做到最细:把 MewDock 模型的
    /// SplitterSize 设为 3(分隔条仅 3px,平时透明不可见,悬停/拖动时显示细高亮)。
    /// 模型经 DockingManager._model 反射获取,首次布局后的 arrange 即按新值收窄;
    /// 布局持久化会保存新值,后续启动直接生效。
    /// </summary>
    private void ThinDockSplitters()
    {
        if (TryGetModel() is not { } model) return;
        model.GetType()
            .GetProperty("SplitterSize")
            ?.SetValue(model, 3.0);
    }

    /// <summary>
    /// 拖动分隔条时鼠标样式按方向变为缩放指针(仿 VS Code):垂直分隔条(侧边栏/编辑器)→
    /// 左右缩放(SizeWE),水平分隔条(编辑器/底部面板)→ 上下缩放(SizeNS)。设置 UIElement.Cursor
    /// 后悬浮该分隔条即自动生效;新分隔条在布局变更重扫时按 IsColumnAxis 重新接线。
    /// </summary>
    private void WireSplitterCursors()
    {
        var assembly = typeof(DockingManager).Assembly;
        if (assembly.GetType("Aprillz.MewUI.MewDock.Controls.FlexSplitter") is not { } splitterType)
        {
            return;
        }

        var columnAxisProp = splitterType.GetProperty("IsColumnAxis");
        if (columnAxisProp is null || TryGetRoot() is not { } root)
        {
            return;
        }

        VisitDockElements(root, element =>
        {
            if (splitterType.IsInstanceOfType(element) && element is Control splitter)
            {
                splitter.Cursor = columnAxisProp.GetValue(element) is true
                    ? CursorType.SizeNS
                    : CursorType.SizeWE;
            }
        });
    }

    /// <summary>
    /// 视图层隐藏所有 tabset 的「最大化/恢复」按钮。按钮在 FlexTabSetView 构造时按模型 flag 创建,
    /// 而模型在首次布局(AddDocumentPane 路径)时才就绪,flag 时序不可控;直接隐藏视图的
    /// _maximizeButton 字段在所有场景下都可靠。
    /// </summary>
    private void HideMaximizeButtons()
    {
        var assembly = typeof(DockingManager).Assembly;
        if (assembly.GetType("Aprillz.MewUI.MewDock.Controls.FlexTabSetView") is not { } viewType)
        {
            return;
        }

        var buttonField = viewType.GetField("_maximizeButton", BindingFlags.NonPublic | BindingFlags.Instance);
        if (buttonField is null || TryGetRoot() is not Panel root)
        {
            return;
        }

        var stack = new Stack<Panel>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var panel = stack.Pop();
            foreach (var child in panel.Children)
            {
                if (viewType.IsInstanceOfType(child))
                {
                    if (buttonField.GetValue(child) is FrameworkElement { IsVisible: true } button)
                    {
                        button.IsVisible = false;
                    }
                }
                else if (child is Panel nested)
                {
                    stack.Push(nested);
                }
            }
        }
    }

    /// <summary>
    /// 编辑器 tab 的「×」关闭按钮仅鼠标悬浮时显示,且 tab 宽度不随悬浮/按钮显隐变化。
    /// 关闭按钮始终占据布局空间(IsVisible 保持 true),未悬浮时置透明并关闭命中测试;
    /// 悬浮 tab 时恢复显示。FlexTabButton 与 _closeButton 在 MewDock 中为 internal/private,
    /// 故经反射遍历停靠区视图树接线;运行时新建 tab 时 Tune 会重新执行,
    /// 已接线实例用 _configuredTabClose 去重。仅文档 tab 有 _closeButton,工具 tab 自动跳过。
    /// </summary>
    private void ConfigureTabCloseHover()
    {
        var assembly = typeof(DockingManager).Assembly;
        if (assembly.GetType("Aprillz.MewUI.MewDock.Controls.FlexTabButton") is not { } tabType)
        {
            return;
        }

        var closeField = tabType.GetField("_closeButton", BindingFlags.NonPublic | BindingFlags.Instance);
        if (closeField is null || TryGetRoot() is not { } root)
        {
            return;
        }

        VisitDockElements(root, element =>
        {
            if (tabType.IsInstanceOfType(element))
            {
                WireTabCloseHover(element, closeField);
            }
        });
    }

    /// <summary>深度优先遍历停靠区视图树(tabset 等 Control 仅实现 IVisualTreeHost,不属 Panel)。</summary>
    private static void VisitDockElements(Element element, Action<UIElement> visit)
    {
        if (element is UIElement uiElement)
        {
            visit(uiElement);
        }

        if (element is Panel panel)
        {
            foreach (var child in panel.Children)
            {
                VisitDockElements(child, visit);
            }
        }
        else if (element is IVisualTreeHost host)
        {
            host.VisitChildren(child =>
            {
                VisitDockElements(child, visit);
                return true;
            });
        }
    }

    private void WireTabCloseHover(UIElement tab, FieldInfo closeField)
    {
        if (!_configuredTabClose.Add(tab))
        {
            return;
        }

        if (closeField.GetValue(tab) is not Button closeButton)
        {
            return;
        }

        // 关闭按钮(16px)始终占据布局空间,保证 tab 宽度不随悬浮/按钮显隐变化;
        // 未悬浮时隐藏「×」内容并关闭命中测试,悬浮 tab 时恢复。
        var glyph = closeButton.Content as UIElement;
        if (glyph is not null)
        {
            glyph.IsVisible = false;
        }
        closeButton.IsHitTestVisible = false;
        tab.MouseEnter += () =>
        {
            if (glyph is not null)
            {
                glyph.IsVisible = true;
            }
            closeButton.IsHitTestVisible = true;
        };
        tab.MouseLeave += () =>
        {
            if (glyph is not null)
            {
                glyph.IsVisible = false;
            }
            closeButton.IsHitTestVisible = false;
        };
    }
}

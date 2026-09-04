using Mew.Workbench.Plugins;
using Xunit;

namespace Mew.Host.Tests;

/// <summary>
/// 插件行状态推导（票据 01）：策略(enabled) × 健康态 × 崩溃集合 × 本会话实态 →（状态词、动作、副提示）。
/// 纯逻辑无窗口依赖；单动作原则：健康行启用/禁用、崩溃行重启、无法参与的插件不提供误导性开关。
/// </summary>
public class PluginRowStateTests
{
    // ---- 健康 × 策略 ----

    [Fact]
    public void Derive_健康且启用_已启用禁用动作()
    {
        var row = PluginRowState.Derive(enabled: true, PluginHealth.Healthy, crashed: false, stillLoaded: true);
        Assert.Equal(PluginRowStatus.Enabled, row.Status);
        Assert.Equal(PluginRowAction.Disable, row.Action);
        Assert.Equal("已启用", row.StatusWord);
        Assert.Equal("禁用", row.ActionLabel);
        Assert.Null(row.Hint);
        Assert.False(row.IsWarning);
    }

    [Fact]
    public void Derive_健康且禁用_未加载_已禁用启用动作无副提示()
    {
        var row = PluginRowState.Derive(enabled: false, PluginHealth.Healthy, crashed: false, stillLoaded: false);
        Assert.Equal(PluginRowStatus.Disabled, row.Status);
        Assert.Equal(PluginRowAction.Enable, row.Action);
        Assert.Equal("已禁用", row.StatusWord);
        Assert.Equal("启用", row.ActionLabel);
        Assert.Null(row.Hint);
    }

    [Fact]
    public void Derive_健康且禁用_本会话仍加载_副提示重启扩展主机后生效()
    {
        // T2 语义：禁用意图已落盘但本次会话 ALC 未卸 → 如实显示「已禁用」并附副提示，不谎称已停
        var row = PluginRowState.Derive(enabled: false, PluginHealth.Healthy, crashed: false, stillLoaded: true);
        Assert.Equal(PluginRowStatus.Disabled, row.Status);
        Assert.Equal(PluginRowAction.Enable, row.Action);
        Assert.Contains("重启扩展主机后生效", row.Hint);
    }

    // ---- 健康态覆盖策略词 ----

    [Theory]
    [InlineData(PluginHealth.InvalidManifest, PluginRowStatus.InvalidManifest, "清单错误")]
    [InlineData(PluginHealth.DuplicateId, PluginRowStatus.DuplicateId, "ID 重复")]
    [InlineData(PluginHealth.NeedsJit, PluginRowStatus.NeedsJit, "需 JIT 扩展主机")]
    public void Derive_健康态覆盖_不提供开关(PluginHealth health, PluginRowStatus status, string word)
    {
        // 无论 enabled 与否、是否仍加载，覆盖词一致且无动作
        var enabled = new PluginRowState(status, PluginRowAction.None, null);
        var row1 = PluginRowState.Derive(enabled: true, health, crashed: false, stillLoaded: true);
        var row2 = PluginRowState.Derive(enabled: false, health, crashed: false, stillLoaded: false);
        Assert.Equal(enabled, row1);
        Assert.Equal(enabled, row2);
        Assert.Equal(PluginRowAction.None, row1.Action);
        Assert.Equal(word, row1.StatusWord);
        Assert.Equal("", row1.ActionLabel);
        // 标红仅限清单错误/ID 重复（需 JIT 是置灰语义，非错误）；崩溃另行断言
        Assert.Equal(status is PluginRowStatus.InvalidManifest or PluginRowStatus.DuplicateId, row1.IsWarning);
    }

    // ---- 崩溃（仅 T3 语义下出现）----

    [Fact]
    public void Derive_已崩溃_动作变重启_非启用禁用()
    {
        var row = PluginRowState.Derive(enabled: true, PluginHealth.Healthy, crashed: true, stillLoaded: false);
        Assert.Equal(PluginRowStatus.Crashed, row.Status);
        Assert.Equal(PluginRowAction.Restart, row.Action);
        Assert.Equal("已崩溃", row.StatusWord);
        Assert.Equal("重启", row.ActionLabel);
        Assert.True(row.IsWarning);
        Assert.NotNull(row.Hint);
    }

    [Fact]
    public void Derive_已崩溃优先于健康态覆盖词()
    {
        // 崩溃先于一切覆盖；清单错误等健康词在崩溃后恢复才重新显形
        var row = PluginRowState.Derive(enabled: true, PluginHealth.InvalidManifest, crashed: true, stillLoaded: false);
        Assert.Equal(PluginRowStatus.Crashed, row.Status);
        Assert.Equal(PluginRowAction.Restart, row.Action);
    }

    [Fact]
    public void Derive_崩溃状态与加载无关()
    {
        var loaded = PluginRowState.Derive(enabled: false, PluginHealth.Healthy, crashed: true, stillLoaded: true);
        var unloaded = PluginRowState.Derive(enabled: false, PluginHealth.Healthy, crashed: true, stillLoaded: false);
        Assert.Equal(loaded, unloaded);
    }
}

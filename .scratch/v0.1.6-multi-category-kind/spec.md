# v0.1.6 启动项多分类与类型维度规格

Status: ready-for-agent

> 版本:v0.1.6
> 对应 ADR:`docs/adr/000106-01-launch-item-multi-category.md`、`docs/adr/000106-02-derived-item-kind.md`

## Problem Statement

启动项只能归属单一分类,但现实中一个启动项常同时属于多个组织(一个游戏既属「游戏」又属「Steam」),单归属迫使复制启动项或选边站;同时「URL / 程序」这类启动项类型差异没有展示维度,列表无法区分显示。最初考虑把「分类」改名为「标签」,经论证分类树保留、改为树内多挂,类型作为推导维度补充。

## Solution

启动项归属从单分类改为多分类(`CategoryIds` slug id 数组,空数组 = 未分类):侧边栏树中启动项出现在其所属每个节点下,父分类子树聚合不变;删除分类改为摘除归属、永不删项。新增「启动类型」推导维度(URL / 程序,由命令推导、不落盘),卡片/列表显示类型徽标,列表工具行提供类型过滤。

## User Stories

1. As a Launcher 用户, I want 一个启动项同时挂在多个分类下, so that 一个游戏既能在「游戏」又能在「Steam」下找到。
2. As a Launcher 用户, I want 详情表单分类字段支持多选, so that 归属可自由组合。
3. As a Launcher 用户, I want 删除分类时不删除启动项, so that 多归属下删除一个分类不会误伤还属于其他分类的项。
4. As a Launcher 用户, I want 「未分类」= 未挂任何分类的项, so that 语义与多归属一致。
5. As a Launcher 用户, I want 列表区分显示「URL / 程序」类型并可过滤, so that 可以只看程序或只看网页。

## Implementation Decisions

- **数据模型**:`LauncherItem.CategoryId: string?` → `CategoryIds: string[]`;手写 JSON 缺省 null 归一为空数组(沿用 Children 模式)。
- **迁移**:旧单值 `categoryId` → `[id]`,null → `[]`;v0.1.3 旧平铺格式迁移链不变。
- **聚合语义**:父分类 = 子树聚合(含子孙);「全部」= 所有项;「未分类」= 空数组项。均不变。
- **删除分类**:摘除归属,永不删项;子分类整棵子树一并摘除;项一个归属不剩 → 未分类;确认框文案「N 个启动项将失去分类 X」。
- **表单**:平铺多选(checkbox + 路径前缀,复用 `FlattenCategoryOptions`);「未分类」为隐式全不选状态;勾父不自动勾子。
- **新建归属**:在分类节点下新建 → 初始数组含该节点;「全部」/「未分类」下新建 → 空数组。
- **「在侧边栏定位」**:定位到树序第一个所属节点。
- **启动类型**:由 Command 推导 URL / 程序两档(脚本 .bat/.cmd 并入程序),不落盘;卡片/列表类型徽标 + 列表工具行类型过滤下拉。
- **UI 命名**:仍叫「分类」(不改「标签」);多选是语义增强不是改名。

## Testing Decisions

- 数据层纯逻辑(迁移单值→数组、空数组归一、未分类聚合、多归属子树聚合、删除摘除)在 `tests/Mew.Launcher.Tests` 补 xunit 用例。
- 类型推导函数(URL/程序判定)纯逻辑,补用例。
- 手工冒烟:多归属项在侧边栏多个节点下可见;删除分类后项保留、无归属项归未分类;表单多选与隐式未分类;类型徽标与过滤;「在侧边栏定位」定位到第一个节点。

## Out of Scope

- 搜索型启动项(查询模板)。
- 标签(独立扁平维度)不引入。
- 分类拖拽排序、节点图标等 v0.1.3 顺延项继续顺延。

## Further Notes

- 版本号常量 `AppVersion` 在 v0.1.5 发布后、v0.1.6 实现开始时提升为 `v0.1.6`(先例:单文件单行提交)。
- 实现票据按 issue-tracker 约定在实现启动时拆分为 `.scratch/v0.1.6-multi-category-kind/issues/NN-*.md`。

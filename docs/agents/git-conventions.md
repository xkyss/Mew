# Git Conventions

This repo follows Conventional Commits 1.0.0 for commit messages.

Reference: https://www.conventionalcommits.org/zh-hans/v1.0.0/

## Commit Message Format

Use this structure:

```text
<type>[optional scope][!]: <description>

[optional body]

[optional footer(s)]
```

Examples:

```text
feat(api): 增加销售申请审批接口
fix(web): 修复库存列表筛选条件丢失
docs(agents): 补充 Git 提交规范
refactor(domain)!: 拆分商品与库存模型
```

## Local Rules

- Commit messages must be written in Chinese.
- The `type`, optional `scope`, optional `!`, colon, and space must follow Conventional Commits syntax.
- Use a short imperative description after the colon.
- Add a blank line before the optional body.
- Add footers after a blank line when needed.
- Mark breaking changes with `!` before the colon or a `BREAKING CHANGE:` footer.

## Required Types

- `feat`: user-visible or domain-visible new functionality.
- `fix`: bug fixes.

## Allowed Common Types

- `docs`: documentation-only changes.
- `test`: tests.
- `refactor`: code restructuring without behavior changes.
- `perf`: performance improvements.
- `style`: formatting-only changes.
- `build`: build system or dependency changes.
- `ci`: continuous integration changes.
- `chore`: maintenance work that does not fit the other types.

## Scope Guidance

Use a scope when it clarifies the affected area. Prefer short, stable names such as:

- `api`
- `web`
- `domain`
- `db`
- `agents`
- `docs`
- `tests`

Do not invent a scope if the type and description are already clear.

## Breaking Changes

For breaking changes, use one of these forms:

```text
feat(api)!: 修改销售申请审批响应结构
```

or:

```text
feat(api): 修改销售申请审批响应结构

BREAKING CHANGE: 审批响应不再返回旧的 status 字段。
```

`BREAKING CHANGE` must stay uppercase when used as a footer token.

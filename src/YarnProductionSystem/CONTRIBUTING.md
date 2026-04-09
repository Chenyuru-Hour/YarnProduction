# 贡献指南（CONTRIBUTING）

本文档定义 `YarnProductionSystem` 仓库的统一开发与协作规范。  
所有提交者（含 AI 生成代码）都必须遵循本规范。

---

## 1. 适用范围

- 适用于本仓库所有项目（`.NET 8`、`Blazor`、`Worker Service` 等）。
- 适用于新增、修改、重构、修复、文档与测试代码。
- 本规范优先级高于个人习惯。

---

## 2. 基本原则

1. **可读性优先**：命名清晰、职责单一、结构明确。  
2. **可维护性优先**：避免重复逻辑，优先复用已有组件。  
3. **安全与稳定优先**：输入校验、异常处理、日志记录不可省略。  
4. **一致性优先**：遵守项目代码风格与目录组织。  
5. **最小改动原则**：仅修改与需求相关文件，避免无关变更。

---

## 3. C# 编码规范

- 目标框架：`.NET 8`
- 开启可空引用：`<Nullable>enable</Nullable>`
- 异步方法必须带 `Async` 后缀。
- 避免魔法值，使用常量、配置项或枚举。
- 公共 API 必须有 XML 文档注释。
- 私有方法可使用行内注释，但避免“注释解释代码本身”。

### 3.1 命名规范

- 类型：`PascalCase`（如 `ProductionOrderService`）
- 方法：`PascalCase`（如 `CalculateYieldAsync`）
- 局部变量/参数：`camelCase`（如 `batchNo`）
- 私有字段：`_camelCase`（如 `_logger`）
- 常量：`PascalCase`（如 `DefaultRetryCount`）

---

## 4. 【强制】方法注释与使用示例规范（中文）

> 所有新增 `public/protected/internal` 方法，必须补充**中文 XML 注释**，并包含“用途、参数、返回值、异常、示例”。

### 4.1 标准模板
```
/// <summary> /// 简要描述方法用途和业务语义。
/// </summary> 
/// <param name="paramName">参数说明、取值范围与单位（如有）。</param>
/// <returns>返回值说明。</returns> 
/// <exception cref="ArgumentNullException">何时抛出该异常。</exception> 
/// <example> 
/// <code> 
/// 
// 调用示例（应可直接运行或最小改造可运行） 
/// var result = await service.DoWorkAsync(input, cancellationToken); 
/// Console.WriteLine(result); 
/// </code> 
/// </example>
```
### 4.2 约束要求

1. 注释必须为中文，术语可中英并列（如“产量 Yield”）。  
2. `<example>` 必须包含**可执行调用示例**，禁止伪代码占位。  
3. 示例中若依赖上下文，需注明前置条件。  
4. 修改方法签名后，必须同步更新注释与示例。  
5. AI 生成代码时，默认按本节模板生成，不可省略。

---

## 5. Blazor UI 规范（微软风格）

UI 设计统一参考 **Microsoft Fluent Design**，要求：

1. **视觉风格**：简洁、层次清晰、留白合理、低视觉噪音。  
2. **组件优先**：优先使用统一组件库（如 Fluent UI/团队标准组件）。  
3. **交互一致性**：
   - 主按钮（Primary）全站语义一致；
   - 危险操作需二次确认；
   - 所有异步操作有加载态、成功态、失败态。  
4. **可访问性（A11y）**：
   - 表单控件有标签；
   - 支持键盘导航；
   - 对比度满足可读性要求。  
5. **响应式**：支持常见桌面分辨率，移动端保持核心可用。  
6. **文案**：中文优先，术语统一，错误提示可操作（告诉用户下一步做什么）。

---

## 6. Worker Service 规范

- 后台任务使用 `BackgroundService` 实现。
- `ExecuteAsync` 内必须支持 `CancellationToken`。
- 禁止无间隔空转循环；轮询必须有可配置延时。
- 异常统一记录日志，避免吞异常。
- 长耗时任务需有超时与重试策略（幂等前提下）。

---

## 7. 代码审查（Code Review）规则

所有 PR 必须至少 1 人通过审查后才能合并。

### 7.1 审查清单（必须逐项确认）

# 代码审查清单（src 全量）

## 阻断（Blocker）- 任一命中禁止合并
- [ ] `dotnet build -c Release` 失败
- [ ] `dotnet test -c Release` 失败
- [ ] 发现高危安全问题（SQL 注入、鉴权绕过、明文密钥/连接串）
- [ ] 公共/受保护/内部方法缺少中文 XML 注释（summary/param/returns/exception/example）
- [ ] 破坏性变更无迁移说明（接口/配置/数据结构）
- [ ] Worker 未正确响应 `CancellationToken` 或存在无间隔空转循环

## 高（High）- 必须在本次修复
- [ ] 空引用风险（违反 `<Nullable>enable</Nullable>` 约束）
- [ ] 异常被吞掉（`catch` 无日志、无处理策略）
- [ ] 异步误用（`async void`、缺少 `Async` 后缀、阻塞等待 `.Result/.Wait()`）
- [ ] 输入缺少校验（边界值、null、格式）
- [ ] Blazor 关键交互不符合规范（危险操作无二次确认、异步无加载/失败态）
- [ ] 关键路径缺少结构化日志（含上下文字段）

## 中（Medium）- 可合并但需创建修复项
- [ ] 重复代码（可提取复用）
- [ ] 魔法值未抽取为常量/配置/枚举
- [ ] 性能热点可优化（重复 IO、不必要分配、N+1）
- [ ] 测试覆盖不足（新增逻辑无测试、缺陷无回归测试）
- [ ] 文档不同步（行为变更未更新 README/SRS/配置说明）

## 低（Low）- 规范改进项
- [ ] 命名不一致（PascalCase/camelCase/_camelCase）
- [ ] 注释质量一般（解释“做了什么”而非“为什么”）
- [ ] UI 文案不统一（中文术语不一致、提示不可操作）
- [ ] 代码格式未统一（可由 format 自动修复）

## 审查结论
- [ ] 通过
- [ ] 有条件通过（需跟踪中/低问题）
- [ ] 拒绝合并（存在阻断/高问题）

## 问题记录（逐条）
- 严重级别：
- 文件路径：
- 行号：
- 问题描述：
- 修复建议：
- 是否必须本次修复：

---

## 8. 提交与分支规范

- 分支命名：`feature/xxx`、`fix/xxx`、`refactor/xxx`
- 提交信息建议使用：
  - `feat: ...`
  - `fix: ...`
  - `refactor: ...`
  - `docs: ...`
  - `test: ...`
  - `chore: ...`

示例：
```
feat: 新增纱线批次产量计算服务并补充中文方法示例 
fix: 修复生产看板在空数据下的空引用异常 
docs: 完善 CONTRIBUTING 中的代码审查与 UI 规范
```

---

## 9. 测试要求

- 新增业务逻辑必须补充测试。
- 修复缺陷必须有回归测试。
- 测试命名遵循：`MethodName_State_ExpectedResult`
- PR 前必须本地通过：
  - `dotnet build`
  - `dotnet test`

---

## 10. AI 生成代码附加要求

使用 AI（含 Copilot）生成代码时，提交前必须人工确认：

1. 是否符合本仓库命名与结构规范；  
2. 是否补齐中文注释与 `<example>`；  
3. 是否存在幻觉 API、过时 API 或不必要复杂度；  
4. 是否补充必要测试与文档；  
5. 是否满足 UI 风格一致性。

---


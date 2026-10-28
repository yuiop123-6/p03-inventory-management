# P03 Inventory API 契约 v1（成员A维护）

> 基路径 `/api`，JSON camelCase，UTF-8。时间一律 UTC ISO 8601（`2026-10-18T10:00:00Z`）。
> 本文件是 A/B/C/D 四端同步的契约基线；变更须全员同步并更新 Swagger 与前端 TS 类型。

## 通用响应结构

**列表（分页）**：`{ "items": [], "total": 0, "page": 1, "pageSize": 20 }`；`page` 从 1 起，`pageSize` 默认 20、最大 100。

**错误（所有非 2xx）**：`{ "code": "...", "message": "...", "details": null, "traceId": "..." }`
每个响应均带 `X-Trace-Id` 响应头，`traceId` 与之相同。

### 错误码总表

| code | HTTP | 场景 |
| --- | --- | --- |
| VALIDATION_ERROR | 400 | 参数/校验失败（含负值、缺分类、PUT 含禁改字段） |
| NOT_FOUND | 404 | 资源不存在 |
| DUPLICATE_VALUE | 409 | 编码/分类名重复（忽略大小写） |
| REFERENTIAL_CONFLICT | 409 | 删除仍被引用的记录（分类有商品；商品有流水或正库存） |
| INSUFFICIENT_STOCK | 400 | 出库量大于当前库存（B，E07） |
| STOCK_CONFLICT | 409 | 并发条件更新冲突（B，E09） |
| IDEMPOTENCY_CONFLICT | 409 | 相同 RequestId 不同负载（B，E13） |
| UNAUTHORIZED | 401 | 未登录/凭据无效 |
| FORBIDDEN | 403 | 已登录但无权限（Operator 写操作） |
| RATE_LIMITED | 429 | 登录限流（每 IP 每分钟 10 次） |
| INTERNAL_ERROR | 500 | 未预期异常（业务冲突不允许落 500） |

## 角色

| 角色 | 权限 |
| --- | --- |
| Admin | 全部：商品/分类写操作、ADJUST、删除、所有查询 |
| Operator | 只读 + IN/OUT；不能 ADJUST、不能删除/编辑商品分类（E14） |

## 接口清单（已实现 = A 交付）

### Auth
| 方法/路径 | 权限 | 说明 |
| --- | --- | --- |
| POST /api/auth/login | 匿名 | `{username,password}` → `{token, expiresAtUtc, user{id,userName,roles[]}}`；错凭据 401；限流 429 |
| GET /api/auth/me | 登录 | 当前用户 `{id,userName,roles[]}` |

### Categories（已实现）
| 方法/路径 | 权限 | 说明 |
| --- | --- | --- |
| GET /api/categories | 登录 | 按 Id 升序 → `{items,total,page,pageSize}` |
| POST /api/categories | Admin | `{name,description?}`；名称 Trim，忽略大小写重复 → 409 |
| PUT /api/categories/{id} | Admin | 更新名称/描述；404 / 409 |
| DELETE /api/categories/{id} | Admin | 有关联商品 → 409 REFERENTIAL_CONFLICT（E04） |

### Products（已实现）
| 方法/路径 | 权限 | 说明 |
| --- | --- | --- |
| GET /api/products | 登录 | `search`（编码/名称包含）、`categoryId`、`stockStatus=all\|normal\|low\|out`、`page`、`pageSize`；按 Id 升序稳定分页；item 含 `categoryName`、`stockStatus` |
| GET /api/products/{id} | 登录 | 详情（含 categoryName）；不存在 404 |
| POST /api/products | Admin | `productCode/name/categoryId/unitPrice/currentStock/minimumStockLevel` 必填；编号 Trim+转大写；重复 409（E02）；未知分类 400；初始库存为期初值不产生流水；负值 400（E11） |
| PUT /api/products/{id} | Admin | 仅允许 `name/description/categoryId/unitPrice/minimumStockLevel`；请求含 `productCode/currentStock/createdAt` → 400；404 |
| DELETE /api/products/{id} | Admin | 有流水或正库存 → 409；其余删除（无流水且库存=0） |

### Stock / Dashboard / Reports（成员B交付，占位约定）
| 方法/路径 | 权限 | 契约要点 |
| --- | --- | --- |
| POST /api/stock-transactions | Operator(IN/OUT)、Admin(IN/OUT/ADJUST) | `requestId`(UUID,必填)/productId/type/quantity/notes?；201 返回交易实体含 previousStock/newStock/transactionReference；重放同 RequestId 同负载返回首次结果；不同负载 409 IDEMPOTENCY_CONFLICT；不足 400 INSUFFICIENT_STOCK |
| GET /api/stock-transactions | 登录 | `productId,type,fromUtc(含),toUtc(不含),page,pageSize`；CreatedAt DESC, Id DESC |
| GET /api/dashboard | 登录 | 4 指标 + 最近 10 条流水 + 过去 7 个 UTC 自然日净变更（零填充） |
| GET /api/reports/products | 登录 | 同商品查询口径 + 库存金额 unitPrice × currentStock |
| GET /api/reports/transactions | 登录 | 同流水过滤分页，默认最新优先 |

## 领域口径（与验收清单一致）

- 缺货：`stock == 0`；低库存：`0 < stock <= minimumStockLevel`（等于阈值属低库存）；正常：`stock > minimumStockLevel`；三者互斥不重复计数（E15）。
- IN/OUT：正整数变动量；ADJUST：目标绝对库存（允许 0，仅 Admin）。
- 库存变更与流水同事务提交；失败不留任何变化（E05-E08）。
- 分类名唯一性在数据库层强制（持久化计算列 `UPPER(LTRIM(RTRIM(Name)))` + 唯一索引）；商品编号唯一且不可编辑。

## 数据库

- SQL Server LocalDB：`(localdb)\MSSQLLocalDB`，库名 `P03Inventory`；连接串在 `backend/appsettings.json`。
- 迁移仅由成员A生成：`dotnet ef migrations add ...`；其他人只执行 `dotnet ef database update`。
- 种子导入：`dotnet run -- --seed-import`（幂等；有流水时拒绝；完成后输出 9/129/4/2 基线对账）。

## 本地启动

```bash
cd backend
dotnet ef database update
dotnet run -- --seed-import   # 首次部署执行
dotnet run                    # http://localhost:5000，Swagger: /swagger
```

默认账号：`admin / Admin@123!`、`operator / Operator@123!`（首次部署后必须修改）。

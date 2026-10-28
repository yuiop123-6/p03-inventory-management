# P03 企业库存管理与数据可视化后台 v2.0

ASP.NET Core 8 Web API + EF Core + SQL Server（LocalDB）后端，Angular 20 前端，
实现商品/分类管理、并发安全的库存事务、幂等提交、仪表盘数据可视化与报表。

## 技术栈

| 层 | 技术 |
| --- | --- |
| 后端 | .NET 8（LTS）、ASP.NET Core Web API、EF Core 8（SQL Server）、ASP.NET Core Identity + JWT Bearer、Swashbuckle Swagger、内置 RateLimiter（登录限流） |
| 前端 | Angular 20（standalone、Reactive Forms、Router）、chart.js、原生 CSS（无 UI 框架）、英文界面 |
| 数据库 | SQL Server / SQL Express LocalDB，EF Core Code-First 迁移 |
| 测试 | Python 标准库冒烟/并发脚本、Postman 集合 |

## 核心特性

- **统一 API 契约**：camelCase JSON；分页 `{items,total,page,pageSize}`；错误 `{code,message,details,traceId}`；错误码见 [docs/api-contract.md](docs/api-contract.md)，业务冲突一律 4xx 不落 500
- **库存规则**：IN/OUT 正整数变动量；ADJUST 目标绝对值（允许 0，仅 Admin）；库存变更与流水写入同事务提交
- **并发安全**：库存扣减使用条件原子 UPDATE（`WHERE CurrentStock >= @qty`），并发不超卖（E09：10 并发恰 5 成功 5 拒绝）
- **幂等提交**：RequestId（UUID）唯一约束 + 负载哈希；重放 200 返回首次结果，负载不一致 409 `IDEMPOTENCY_CONFLICT`
- **数据约束**：分类名忽略大小写唯一（持久化计算列 + 唯一索引）；商品编号唯一且不可编辑；有流水或正库存的商品禁止删除
- **认证**：Identity 密码哈希（不明文）、JWT（480 分钟）、登录每 IP 每分钟 10 次限流；Admin/Operator 双角色
- **仪表盘**：4 项指标 + 最近 10 条流水 + 过去 7 个 UTC 自然日净变更柱状图（零填充）
- **种子导入**：幂等（重复导入不产生重复数据）、有流水时拒绝导入、完成后输出基线对账（9/129/4/2）

## 目录结构

```
backend/                 # ASP.NET Core 8 分层后端
  Controllers/           # API 控制器（Auth/Products/Categories/StockTransactions/Dashboard/Reports）
  Services/              # 业务逻辑（StockService 含事务/并发/幂等核心）
  Data/                  # AppDbContext、实体、EF 迁移
  Common/                # 统一错误处理中间件、错误码、分页
  Dtos/                  # 请求/响应 DTO
frontend/                # Angular 20 前端
  src/app/core/          # 模型、AuthService、JWT 拦截器、守卫、领域 API 服务
  src/app/features/      # login / dashboard / products / categories / stock / reports
seed-data/               # 种子数据与期望基线
tests/                   # 冒烟脚本、并发复现脚本、Postman 集合
docs/                    # 部署 / API 契约 / 验收记录 / 已知问题
```

## 快速开始

前置：.NET 8 SDK、SQL Server（或 LocalDB）、Node.js 20.19+。

```bash
# 1) 数据库初始化 + 种子导入（基线 9/129/4/2，默认账号 admin / operator）
cd backend
dotnet ef database update
dotnet run -- --seed-import

# 2) 启动后端（http://localhost:5000，Swagger: /swagger）
dotnet run

# 3) 启动前端（http://localhost:4200，/api 代理到 5000）
cd ../frontend
npm install
npm start
```

默认账号：`admin / Admin@123!`（Admin）、`operator / Operator@123!`（Operator）。**部署后立即修改。**

## 验收测试

```bash
python tests/api_smoke_e01_e17.py     # API 冒烟 19/19（需干净基线库，重置方法见脚本头注释）
python tests/e09_e13_concurrency.py   # E09 并发超卖 / E13 并发幂等 12/12（独立测试库，一键复现）
```

浏览器验证：登录 → Dashboard 核对 9/129/4/2 与 7 日图 → Products/Categories CRUD →
Stock 提交 IN/OUT/ADJUST → Reports 核对金额与流水 → Operator 登录确认写操作全部隐藏。

详细记录见 [docs/acceptance-records.md](docs/acceptance-records.md)，部署细节见
[docs/deployment.md](docs/deployment.md)，已知问题见 [docs/known-issues.md](docs/known-issues.md)。

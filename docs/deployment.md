# P03 部署说明

## 环境要求

| 组件 | 版本 | 用途 |
| --- | --- | --- |
| .NET SDK | 8.0+ | 后端 API + EF Core 迁移 |
| SQL Server LocalDB | 随 VS/SDK 安装 | 数据库（`(localdb)\MSSQLLocalDB`，库名 `P03Inventory`） |
| Node.js | 18+ | 前端 Angular 构建 |
| Python | 3.10+（可选） | 验收脚本 |

## 首次部署

```bash
# 1. 后端（backend/ 目录）
dotnet ef database update          # 建库（迁移已随仓库交付）
dotnet run -- --seed-import        # 幂等导入种子数据，输出 9/129/4/2 基线对账
dotnet run                         # http://localhost:5000，Swagger: /swagger
```

```bash
# 2. 前端（frontend/ 目录）
npm ci                             # 按锁定文件安装，勿手改大版本
npm start                          # http://localhost:4200，proxy /api→5000
```

默认账号（首次部署后必须修改）：

| 账号 | 密码 | 角色 |
| --- | --- | --- |
| admin | Admin@123! | Admin（全部权限） |
| operator | Operator@123! | Operator（只读 + IN/OUT） |

## 日常重置（恢复演示基线）

```bash
# 先停止运行中的后端，否则 drop 会因连接占用失败
cd backend
dotnet ef database drop --force
dotnet ef database update
dotnet run -- --seed-import
```

## 验收测试

```bash
python tests/api_smoke_e01_e17.py     # E01-E17，19 断言（需干净基线；会改数据）
python tests/e09_e13_concurrency.py   # E09/E13 并发，12 断言（自包含，任意状态可跑）
```

详细验收记录见 [acceptance-records.md](acceptance-records.md)。

## 关键配置

| 项 | 位置 | 说明 |
| --- | --- | --- |
| 连接串 | `backend/appsettings.json` | LocalDB 默认即可；禁止提交真实生产密钥 |
| 前端 API 地址 | `frontend/proxy.conf.json` | `/api` → `http://localhost:5000`，前端不写死主机 |
| 登录限流 | 后端内置 | 每 IP 每分钟 10 次，超限 429 RATE_LIMITED |

## 生产部署注意

1. 迁移仅由成员 A 生成；其他成员只执行 `dotnet ef database update`。
2. 敏感配置（连接串/密钥）走环境变量或密钥库，不要写进 appsettings 提交。
3. HTTPS：生产必须启用 HTTPS 终结点，并更新 CORS/代理配置。
4. 数据库备份：上线前全量备份；种子导入工具在有流水时会拒绝执行（保护生产数据）。

# P03 部署说明

> 目标：在一台**全新 Windows 机器**上，按本文从零把项目跑起来。任何一步失败先看第 6 节「常见安装问题」。

## 1. 环境要求（按顺序安装）

| # | 组件 | 说明 | 验证命令 |
| --- | --- | --- | --- |
| 1 | .NET SDK **8.0** | 仓库根目录有 `global.json` 锁定 8.x；装 8.0.400 或更高 8.x 版本 | `dotnet --version` 显示 8.0.x |
| 2 | **SQL Server Express LocalDB** | ⚠️ **装 .NET SDK 不自带 LocalDB**。它随 Visual Studio 或 SQL Server Express 安装：下载「SQL Server Express 安装程序」→ 安装时选 **Basic（基本）** 即包含 LocalDB；已装 VS 2022 的机器通常已有 | `sqllocaldb info` 列出 `MSSQLLocalDB` |
| 3 | Node.js 18+ | 前端构建 | `node -v` |
| 4 | Python 3.10+（可选） | 验收脚本 | `python --version` |

> 没有 LocalDB、只有完整 SQL Server/Express 实例？改 `backend/appsettings.json` 连接串：
> `"Server=localhost;Database=P03Inventory;Trusted_Connection=True;TrustServerCertificate=True"`（按实际实例名/账号调整）。本地演示无需改动。

## 2. 后端启动（backend/ 目录）

```bash
dotnet tool restore     # 安装 EF 工具（dotnet-ef，版本由 .config/dotnet-tools.json 锁定）
dotnet ef database update
dotnet run -- --seed-import   # 幂等导入种子，输出 9/129/4/2 基线对账
dotnet run                    # http://localhost:5000，Swagger: /swagger
```

默认账号（首次部署后必须修改，见第 5 节）：

| 账号 | 密码 | 角色 |
| --- | --- | --- |
| admin | Admin@123! | Admin（全部权限） |
| operator | Operator@123! | Operator（只读 + IN/OUT） |

## 3. 前端启动（frontend/ 目录）

```bash
npm ci           # 按锁定文件安装，勿手改大版本
npm start        # http://localhost:4200，proxy /api→5000
```

## 4. 日常重置（恢复演示基线）

```bash
# 必须先停止运行中的后端进程，否则 drop 因数据库连接占用而失败
cd backend
dotnet ef database drop --force
dotnet ef database update
dotnet run -- --seed-import
```

## 5. 生产配置注意事项

1. **改默认密码**：admin/operator 密码在 `appsettings.json` SeedUsers 节点，或用环境变量覆盖；演示密码不得带入生产。
2. **JWT 密钥**：`Jwt:Key` 为空时仅适合本机演示。生产必须用环境变量 `Jwt__Key` 注入强随机密钥（≥32 字符），且改 `Jwt:DurationMinutes` 为较短时效。
3. **HTTPS**：生产必须启用 HTTPS 终结点（`Urls` 改 https 或前置反向代理），并同步更新前端代理/CORS。
4. **迁移纪律**：迁移仅由成员 A 生成；其他人只执行 `dotnet ef database update`。
5. **机密不入仓**：连接串/密钥一律走环境变量或密钥库；仓库内 appsettings.json 仅含演示配置。

## 6. 常见安装问题

| 现象 | 原因 | 处理 |
| --- | --- | --- |
| `dotnet ef: 无法执行，找不到命令` | 未安装 EF 工具 | `dotnet tool restore`（第 2 步第 1 条命令） |
| `dotnet --version` 显示 10.x/7.x 或报 global.json 不匹配 | SDK 版本不符 | 装 .NET 8 SDK（8.0.400+）；`global.json` 允许 8.x 内最新特性带 |
| 启动报数据库连接失败 / `sqllocaldb` 不是命令 | LocalDB 未安装 | 第 1 节第 2 行安装 SQL Server Express Basic |
| `dotnet ef database drop` 失败：数据库正在使用 | 后端还在运行 | 停掉后端进程再执行 |
| 种子导入拒绝执行 | 库中已有流水（保护机制） | 按第 4 节重置后再导入 |
| 登录连续失败后 429 | 登录限流（每 IP 每分钟 10 次） | 等 1 分钟再试 |

## 7. 数据备份与恢复

```bash
# 备份（命令行，Windows）
sqlcmd -S "(localdb)\MSSQLLocalDB" -E -Q "BACKUP DATABASE [P03Inventory] TO DISK='C:\backup\P03Inventory.bak' WITH INIT"

# 恢复
sqlcmd -S "(localdb)\MSSQLLocalDB" -E -Q "RESTORE DATABASE [P03Inventory] FROM DISK='C:\backup\P03Inventory.bak' WITH REPLACE"
```

也可用 Visual Studio 的「SQL Server 对象资源管理器」右键数据库 → 备份/恢复。重要节点（上线前、批量导入后）建议全量备份；`seed-import` 本身有流水保护，误导入可用恢复兜底。

## 8. 打包部署（dotnet publish）

```bash
# 后端发布（self-contained 可脱离 SDK 运行，framework-dependent 体积更小）
cd backend
dotnet publish -c Release -r win-x64 --self-contained false -o ../publish/api
# 服务器上运行（需装 ASP.NET Core Runtime 8.0）
dotnet publish/api/P03.Inventory.Api.dll

# 前端发布（静态文件，任意静态服务器/IIS/nginx 均可）
cd frontend
npm run build -- --configuration production
# 产物在 frontend/dist/，部署时将 API 地址指向后端（生产 apiUrl 已配置 /api，可用反向代理转发）
```

## 9. 验收测试

```bash
python tests/api_smoke_e01_e17.py      # E01-E17，19 断言（需干净基线；会改数据，测后按第 4 节重置）
python tests/e09_e13_concurrency.py    # E09/E13 并发，12 断言（主库自包含模式）
python tests/e09_e13_concurrency.py --isolated   # 独立重置测试库模式（验收清单 testIsolation 要求）
python tests/perf_check.py             # P95 性能抽测
```

详细验收记录见 [acceptance-records.md](acceptance-records.md)。

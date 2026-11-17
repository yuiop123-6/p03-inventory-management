# 新手环境搭建指南：从零跑起 P03 项目

> 面向第一次接触 .NET 项目的同学（队友 B/C/D 或新电脑）。照着做，每一步都有「验证方法」和「预期输出」。
> 技术版完整文档见 [deployment.md](deployment.md)，本指南是它的手把手版。
> 预计总耗时：30-45 分钟（大部分是等安装包下载）。

## 0. 我们要装的三样东西（先明白它们是什么）

| 组件 | 是什么 | 为什么需要 |
| --- | --- | --- |
| .NET 8 SDK | 微软的开发工具包，C# 后端代码靠它编译运行 | 后端项目是 .NET 8 写的 |
| SQL Server LocalDB | 一个「精简版数据库」，装在个人电脑上零配置可用 | 商品、库存数据存在这里 |
| EF 工具（dotnet-ef） | 一条命令行小工具 | 用它把「数据表结构」建到 LocalDB 里 |

> ⚠️ 三者关系：SDK ≠ LocalDB ≠ EF 工具，**互相都不附带**，需要分别安装。这就是最容易踩的坑。

## 1. 安装 .NET 8 SDK

1. 打开官网下载页：https://dotnet.microsoft.com/download/dotnet/8.0
2. 找到 **.NET SDK 8.0.xxx**（不要点 "Runtime"，要 **SDK**），下载 Windows x64 安装包
3. 双击安装，一路下一步

**验证**：打开终端（开始菜单搜 "PowerShell"），输入：

```powershell
dotnet --version
```

预期输出：`8.0.4xx`（8.0.400 或更高的 8.x 都行）。

> 如果显示 10.x 或 7.x：说明机器上装了别的版本。项目根目录的 `global.json` 已声明用 8.x，在 backend/ 目录下操作时会自动用对的版本，一般不用管。

## 2. 安装 SQL Server LocalDB

> ⚠️ **最大坑位预警**：装 .NET SDK **不会**自带 LocalDB；SQL Server Express 安装器选「**基本**」类型装的也是完整引擎，**同样不带 LocalDB**。必须按下面三种方式之一装。

**方式 A（推荐，最轻量）**：自定义安装勾选

1. 打开下载页：https://www.microsoft.com/en-us/download/sql-server-express
2. 下载并运行安装程序，出现三个选项时选 **Custom（自定义）**
3. 走到「功能选择 / Feature Selection」页面时，勾选 **SQL Server Express LocalDB**（其他都不用勾）
4. 完成安装

**方式 B**：如果你已装 Visual Studio 2022

1. 打开 **Visual Studio Installer**（开始菜单搜）
2. 点「修改」→「单个组件」页签
3. 搜索 "LocalDB"，勾选 **SQL Server Express LocalDB**，点「修改」等待安装

**方式 C**：下载介质直装

1. 同方式 A 的下载页，这次选 **Download media（下载介质）**
2. 选 **LocalDB** 包，下载后运行里面的 `SqlLocalDB.msi`

**验证**：终端输入：

```powershell
sqllocaldb info
```

预期输出：一行 `MSSQLLocalDB`。

> 如果提示「无法将 sqllocaldb 项识别为命令」→ LocalDB 没装上，回上面的方式 A 重装。
> 如果输出 `MSSQLLocalDB` 但后面标注 auto-create：正常，首次连接时会自动创建实例。

## 3. 配置 EF 工具（一行命令）

拿到项目代码后（`git clone` + `git checkout dev`），在 **backend 目录**下执行：

```powershell
cd backend
dotnet tool restore
```

预期输出：`已还原 ...\P03.Inventory.Tests\...` 或「工具 ... 已是最新的」。

> 这一步会读仓库里的 `backend/.config/dotnet-tools.json`（已锁好版本 8.0.32），自动安装 dotnet-ef。
> 「还原」= 按清单自动安装，跟 npm install 一个道理。以后换电脑、新队友入职，都是这一条命令。

**验证**：

```powershell
dotnet ef --version
```

预期输出：`8.0.32`（或类似）。

## 4. 启动项目

### 4.1 后端（在 backend/ 目录，按顺序 4 条命令）

```powershell
dotnet tool restore          # 第 3 步做过的可跳过
dotnet ef database update    # 建库建表（首次约 1-2 分钟）
dotnet run -- --seed-import  # 导入演示数据，看到 Baseline: 9 products / 129 stock / 4 low / 2 out 即成功
dotnet run                   # 启动服务
```

看到 `Now listening on: http://localhost:5000` 即启动成功。浏览器打开 http://localhost:5000/swagger 能看到接口文档页面。

登录账号：`admin / Admin@123!`（管理员）、`operator / Operator@123!`（操作员）。

### 4.2 前端（另开一个终端，frontend/ 目录）

```powershell
cd frontend
npm ci        # 首次需要，按锁定文件装依赖（Node.js 18+）
npm start     # 启动，浏览器打开 http://localhost:4200
```

## 5. 跑测试确认环境健康（可选但建议）

```powershell
# 在 backend/ 目录
dotnet test .\P03.Inventory.sln
```

预期输出末尾：`已通过! - 失败: 0，通过: 71，已跳过: 0，总计: 71`。

> 注意必须带 `.\P03.Inventory.sln`——backend 目录下有多个项目文件，不指定会报 MSB1011 或只跑部分项目。

## 6. 常见报错速查表（都是我们真实踩过的）

| 报错/现象 | 原因 | 解决 |
| --- | --- | --- |
| `dotnet ef: 无法执行，找不到命令` | EF 工具没装 | backend/ 下执行 `dotnet tool restore` |
| `sqllocaldb` 不是命令 | LocalDB 没装 | 按第 2 节方式 A 重装（记住：不是「基本」） |
| 启动报数据库连接失败 | LocalDB 未装或实例名不对 | `sqllocaldb info` 检查；不行就重装 LocalDB |
| `dotnet ef database drop` 报「数据库正在使用」 | 后端还在运行占着库 | 停掉后端（关掉那个终端窗口或 Ctrl+C）再执行 |
| build 报 MSB3027「文件被 P03.Inventory.Api 锁定」 | 后端正在运行，exe 被占用 | 先停后端再 build，这是正常现象不是代码错 |
| `MSB1011: 此文件夹中包含多个项目` | 没指定用哪个项目 | 命令带上 `.\P03.Inventory.sln` |
| 登录一直失败后返回 429 | 登录限流（每 IP 每分钟 10 次） | 等 1 分钟再试 |
| 种子导入被拒绝 | 库里已有数据（保护机制） | 先按顺序执行 drop → update → seed-import（见下） |

## 7. 恢复出厂演示数据（演示前建议重置）

测试会把数据搞乱，演示前恢复到最初的干净状态（**先停掉后端**）：

```powershell
cd backend
dotnet ef database drop --force
dotnet ef database update
dotnet run -- --seed-import
```

看到 `Baseline: 9 / 129 / 4 / 2` 对账输出即恢复成功。

---

有任何一步卡住，把**终端里的报错原文**发到群里，附上你执行的是哪条命令。

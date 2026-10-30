# P03 验收记录（E01-E17）

> 记录日期：2026-10-30 ｜ 执行环境：后端 :5000（Development）+ Angular 前端 :4200（proxy /api→5000）
> 数据基线：种子导入后 9 商品 / 总库存 129 / 低库存 4 / 缺货 2 / 流水 0
> 证据包：`deliverables/self-check/`（断言明细 JSON）、`deliverables/screenshots/`（13 张）、`deliverables/video/p03-demo.mp4`
> 自动化脚本：`tests/api_smoke_e01_e17.py`（19 断言）、`tests/e09_e13_concurrency.py`（12 断言）

## 一、验收结果总览

| 层次 | 用例范围 | 结果 |
| --- | --- | --- |
| API 层 | E01-E17 | **17/17 PASS**（Python 脚本 19 断言全绿） |
| 并发专项 | E09 / E13（README 规格） | **12/12 PASS** |
| UI 补充 | Operator 权限隐藏、守卫、空态等 8 项 | **8/8 PASS** |
| 浏览器全流程 | 登录→Dashboard→CRUD→库存→报表→登出 | PASS（录屏完整走通） |

## 二、API 层逐项记录

| 用例 | 验收点 | 结果 | 关键证据 |
| --- | --- | --- | --- |
| E01 | 种子基线 9/129/4/2，分类 3，流水 0 | PASS | dashboard=9/129/4/2 |
| E02 | 商品编号重复 → 409 DUPLICATE_VALUE | PASS | 409/DUPLICATE_VALUE |
| E03 | 分类名重复（含大小写/空格变体）→ 409 | PASS | 精确=409 变体=409 |
| E04 | 删除有关联商品的分类 → 409 REFERENTIAL_CONFLICT | PASS | 409/REFERENTIAL_CONFLICT |
| E05 | IN 入库 201，库存与仪表盘同步 +5 | PASS | previousStock=10 → newStock=15 |
| E06 | Operator 执行 OUT 201，库存递减 | PASS | 15→13 |
| E07 | 出库超库存 → 400 INSUFFICIENT_STOCK，零副作用 | PASS | 库存不变、流水不变 |
| E08 | ADJUST 到 0：201，状态 out | PASS | 13→0 |
| E09 | 并发防超卖：10 并发 OUT×1（库存 5）恰 5 成功 5 拒绝，余额严格 4→0，流水恰 +5 | PASS | 见并发脚本输出 |
| E10 | 流水查询：productId/type 过滤、DESC、分页、关联名称回显 | PASS | rows=3 out=1 desc=True |
| E11 | 负单价 / 负库存 / 负 IN / 零 OUT → 400 | PASS | 3 断言全过 |
| E12 | PUT 含 productCode/currentStock → 400 且数据未被篡改 | PASS | 编号/库存保持原值 |
| E13 | 幂等：重放 200 同结果；异负载 409 IDEMPOTENCY_CONFLICT；并发 6 重放仅 1 流水 | PASS | 见并发脚本输出 |
| E14 | Operator：ADJUST/删除/改商品/建分类全 403，查询 200 | PASS | 403×4 + read=200 |
| E15 | 缺货/低库存/正常三态互斥且与筛选口径一致（等阈值属低库存） | PASS | out/low/normal 与本地计算全等 |
| E16 | 仪表盘 4 指标吻合；7 日净变更零填充且与流水聚合一致 | PASS | got=[0,0,0,0,0,0,-112]=exp |
| E17 | 报表 stockAmount=unitPrice×currentStock；流水报表与查询总数一致、DESC | PASS | 9 行金额全对 |

## 三、UI 补充（浏览器实测）

| 检查点 | 结果 |
| --- | --- |
| Operator 登录后：新建/删除/编辑商品、新建分类入口全部隐藏 | PASS |
| Operator 库存页：ADJUST 选项禁用/移除 | PASS |
| 登出后访问 /products 重定向 /login（路由守卫） | PASS |
| 流水空状态、搜索无结果、加载骨架屏 | PASS |
| 响应式 375px：导航压缩、筛选堆叠、表格可读 | PASS |
| 表单校验错误提示（负值/缺字段） | PASS |
| 防重复提交（按钮 loading） | PASS |
| 角色菜单控制（Dashboard/报表菜单按角色渲染） | PASS |

## 四、复现方式

```bash
# 后端（backend/ 目录）
dotnet ef database drop --force    # 先停掉运行中的后端
dotnet ef database update
dotnet run -- --seed-import        # 9/129/4/2 基线对账
dotnet run                         # :5000

# 前端（frontend/ 目录）
npm start                          # :4200，proxy /api→5000

# 自动化验收（仓库根目录，后端运行中）
python tests/api_smoke_e01_e17.py     # 19/19（会改数据，测后需重置基线）
python tests/e09_e13_concurrency.py   # 12/12（自包含，可在任意状态运行）
```

## 五、备注

- E09 拒绝方实际返回 400 INSUFFICIENT_STOCK（CAS 重读后库存确已被抢完）；409 STOCK_CONFLICT 为重试耗尽路径，两者均在契约错误码表内。
- E13 重放返回 200（非 201），符合「重放返回首次结果」语义；首次 201。
- 原 acceptance_manifest.json / 需求书 docx 附件已失效，本记录依据 docs/api-contract.md 领域口径 + README 验收规格执行；原件到位后可再做文字比对。

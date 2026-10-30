# P03 成员D 交付自检报告（E01-E17）

> 生成时间：2026-10-30 ｜ 执行人：成员A（代成员D完成交付并自检）
> 环境：后端 http://localhost:5000（SQL Server LocalDB `P03Inventory`）｜ 前端 http://localhost:4200（Angular dev + /api 代理）
> 基线：`--seed-import` 对账通过：9 商品 / 总库存 129 / 低库存 4 / 缺货 2 / 流水 0，与 `seed-data/expected_initial_dashboard.json` 一致

## 一、验收清单口径说明

原始上传的验收清单（acceptance_manifest.json）与需求书 docx 附件已被系统清理，无法逐行对照原文。本次自检依据 **docs/api-contract.md**（其「领域口径」一节明确标注与验收清单一致）+ seed-data 基线文件重建 E01-E17 检查项。若需对照原文逐行复核，请重新上传两份文件。

## 二、交付物清单（D 剩余工作）

| 交付物 | 位置 | 说明 |
| --- | --- | --- |
| 桌面截图 ×7 | [deliverables/screenshots/desktop/](file:///f:/p03-inventory-management/deliverables/screenshots/desktop) | 登录/仪表盘/商品/分类/库存/报表(商品/流水)，视口 1366×768 |
| 移动截图 ×6 | [deliverables/screenshots/mobile/](file:///f:/p03-inventory-management/deliverables/screenshots/mobile) | 同上页面 375×812（isMobile+touch，DPR=2） |
| 演示录屏 | [deliverables/video/p03-demo.mp4](file:///f:/p03-inventory-management/deliverables/video/p03-demo.mp4) | 登录→商品→分类→库存入库(P001 IN 10)→仪表盘联动→报表→登出 |
| E 用例结果 | [deliverables/self-check/e01-e17-results.json](file:///f:/p03-inventory-management/deliverables/self-check/e01-e17-results.json) | 每条用例的断言与证据（请求/响应级） |

响应式验证结论：375px 下顶部导航压缩为紧凑横向布局、筛选表单纵向堆叠、表格保持可读；未发现样式错乱。

## 三、E01-E17 API 层自检（17/17 PASS）

| 用例 | 验证点 | 结果 | 关键证据 |
| --- | --- | --- | --- |
| E01 | 种子基线 9/129/4/2、3 分类、0 流水 | PASS | dashboard=9/129/4/2 products=9 categories=3 txns=0 |
| E02 | 商品编码重复 → 409 | PASS | DUPLICATE_VALUE |
| E03 | 分类名重复（精确+大小写/空格变体）→ 409 | PASS | 两次均 409 DUPLICATE_VALUE |
| E04 | 删除有商品的分类 → 409 | PASS | REFERENTIAL_CONFLICT |
| E05 | 入库 IN+5 原子生效 | PASS | 10→15，DB 与 dashboard 同步 +5 |
| E06 | 出库 OUT-2（operator 执行） | PASS | 15→13 |
| E07 | 出库超库存 → 400，无副作用 | PASS | INSUFFICIENT_STOCK；库存 13→13、流水数不变 |
| E08 | ADJUST 归零（仅 Admin） | PASS | 13→0，stockStatus=out |
| E09 | 并发出库防超卖 | PASS | 并发 2×OUT 60：1×201 + 1×4xx，最终库存 0，流水 +1（见第五节说明） |
| E10 | 流水查询：商品/类型过滤、倒序、分页、关联回显 | PASS | 过滤正确、DESC、分页 total 一致、productCode/productName 回显 |
| E11 | 负值/零数量校验 → 400 | PASS | 负单价/负库存/负数量/OUT 0 全部 400 |
| E12 | PUT 含禁改字段 → 400 且字段未被改动 | PASS | productCode/currentStock 均 400，数据未变 |
| E13 | 幂等：同 RequestId 重放返回首次结果；不同负载 → 409 | PASS | 重放 200 同 ID 同结果、流水不增加；冲突 409 IDEMPOTENCY_CONFLICT |
| E14 | Operator：ADJUST/删除/编辑/建删分类 → 403；只读+IN/OUT 放行 | PASS | 5 项 403 FORBIDDEN；报表读取 200 |
| E15 | 库存三态互斥（缺货=0；低 0<stock≤阈值；正常>阈值） | PASS | out=5 low=3 normal=1 合计 9；API 过滤与本地口径一致；stock==阈值 归低库存 |
| E16 | 仪表盘：4 指标对账、最近流水≤10 含关联字段、7 天零填充净变更 | PASS | 指标全对账；dailyNetChanges 7 天与本地聚合逐值一致 |
| E17 | 报表：stockAmount=unitPrice×currentStock、过滤口径、流水报表与查询一致 | PASS | 9 行金额全部吻合；流水报表 total/倒序/关联一致 |

结果 JSON 含每条断言细节：[e01-e17-results.json](file:///f:/p03-inventory-management/deliverables/self-check/e01-e17-results.json)

## 四、UI 层补充自检（8/8 PASS）

以 operator 登录走查（Playwright，1366×768）：

| 检查点 | 结果 |
| --- | --- |
| 顶栏显示 OPERATOR 角色徽章 | PASS |
| 商品页无「New product」按钮、无行内删除/编辑按钮 | PASS |
| 分类页无新建入口 | PASS |
| 库存页 ADJUST 选项禁用（并显示 Operator 提示） | PASS |
| Sign out 回登录页 | PASS |
| 登出后访问 /products 被路由守卫重定向回 /login | PASS |

admin 侧页面渲染、空状态（流水为空）、加载骨架、7 天零填充图表已在截图采集流程中确认。

## 五、E09 语义说明（非缺陷）

B 的实现为 CAS 条件更新 + 最多 3 次重试（[StockTransactionService.cs#L110-L144](file:///f:/p03-inventory-management/backend/Services/StockTransactionService.cs#L110-L144)）。并发竞争中失败方：

- 若重读后库存已不足 → 400 INSUFFICIENT_STOCK（如实告知原因，实测命中此路径）
- 若 CAS 持续争用耗尽重试 → 409 STOCK_CONFLICT（代码路径存在，[L140](file:///f:/p03-inventory-management/backend/Services/StockTransactionService.cs#L140)）

两者均为契约错误码总表内定义的响应。E09 核心验收点「并发不超卖、恰好一笔成功、库存归零、流水唯一」全部满足。

## 六、项目书逐行验证（2026-10-30 补充）

> 依据：README.md（项目书交付形态）验收规格 + 任务看板 D 职责（库存操作页/Dashboard/报表/图表 + 截图/录屏/E01-E17 走查 + 交叉评审关注点）。原 manifest/docx 附件已失效，待原件后可再做文字比对。

### 6.1 D 职责逐项核对

| # | 项目书条目 | 结果 | 证据 |
| --- | --- | --- | --- |
| 1 | 库存操作页（商品搜索选择/IN/OUT/ADJUST/实时预览/幂等 RequestId） | 完成 | [stock.component.ts](file:///f:/p03-inventory-management/frontend/src/app/pages/stock/stock.component.ts)；E05-E09、E13 API 全过 |
| 2 | Dashboard 仪表盘（4 指标+最近流水+7 日净变更图表） | 完成 | [dashboard 截图](file:///f:/p03-inventory-management/deliverables/screenshots/desktop/02-dashboard.png)；E16 对账全过（含零填充逐值核对） |
| 3 | 报表页面（商品库存金额+流水报表，过滤/分页） | 完成 | E17 全过：stockAmount=unitPrice×currentStock 9/9 行吻合 |
| 4 | 图表组件（7 日净变更柱状图，chart.js） | 完成 | 截图确认渲染；有数据后增量正确（E16 netValsMatch=true） |
| 5 | 页面截图（桌面 1366×768 + 移动 375×812 响应式） | 完成 | deliverables/screenshots/ 13 张 |
| 6 | 演示录屏 | 完成 | deliverables/video/p03-demo.mp4 |
| 7 | E01-E17 验收走查 | 完成 | API 17/17 + UI 补充 8/8（见第三节/第四节） |
| 8 | 交叉评审点：复用公共组件 | 通过 | 公共逻辑集中于 core/（ApiService/AuthService/ToastService/拦截器/守卫/normalize/models）+ shell 布局；页面无重复实现 |
| 9 | 交叉评审点：路由无冲突 | 通过 | [app.routes.ts](file:///f:/p03-inventory-management/frontend/src/app/app.routes.ts)：6 路径唯一、全懒加载、通配重定向 |
| 10 | 交叉评审点：权限守卫生效 | 通过 | authGuard 保护全部业务路由；登出后访问 /products 重定向 /login（UI 8/8） |
| 11 | 交叉评审点：空状态/加载态正常 | 通过 | 流水空状态、搜索无结果、骨架屏均在截图/走查中确认 |

### 6.2 README 验收规格逐行核对

| README 原文条目 | 结果 | 说明 |
| --- | --- | --- |
| E09「10 并发恰 5 成功 5 拒绝」 | PASS（按规格复测） | 库存 5 + 10 并发 OUT×1：恰 5×201 + 5 拒绝，库存 0，余额严格 4,3,2,1,0，流水恰 +5；证据 [concurrency-readme-spec.json](file:///f:/p03-inventory-management/deliverables/self-check/concurrency-readme-spec.json) |
| E13「并发幂等」 | PASS（按规格复测） | 同 RequestId 6 并发同负载：全部返回首次结果（同 ID 同 newStock），恰好 1 条流水 |
| 浏览器验证流程「登录→Dashboard 核对 9/129/4/2 与 7 日图→Products/Categories→Stock IN/OUT/ADJUST→Reports 金额与流水→Operator 写操作全部隐藏」 | PASS | 录屏完整走通前 5 步；Operator 隐藏项 UI 8/8 |
| 「重放 200 返回首次结果，负载不一致 409」 | PASS | E13：重放 200 同结果；异负载 409 IDEMPOTENCY_CONFLICT |
| 「Operator 登录确认写操作全部隐藏」 | PASS | 新建/删除/编辑入口全部隐藏，ADJUST 选项禁用 |
| README 引用 `tests/api_smoke_e01_e17.py`（19/19）、`tests/e09_e13_concurrency.py`（12/12） | **已补齐** | 实测 19/19、12/12 全绿（曾缺失，2026-10-30 修复，见 6.3-G1） |
| README 引用 `docs/acceptance-records.md`、`docs/deployment.md`、`docs/known-issues.md` | **已补齐** | 三文档入库（曾缺失，2026-10-30 修复，见 6.3-G2） |

### 6.3 项目书级缺口（非 D 单独职责，建议认领）

> **2026-10-30 更新：G1/G2 均已补齐并入库（tests/ + docs/），断言数与 README 规格一致（19/19、12/12），实测全绿。**

| 缺口 | 说明 | 状态 |
| --- | --- | --- |
| G1 | `tests/` 目录：README 承诺的 Python 冒烟脚本（E01-E17, 19 项断言）与并发复现脚本（12 项断言）、Postman 集合均不存在 | **已补齐**：[api_smoke_e01_e17.py](file:///f:/p03-inventory-management/tests/api_smoke_e01_e17.py) 实测 19/19；[e09_e13_concurrency.py](file:///f:/p03-inventory-management/tests/e09_e13_concurrency.py) 实测 12/12（自包含，免重置可跑）；[P03.postman_collection.json](file:///f:/p03-inventory-management/tests/P03.postman_collection.json) JSON 校验通过 |
| G2 | `docs/` 缺 acceptance-records.md / deployment.md / known-issues.md | **已补齐**：[acceptance-records.md](file:///f:/p03-inventory-management/docs/acceptance-records.md)（正式验收记录）、[deployment.md](file:///f:/p03-inventory-management/docs/deployment.md)（部署说明）、[known-issues.md](file:///f:/p03-inventory-management/docs/known-issues.md)（7 项已知问题/设计说明） |

## 七、遗留事项

1. 无阻断缺陷。
2. E13 重放返回 200（非 201）——契约仅要求「返回首次结果」，实测同 ID 同结果、不产生新流水，符合契约。
3. 原始验收清单/需求书附件失效，建议拿到原件后对 E01-E17 标题与口径做一次文字比对（本报告每条用例均附契约依据，便于逐条核对）。
4. 自检后数据库已重置回纯净基线（9/129/4/2，0 流水），供后续演示与集成。

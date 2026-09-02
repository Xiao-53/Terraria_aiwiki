# ADR 0007：产品源码与上游参考仓库分离

- 状态：accepted
- 日期：2026-09-02
- 决策范围：产品源码所有权、参考仓库边界与 Windows 构建资产来源
- 关联：`openspec/changes/archive/separate-product-from-reference/plan.md`、`docs/adr/0002-portable-core-and-data-root.md`、`docs/adr/0003-use-loopback-local-viewer-transport.md`、`openspec/specs/local-wiki-viewer/spec.md`

## 背景

Phase 01–09 的新 Core、Application、Windows、WikiTools 和 Python Agent 最初在
BigBearKing/terraria-wiki-offline 的本地分支中演进。外层仓库只保存治理文件，Windows
project 还在 build/publish 时直接读取旧 MAUI project 的 Viewer Web、bridge 和图标。
这使新产品的源码所有权、提交边界和构建可复现性依赖一个既作为上游参考、又承载本地
产品提交的嵌套仓库。

产品提交 `d67d904c32b853edb664b68121897a2f2a8f2655` 已形成可独立迁移的根级
`src/`、`tests/`、`agent/` 与 solution。上游参考点为
`a1dd89981d7604006ec91955d4d19e93cb933ce1`。

## 决策

1. 外层仓库成为唯一产品源码仓库。产品入口固定为根级 `src/`、`tests/`、`agent/`、
   `assets/` 和 `Terraria_Wiki.Core.slnx`。
2. 产品内容以 `d67d904` 建立一个新的外层内容基线，不嫁接内层 24 个本地产品提交历史；
   内层本地恢复分支 `archive/new-app-before-extraction` 保留该提交。
3. `terraria-wiki-offline/` 作为只读 Git Submodule 固定到纯上游 `a1dd899`，只用于核对旧
   实现、行为和来源。产品 project、tests、build、publish 与 runtime 不得读取它。
4. Windows 当前实际使用的 Viewer Web、`iframe-bridge.js` 和应用图标按 `d67d904` 字节
   快照进入外层 `assets/windows/`。来源、提交、数量和许可提示保存在同目录 README；
   后续更新必须作为显式产品变更重新比对和验证。
5. namespace、assembly、solution 名称、公共 API、SQLite schema、数据根、包格式、AI
   协议、Viewer transport 和产品行为均不因仓库分离而改变。
6. Phase 10 在 P0 对等验收后决定保留或移除参考 Submodule；移除只影响外层 gitlink，
   不删除或改写 BigBearKing 上游仓库。

本决策替代 ADR 0002 中“正式生产 Core 放在内层仓库”及其对外层源码所有权的否定；
ADR 0002 的单一 portable Core、显式绝对数据根、原位数据复用和数据安全约束继续有效。
本决策同时替代 ADR 0003 中“build/publish 从旧 project 直接复制 Viewer 资产”的部分；
ADR 0003 的受限动态 loopback、消息安全和生命周期决策继续有效。

## 后果

正面后果：

- 产品源码、治理、测试和构建资产进入同一可审查提交边界。
- 正常 checkout/build/publish 不需要参考 Submodule 内容，资产输入由产品提交固定。
- 上游参考保持精确、只读和可更新，不再混入产品分支或生成物。

负面后果与约束：

- 外层仓库现在同时承载治理与产品源码，任何基线提交必须逐项暂存，避免夹带既有未跟踪内容。
- Viewer/icon 快照需要显式维护来源与第三方许可提示；Submodule 更新不会自动更新产品资产。
- 旧 MAUI host 不再作为生产 Core 的编译消费者；其 P0 行为只作为参考，接管证据仍由新产品测试和目标平台验证承担。

## 被否决方案

- 继续在内层本地分支开发产品：源码所有权和上游参考边界继续混杂。
- 把上游仓库当普通复制目录：失去精确 gitlink、来源与更新审查能力。
- 让 Submodule 在构建时供给 Viewer/icon：正常构建仍依赖参考仓库内容和初始化状态。
- 嫁接内层逐提交历史：会把上游与产品历史重新混合，并扩大本次物理迁移范围。

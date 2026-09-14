# 修复 Wiki Viewer 站内链接导航

- 状态：archived；验证通过并已归档
- Phase：无
- 执行环境：Windows 11 x64 构建、测试与本地 Release GUI smoke
- 创建日期：2026-09-14
- 最后更新：2026-09-14
- 范围：已下载 Wiki HTML 的站内链接目标、Viewer 点击处理和链接导航 smoke
- 关联：`openspec/specs/local-wiki-viewer/spec.md`、`docs/adr/0003-use-loopback-local-viewer-transport.md`

## 背景与当前证据

用户反馈 Wiki 正文中的链接点击后不跳转。源码与真实加工库证明：`WikiContentProcessor.ProcessAnchorLinks` 对带 `href`/`title` 的链接追加 fragment 后删除 `href`，但没有写入 Viewer 点击脚本读取的 `data-wiki`。真实库中可见 `<a title="目标">`；`wiki-app-common.js` 只在存在 `data-wiki` 且无 `href` 时调用 `gotoPage`，因此点击没有导航。

## 目标与完成定义

1. 当前已经下载的、仅保留 `title` 的站内链接无需重新下载即可在唯一 Reader 中跳转。
2. 后续页面加工为站内链接保存明确的 `data-wiki`，并保留 fragment。
3. 外部链接继续交给系统浏览器，图片链接继续进入图片 Viewer，页面内 `#anchor` 保持既有行为。
4. Core 回归、Windows tests、Release build 和实际 GUI 点击 smoke 通过。

## 范围与非目标

只修改链接目标加工、公共 Viewer 点击逻辑、合成 smoke 和对应测试。不改搜索、数据库格式、UI 布局、Viewer transport、下载流程、AI 或其他已记录问题。

## P0 / 兼容性影响

完整 HTML 仍由 Core 保存并由当前 Viewer 渲染。Viewer 对无 `href` 链接增加 `title` 兼容回退，现有 v2 数据库不迁移；未来写入同时保存 `title` 与 `data-wiki`。导航仍通过受约束的 `PageRedirectAsync` Application 边界。

## 文件变更

- MODIFY `src/Terraria_Wiki.Core/Data/WikiContentProcessor.cs`：为加工后的站内链接保存 `data-wiki`。
- MODIFY `assets/windows/Viewer/Web/_common/wiki-app-common.js`：兼容读取现有库的无 `href` `title`。
- MODIFY Core/Windows tests 与 smoke fixture：覆盖 fragment 和真实点击导航。
- MODIFY `src/Terraria_Wiki.Windows/MainWindow.cs`：smoke 通过 DOM 点击链接，而非直接调用页面命令。

## 执行步骤

- [x] 读取真实调用链并定位链接目标属性不一致。
- [x] 实现当前库兼容与未来加工修正，补回归测试。
- [x] 执行相关测试、全量 Release build/tests、更新本地 Release 并运行隔离 GUI smoke。
- [x] 记录验证、完成计划并建立 Git 保存点。

## 风险与回退

`title` 也用于图片提示，因此兼容回退只对无 `href` 的普通链接生效；图片分支先处理。外链仍依据 `href` 交给受限宿主方法，不使用 `title`。回退本 change 即恢复原行为，不改变数据库格式。

## 进度记录、验证结果与完成摘要

- 2026-09-14：确认真实库存在 href-less `title` 站内链接，公共 Viewer 脚本没有兼容目标，开始修复。
- 2026-09-14：Viewer 对无 `href` 普通链接按 `data-wiki`、`title` 顺序解析；未来内容加工写入 `data-wiki`，fragment 保留，HTTP(S) 外链继续保留原 `href`。
- 2026-09-14：Core 定向测试 4/4、Windows 定向测试 17/17、JavaScript `node --check` 通过。Windows Release solution build 通过，0 warning、0 error；全量 .NET tests 为 310/310。
- 2026-09-14：更新 `artifacts/local-release/rebuild-local-wiki-search/Terraria_Wiki.Windows.exe`。隔离 GUI smoke 使用仅有 `title="Smoke Alias"` 的链接执行真实 DOM click，成功提交 `Smoke Target`，随后完成后退、前进、脚本、样式、图片、音频和退出清理；证据为 `artifacts/acceptance/fix-wiki-viewer-link-navigation/windows-link-smoke.json`。

现有已下载数据库无需迁移或重新下载即可恢复站内链接导航。修复没有改变数据库格式、搜索、Viewer transport 或 UI 布局，本 change 可以归档。

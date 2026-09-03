# 替换 Windows 软件图标

- 状态：completed
- Phase：无；用户授权的独立 Windows 品牌资产变更
- 执行环境：WSL Codex CLI 编辑与静态检查；Windows .NET SDK 构建、测试与 EXE 图标验证
- 创建日期：2026-09-03
- 最后更新：2026-09-03
- 范围：Windows Avalonia 应用图标资产、WinExe 构建配置、边界测试与资产来源说明
- 关联架构/规范/ADR：`docs/architecture/ARCHITECTURE-v0.6.md`、`openspec/project.md`、`openspec/specs/windows-presentation/spec.md`、`docs/adr/0001-use-avalonia-client.md`、`docs/adr/0007-separate-product-source-from-reference.md`

## 背景与当前证据

用户提供了 1254×1254 RGBA PNG 并要求修改软件图标。当前 Windows 项目把
`assets/windows/AppIcon/appicon.ico` 作为 Avalonia 资源供主窗口与 Shell 使用，但
`Terraria_Wiki.Windows.csproj` 没有设置 `ApplicationIcon`，因此不能用源码配置证明生成的
WinExe 使用该图标。现有 ICO 包含 256、64、48、32、16 五种尺寸；`logo.svg` 是独立的旧品牌
矢量资产，本次没有替换它的授权或必要性。

当前 Git 恢复点为 `a1ecc1e`。工作区存在与本任务无关的未跟踪文件，本变更不得修改或提交它们。

## 目标与完成定义

- 用户提供的图片成为 Windows 窗口、Shell 和 WinExe 的软件图标。
- ICO 保持 256、64、48、32、16 五种带透明度的尺寸，兼容当前 Windows 10+ 目标。
- 保留可追溯的原始 PNG，并准确更新资产来源、哈希和生成方法说明。
- Windows Release 构建与相关测试通过；从实际 WinExe 提取出的关联图标可验证为新图标。

## 前置条件

- Phase 00–10 已完成；本变更不要求开始 Phase 11 的安装器、签名或发布渠道工作。
- 使用用户本轮提供的图片作为输入，路径为
  `/mnt/c/Users/Xiao/Downloads/ChatGPT Image 2026年9月3日 12_36_32.png`。
- Windows 已安装可用的 .NET SDK 与 System.Drawing；不新增产品依赖。

## 范围与非目标

范围内：保存源 PNG、生成多尺寸 ICO、设置 Windows `ApplicationIcon`、补充边界测试和资产说明。

非目标：重做页面、布局、品牌矢量 `logo.svg`、安装器、签名、发布渠道、Android 或其他平台图标，
以及 Phase 11 的其余产品化工作。此次不触发 Windows UI Skill，因为没有新页面、共享组件或视觉布局变更。

## P0 / 兼容性影响

不改变 Core、Application、SQLite/FTS5、数据包、Viewer、AI Runtime 或公共 API。现有
`avares://Terraria_Wiki.Windows/Assets/appicon.ico` 路径保持不变，避免窗口与 Shell 调用方变化。
仅 Windows 构建和品牌资产受影响。

## 方案与待决问题

采用用户 PNG 作为无损源文件，通过 Windows System.Drawing 高质量双三次缩放生成 256、64、48、
32、16 像素的 32-bit RGBA PNG-compressed ICO 帧。项目最低目标为 Windows 10，支持这种 ICO 编码。
同时设置 MSBuild `ApplicationIcon`，使 WinExe 原生资源与 Avalonia 内部资源使用同一个文件。

不把源 PNG作为 AvaloniaResource；它只用于来源追溯与未来再生成。无待用户确认问题。

## 文件变更

- ADD `assets/windows/AppIcon/appicon-source.png`：用户提供的原始图。
- MODIFY `assets/windows/AppIcon/appicon.ico`：由源图生成的新多尺寸软件图标。
- MODIFY `assets/windows/README.md`：区分上游快照与产品覆盖图标，记录生成方法与哈希。
- MODIFY `src/Terraria_Wiki.Windows/Terraria_Wiki.Windows.csproj`：设置 WinExe `ApplicationIcon`。
- MODIFY `tests/Terraria_Wiki.Windows.Tests/PresentationBoundaryTests.cs`：守住原生应用图标配置。
- ADD/MOVE `openspec/changes/replace-windows-app-icon/plan.md`：记录执行证据并在完成后归档。
- KEEP `assets/windows/AppIcon/logo.svg`：不改变旧矢量品牌资产。

## 执行步骤

- [x] 核对当前资产、调用位置、ICO 尺寸、源 PNG 透明度与 Git 状态；恢复点为 `a1ecc1e`。
- [x] 保存源 PNG并生成多尺寸 ICO；解析帧表并人工查看代表尺寸。
- [x] 接入 WinExe 图标配置、补充边界测试并更新资产来源说明。
- [x] 在 Windows 执行 Release 构建和相关测试，从生成的 WinExe 提取关联图标验证。
- [x] 检查差异、临时文件和仓库状态，填写验证结果并归档计划。

## 验证与回归计划

- WSL：解析 ICO 目录，确认五个预期尺寸、32 bpp、PNG 帧和透明通道；`git diff --check`。
- Windows：`dotnet build Terraria_Wiki.Core.slnx -c Release --no-restore --nologo`。
- Windows：运行 `Terraria_Wiki.Windows.Tests` 中相关边界测试；如时间允许运行完整 solution tests。
- Windows：用 `System.Drawing.Icon.ExtractAssociatedIcon` 从实际 Release WinExe 提取图标并人工查看。
- Android：不适用；Phase 13 已暂缓，本变更没有 Android 授权。

## 风险与回退

- 小尺寸像素画可能因缩放变糊；通过 64/32/16 预览检查，必要时调整缩放策略。
- Windows Explorer 可能缓存旧图标；构建产物资源验证不依赖 Explorer 缓存。
- 资产来源与旧上游快照不同；README 必须明确该覆盖，不能继续宣称 94 个文件全部字节一致。
- 回退方式：恢复本变更前的 ICO、项目配置、测试和资产说明，并删除新增源 PNG。

## 进度记录

- 2026-09-03：完成现状调查并建立计划，下一步生成与验证图标资产。
- 2026-09-03：保存原始 PNG，生成并人工检查五尺寸 ICO；设置 `ApplicationIcon` 并更新测试与来源说明。
- 2026-09-03：Windows Release build 0 warning/0 error，相关测试 5/5、完整测试 275/275 通过；从实际 WinExe 成功提取并检查新图标。

## 发现、偏差与决策

- 决定保留 `logo.svg`，因为实际窗口与 Shell 均引用 ICO，用户提供的是软件图标替代图而非矢量品牌系统重做。
- 决定增加 `ApplicationIcon`，因为仅嵌入 AvaloniaResource 不能保证 WinExe 文件图标被设置。
- 生成后的 ICO 被 Windows System.Drawing 正常加载为 32×32 默认帧；`file` 命令把全 PNG 帧 ICO
  误识别为 Targa，但 ICO 目录解析、Windows 加载、MSBuild 编译和 WinExe 资源提取均通过，因此不改为旧式 DIB 帧。

## 验证结果

- WSL ICO 解析：ICONDIR 类型 1、5 帧；256/64/48/32/16 均为 planes 1、32 bpp、PNG 数据。
- 人工预览：256、64、32、16 像素版本均能辨识绿色树、地块和 `WIKI` 字样。
- 哈希：源 PNG `7892172063d661a2ee77b389b105bcf201df4f80682cf7e66b9dae9bbbcd16b8`；
  ICO `cfb581e78a1a1682c4e0593cf56d5f567f14e0c79f677ae56859c8dbb26e0518`。
- 资产集合：94 文件 runtime aggregate
  `7b7d0ece9f45bcfb33bd58974423a1f20ca8f98d3679ff917c721d0b65f17cde`；96 文件 support aggregate
  `7c6e3d05f29a39c99f3b81a52f36e716a1120f3a6438e37265a2d021b35d0dfd`。
- Windows：Release solution build 通过，0 warning / 0 error。
- Windows：`PresentationBoundaryTests` 5/5 通过；完整 solution Core 92、Application 54、WikiTools 28、Windows 101，共 275/275 通过。
- Windows WinExe：`Icon.ExtractAssociatedIcon` 从实际 Release EXE 成功提取 32×32 新图标，人工检查一致。
- 静态检查：`git diff --check` 通过；生成脚本与预览文件均已清理；既有无关未跟踪文件未修改。

## 完成摘要

Windows 主窗口、Shell 品牌入口和生成的 WinExe 现在统一使用用户提供的新图标。原始 PNG 可追溯，
现有 `logo.svg`、P0 能力、数据、Viewer、AI Runtime 和非 Windows 平台均未改变。

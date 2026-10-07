# Terraria AI Wiki

Terraria AI Wiki 是一个面向 Windows 的本地 Terraria Wiki 阅读与 AI 辅助查询项目。

主要能力包括 Wiki 下载与更新、SQLite / FTS5 搜索、离线页面阅读、收藏、数据库导入导出，以及通过本地 Wiki 查询边界获取事实的 AI 问答。

## 源码目录

- `src/`：C# Wiki Core、Application、Wiki Tools、Avalonia Windows 客户端和 Viewer 宿主源码。
- `agent/`：Python AI Agent 与 Sidecar 源码。
- `assets/windows/Viewer/`：内嵌 Wiki Viewer 的 HTML、JavaScript 和 CSS 源码。

当前客户端使用 .NET、Avalonia 和 WebView2；AI 部分使用 Python 与 Pydantic AI。当前产品目标为 Windows，Android 尚未实现。

## 仓库范围

此远程仓库的最新版本仅展示产品源码与本 README。开发计划、Agent 规则、测试、构建脚本、项目与依赖配置、图片、字体、数据库和发布制品不在当前发布范围内，因此该源码快照不能直接构建或运行。

## 来源与权利

项目基于 [BigBearKing/terraria-wiki-offline](https://github.com/BigBearKing/terraria-wiki-offline) 演进，Viewer 资产来源快照对应上游提交 `a1dd899`。第三方源码中已有的版权与许可声明保持原样。

本项目为非官方项目，与 Re-Logic、wiki.gg 或 Calamity Mod 团队无隶属或背书关系。Terraria 及相关名称、商标和游戏素材属于各自权利人。Wiki 内容与第三方组件分别遵循其适用条款；项目自有源码采用 CC BY-NC-SA 4.0（与上游当前协议相同）；第三方源码与素材保留各自许可及权利限制。

# 变更说明（相对上游）

> 上游项目：[naravid19/N_m3u8DL_RE_GUI](https://github.com/naravid19/N_m3u8DL_RE_GUI)
> 基线提交：`dev` 分支 `74572d818395408e5d2e638c3691fbfe95167e18`（v2.1.5 代）
> 本仓库版本：`2.1.6`（汉化增强版）
> 许可：MIT（沿用上游，详见 `LICENSE`）

本文件只记录**本汉化增强版相对上游**的改动；上游自身的完整变更历史见 `CHANGELOG.md`。

---

## 改动总览

| 类别 | 文件 | 变化 |
| --- | --- | --- |
| 简体中文化 | `MainWindow.xaml`（约 298 条 UI 串）、`MainWindow.xaml.cs`、`ViewModels/MainViewModel.cs`、`App.xaml.cs`、`Services/DownloadService.cs`、`Views/StreamPickerWindow.xaml` | UI 文案与运行期提示全部汉化 |
| 字体功能 | `MainWindow.xaml`、`MainWindow.xaml.cs`、`StreamPickerWindow.xaml`、`Services/MainWindowConfigMapper.cs`、`N_m3u8DL_RE_GUI.csproj` | 新增字体选择 / 导入功能；根窗口 `FontSize="15"`（较上游 +2）；可选用内嵌「京華老宋体-GJ」 |
| Cloudflare 绕过脚本重写 | `m3u8_cf_bypass.py` | 并发 / 断点续传 / UTF-8 输出修复；代理交由 GUI 开关控制 |
| CF 命令构造 | `N_m3u8DL_RE_GUI.Core/CfCommandBuilder.cs` | `CfCommandOptions` 新增 `Threads`、`Proxy`，拼接 `--threads` / `--proxy` |
| CF 分片目录 | `MainWindow.xaml.cs`（`BuildCfOptions`） | 分片目录固定为 `保存目录\cf_segments` |
| 日志防卡死 | `MainWindow.xaml.cs`（`AppendLog`/`FlushLog`） | 缓冲上限 `MaxLogChars=80000` + 200ms 刷新节流 |
| 代理开关 | `MainWindow.xaml`、`MainWindow.xaml.cs`、`Services/MainWindowConfigMapper.cs` | 新增「使用代理」开关（默认直连）；代理地址可编辑、自动记忆 |
| B 站支持 | `MainWindow.xaml.cs`、`N_m3u8DL_RE_GUI.csproj`、`yt-dlp.exe`（新增） | 识别 B 站输入并调用 yt-dlp 下载 |
| 发布配置 | `N_m3u8DL_RE_GUI.csproj` | 4 个外部依赖设 `CopyToPublishDirectory` + `ExcludeFromSingleFile` |
| 版本号 | `Directory.Build.props` | `<AppVersion>` 2.1.5 → 2.1.6 |

`git diff --stat`（相对上游 `dev`）：

```
 Directory.Build.props                         |   2 +-
 N_m3u8DL_RE_GUI.Core/CfCommandBuilder.cs      |  10 +-
 N_m3u8DL_RE_GUI/App.xaml.cs                   |   6 +-
 N_m3u8DL_RE_GUI/MainWindow.xaml               | 479 +++++++++++++-------------
 N_m3u8DL_RE_GUI/MainWindow.xaml.cs            | 287 +++++++++++----
 N_m3u8DL_RE_GUI/N_m3u8DL_RE_GUI.csproj        |  13 +
 N_m3u8DL_RE_GUI/Services/DownloadService.cs   |  30 +-
 N_m3u8DL_RE_GUI/ViewModels/MainViewModel.cs   |  16 +-
 N_m3u8DL_RE_GUI/Views/StreamPickerWindow.xaml |  20 +-
 m3u8_cf_bypass.py                             | 281 ++++++++++++---
 10 files changed, 747 insertions(+), 397 deletions(-)
```

---

## 1. 简体中文化

- `MainWindow.xaml`：约 298 条 UI 字符串（Tab 标题、Label、CheckBox、Button、ToolTip、`AutomationProperties.Name` 辅助说明等）由英文改为简体中文，同时保留技术名词英文（HLS / DASH / KEY / IV / Proxy 等）。
- `MainWindow.xaml.cs`：状态栏、日志、MessageBox、异常提示等运行期文案汉化。
- `ViewModels/MainViewModel.cs`：下载启动 / 失败 / 停止 / URL→标题 等提示汉化。
- `App.xaml.cs`：全局异常对话框汉化（「发生了意外错误…」/「意外错误」）。
- `Services/DownloadService.cs`：进程调度日志（开始下载、命令、失败、退出码等）汉化。
- `Views/StreamPickerWindow.xaml`：清晰度选择弹窗汉化。

> 说明：上游自身也提供 en-US / 简体中文 / 繁體中文 三语切换（`LanguageService` + `{DynamicResource Str_*}`）。本版本的汉化是**在源码层把默认英文文案直接改为简体中文**，与上游运行期多语言机制并存。

## 2. 字体功能（选择 / 导入）

- **字体选择器**：标题栏新增「字体」下拉框 `Combo_UIFont`，枚举系统字体（`System.Windows.Media.Fonts.SystemFontFamilies`），选中即全局应用。
- **导入自定义字体**：新增「导入」按钮 `Button_ImportFont`，通过 `OpenFileDialog` 选择本地 `.ttf/.otf/.ttc`，用 `Fonts.GetFontFamilies(new Uri(path))` 加载并加入下拉列表后应用。
- **内置字体（可选）**：若构建时 `N_m3u8DL_RE_GUI/Fonts/KingHwaOldSong-GJ.ttf`（京華老宋体-GJ，家族名 `KingHwaOldSong-GJ`）存在，则以 `<Resource>` 嵌入并作为「京華老宋体-GJ（内置）」选项；`.csproj` 的 `<Resource>` 带 `Condition="Exists(...)"`，**缺少该字体文件的克隆仍可正常构建**。
- **默认与回退**：根窗口 `FontFamily` 默认为安全字体 `Microsoft YaHei UI`；选中的字体源通过 `ApplyFontSource` 应用，加载失败回退到默认。
- **持久化**：选中项经 `MainWindowConfigMapper.Capture` 以 `UIFont` 键（base64）写入配置；启动时 `Window_Loaded` 调用 `InitializeFontFeature(config.GetDecodedBase64("UIFont"))` 还原。
- **子窗口继承**：`StreamPickerWindow` 以 `FontFamily = this.FontFamily` 创建，继承所选字体。
- 全局字号较上游 +2（`FontSize="15"`）；命令预览区 `Consolas` 由 11 提升到 13。

> 说明：本仓库**不再默认内嵌并强制**京華老宋体。字体文件（约 30 MB）不纳入 git，构建缺少它时自动回退系统字体；如需该观感可运行时「导入」或从 Release 附件获取。京華老宋体为开源字体项目。

## 3. Cloudflare 绕过脚本重写（`m3u8_cf_bypass.py`）

在上游脚本基础上重写，新增 / 强化：

- **代理交给 GUI（默认直连）**：CF 脚本的 `--proxy` 由 GUI 代理开关决定——不勾选「使用代理」时传 `direct`（强制直连）；勾选时传用户填写的地址；勾选但留空时传 `auto`（按 环境变量 `HTTPS_PROXY`/`HTTP_PROXY`/`ALL_PROXY` → Windows 注册表 `Internet Settings` 顺序自动探测）。脚本自身不再假定任何具体地址。
- **并发下载**：`--threads`（默认 16，上限 64），`ThreadPoolExecutor`，每线程独立 `curl_cffi` Session。
- **断点续传**：默认复用已下载的 `*.ts`；`.part` 原子写（`os.replace`）；`cf_manifest.txt` 记录播放列表 URL，URL 变更时自动清空旧分片；`--overwrite` 强制重下。
- **分片目录**：`--seg-dir`，默认 `输出目录\cf_segments`。
- **UTF-8 输出修复**：强制 `stdout/stderr` 以 UTF-8（errors=replace）输出，兼容 GUI `.bat` 的 `chcp 65001` 与 GBK 控制台。

## 4. CF 命令构造（Core/CfCommandBuilder.cs）

`CfCommandOptions` record 新增两个字段：

```csharp
bool KeepSegments,
int Threads,      // 新增
string Proxy);    // 新增
```

`BuildCommand` 在合适位置追加 `--threads <N>`（`N > 0` 时）与 `--proxy "<X>"`（非空时）。

## 5. B 站（yt-dlp）支持

`MainWindow.xaml.cs` 新增：

- `IsBilibiliInput`：识别 `BVxxxx`（长度 ≥ 10）、`av<数字>`、`bilibili.com` / `b23.tv` / `b23.wtf` 链接；
- `NormalizeBilibiliInput`：归一化为 `https://www.bilibili.com/video/<id>`；
- `StartBilibiliDownloadAsync`：定位 `yt-dlp.exe`（程序目录优先，其次当前目录），参数
  `-f "bv*+ba/b" --merge-output-format mp4 --ffmpeg-location <dir> --no-playlist --newline --no-mtime --concurrent-fragments N`（`N` 取自最大并发，默认 16，上限 64）。

依赖新增 `yt-dlp.exe`（`N_m3u8DL_RE_GUI.csproj` 增加对应 `<None Include="..\yt-dlp.exe">`）。

## 6. 发布配置与版本号

- `N_m3u8DL_RE_GUI.csproj`：`N_m3u8DL-RE.exe` / `ffmpeg.exe` / `m3u8_cf_bypass.py` / `yt-dlp.exe` 统一设
  `CopyToPublishDirectory=PreserveNewest` + `ExcludeFromSingleFile=true`（不塞进单文件 exe，与主程序同目录）。
- `Directory.Build.props`：`<AppVersion>` 由 2.1.5 提升为 2.1.6。

## 7. 代理开关（默认直连）

- **界面新增**：网络标签页「请求与代理」中新增 `CheckBox_UseProxy`（「使用代理」，默认不勾选）与可编辑的 `TextBox_Proxy` 代理地址栏。
- **默认直连**：不勾选时不使用任何代理（N_m3u8DL-RE 不传 `--custom-proxy`；CF 脚本传 `--proxy direct`；yt-dlp 传 `--proxy ""`），地址栏置灰。
- **统一生效**：开关同时作用于 N_m3u8DL-RE、Cloudflare 绕过脚本与 B 站 yt-dlp 三个下载路径。
- **自动记忆**：代理地址与开关状态分别以 `代理`（base64）与 `使用代理` 键写入 `config.json`，下次启动自动还原。
- **实现**：`MainWindow.xaml.cs` 新增 `UseProxyEnabled` / `EffectiveProxyUrl()` / `CfEffectiveProxy()` 辅助方法；`MainWindowConfigMapper` 负责持久化。

---

## 许可

本仓库为上游的衍生作品，沿用上游 **MIT License**。原始版权归 `Copyright (c) 2026 Narawit` 所有，详见根目录 `LICENSE`。

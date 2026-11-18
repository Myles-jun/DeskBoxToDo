# 开发与构建

## 工具链

不需要 .NET SDK：目标框架是系统自带的 .NET Framework 4.8，用 Visual Studio 2022 自带的 Roslyn `csc.exe` 编译；
`rc.exe`（Windows SDK）只用来把图标和版本信息嵌进 exe，没有也能出包（降级为仅图标）。

```
powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1
```

产物：

- `DeskBoxToDo.exe` —— 单文件，无外部依赖
- `app.ico` / `version.res` —— 中间产物
- `dist/DeskBoxToDo_1.0.0/` + `dist/DeskBoxToDo_1.0.0.zip` —— 可分发版本

## 代码结构

| 文件 | 作用 |
| --- | --- |
| `Program.cs` | 入口；CLI 模式与托盘模式分流 |
| `TrayShell.cs` | 托盘、热键、菜单、采集→解析→确认→写入的编排 |
| `SelectionCapture.cs` | 取选中文字：UIA 优先，失败退回模拟 Ctrl+C（等修饰键松开） |
| `TodoParser.cs` | 本地规则解析：时间/子项/标题/重要/颜色 |
| `AiParser.cs` | 可选 LLM 解析（OpenAI 兼容，省 token 设计、缓存、错误翻译） |
| `DeskBoxStore.cs` | 读写 DeskBox 的 `todo.json`：合并、排序、`.bak` 备份、重启 DeskBox |
| `PreviewForm.cs` | 确认窗（毛玻璃） |
| `GlassUi.cs` | 毛玻璃窗口基类 + 输入框/按钮/开关/色板 |
| `GlassMenu.cs` | 毛玻璃右键菜单 |
| `OnboardingForm.cs` / `AboutForm.cs` / `SettingsForm.cs` | 首次引导 / 关于 / 设置 |
| `HotkeyRecorderForm.cs` | 快捷键录制（底层键盘钩子） |
| `TrayIconFactory.cs` / `IconExport.cs` | 自绘托盘图标与多尺寸 .ico 导出 |
| `AppInfo.cs` | 版本号、引导版本 |

## 调试用 CLI

```
DeskBoxToDo.exe --text "周五交作业" --dry-run --out r.json   # 只解析
DeskBoxToDo.exe --ai-test                                    # 测 API Key
DeskBoxToDo.exe --settings                                   # 打开设置
DeskBoxToDo.exe --onboarding                                 # 预览引导
DeskBoxToDo.exe --menu-test / --icon-test / --export-icon p  # 截素材
```

环境变量 `DESKBOX_TODO_AI_FAKE='{"t":"...","d":"...","i":true}'` 可离线跑通 AI 链路；
`DESKBOX_TODO_NO_CAPTURE_EXCLUSION=1` 关闭"窗口对截屏隐身"，便于截图调试。

## 已知约束

- 写入时必须让 DeskBox 退出再启动（它的 ViewModel 是 todo.json 的唯一写入方）
- 取词依赖 UIA 或剪贴板，个别自绘界面（部分游戏、Electron 应用）可能取不到
- WinForms 无法做真·DWM 亚克力，因此毛玻璃用"整屏模糊 + 按位置裁切/实时抓取"实现

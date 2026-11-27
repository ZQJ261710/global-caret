# Global Caret

适用于 Windows 的全局文本输入光标叠加程序。视觉效果参考 [Woodfish Theme](https://github.com/woodfishhhh/Woodfish-Theme)：彩虹色光标、柔和光晕，以及移动时的短拖尾。本项目独立运行，没有复制 Woodfish Theme 的代码。

## 下载和运行

1. 从 [Releases](https://github.com/ZQJ261710/global-caret/releases) 下载 `GlobalCaret-v0.2.0-win-x64.zip` 并解压。
2. 双击 `GlobalCaret.exe`。发布包自带 .NET 10 运行时，无需另装 .NET Desktop Runtime。

右键系统托盘图标可暂停或退出；双击图标可切换暂停状态。程序不会自动开机启动，也不会读取、保存或上传输入内容。

## 实现与适用范围

- 优先使用 Windows UI Automation 的 `TextPattern2.GetCaretRange()` 获取当前插入点，再尝试 `TextPattern`。传统控件可回退到 Win32 原生光标。
- 只有成功查询焦点控件并确认它不是密码框时才显示效果；查询失败时隐藏。原生 `Edit` 密码样式也会被排除。
- 透明窗口只覆盖光标附近区域，随光标移动，并允许鼠标点击穿透。Windows 焦点和光标事件触发更新，低频轮询处理未发事件的编辑器。
- 使用每显示器 DPI 感知和物理像素坐标定位。某些自绘编辑器、游戏、独占全屏程序、管理员权限窗口和安全桌面可能不公开光标位置。

## 从源码构建

需要 Windows 和 [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)：

```powershell
dotnet build .\GlobalCaret\GlobalCaret.csproj -c Release
dotnet run --project .\GlobalCaret\GlobalCaret.csproj -c Release
```

生成 Windows x64 独立单文件版本：

```powershell
dotnet publish .\GlobalCaret\GlobalCaret.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=false -o .\GlobalCaret\publish
```

第三方组件 FlaUI 和 Interop.UIAutomationClient 的许可文件随发布包提供。

## 验证

v0.2.0 已通过 Release 构建，以及普通 WinForms 输入框和密码框的焦点定位检查。不同缩放比例的多显示器组合、不同浏览器和第三方编辑器仍取决于应用自身的 UI Automation 实现；欢迎提交可复现的问题。

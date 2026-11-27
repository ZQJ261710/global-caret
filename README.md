# Global Caret

一个适用于 Windows 的文本输入光标叠加程序。视觉效果参考 [Woodfish Theme](https://github.com/woodfishhhh/Woodfish-Theme)：约 6px 的彩虹色光标、柔和光晕，以及移动时的短拖尾。程序独立运行，不修改其他应用的文件。

## 下载和运行

1. 在 [Releases](https://github.com/ZQJ261710/global-caret/releases) 下载 `GlobalCaret-v0.1.0-win-x64.zip` 并解压。
2. 安装 [.NET 9 Desktop Runtime（Windows x64）](https://dotnet.microsoft.com/download/dotnet/9.0)，如果电脑上尚未安装。
3. 双击解压后的 `GlobalCaret.exe`。请将同目录下的 `.dll` 和 `.json` 文件与它放在一起。

运行后，右键系统托盘图标可暂停或退出；双击图标可切换暂停状态。程序不会自动设为开机启动。

## 支持范围

程序优先通过 Windows UI Automation 的 `TextPattern` 获取当前输入位置，在传统编辑控件中回退到 Win32 原生光标。透明叠加层允许鼠标点击穿透，并跳过系统标记为密码框的输入区域。程序不读取、保存或上传输入内容。

显示效果取决于目标应用是否向 Windows 公开光标位置。某些自绘编辑器、游戏、独占全屏程序、管理员权限窗口和安全桌面可能无法显示。不同缩放比例的多显示器组合尚未完整验证。

## 从源码构建

需要 Windows 和 .NET 9 SDK：

```powershell
dotnet build .\GlobalCaret\GlobalCaret.csproj -c Release
dotnet run --project .\GlobalCaret\GlobalCaret.csproj -c Release
```

发布到目录：

```powershell
dotnet publish .\GlobalCaret\GlobalCaret.csproj -c Release -o .\GlobalCaret\publish
```

## 项目状态

当前版本是 Windows 原型，已通过 Release 构建及启动检查。跨应用的光标定位和视觉效果仍需在具体应用中验证。Woodfish Theme 是视觉参考；本项目未复制其代码。

# 动态照片工坊 Motion Photo Studio

<img src="https://github.com/user-attachments/assets/b1df8cec-dcc8-47d0-8db4-890f6a8e8ea1" alt="main_banner" height="500px" />

通过简洁明了的 UI，使用现有的照片或视频，亦或是来自 iPhone 的实况，将其制作成**动态照片**（Google Motion Photo）。
Windows 11 自带「照片」或是 Google 相册和大部分安卓手机相册可以直接播放动效。

项目灵感来自 [happycola233/MotionPhotoMaker](https://github.com/happycola233/MotionPhotoMaker)
（衍生自 [MotionPhotoMuxer](https://github.com/mihir-io/MotionPhotoMuxer)）：

| | 上游方案 | 本项目 |
|---|---|---|
| 形态 | Python CLI（PyInstaller 打包） | WebView2 桌面应用，单文件 exe ≈ 1 MB |
| 依赖 | Python 运行时 + ExifTool | 无（.NET Framework 4.8 为系统组件） |
| 元数据 | ExifTool 写 XMP | 纯 C# 重写 JPEG APP1 段 |
| 界面 | 命令行 | Material 3 Expressive |

## 功能

- **实况照片转换**：iPhone 的 `IMG_xxxx.JPG` + `IMG_xxxx.MOV` 直接配对合成。
- **任意图片格式**：PNG / BMP / TIFF 会先转码为 JPEG 再打包（白底，避免透明通道丢失）。
- **批量处理**：拖入文件夹递归扫描，自动按同名配对，实时进度与结果清单。
- **纯本地合成**：不依赖 ExifTool、ffmpeg 或任何联网服务。
- **便携**：解压即用，所有数据（设置、界面资源、日志）都在程序目录的 `data\`。

## 技术要点

- **宿主**：.NET Framework 4.8 WinForms 无边框窗口 + WebView2，托管程序集与原生
  `WebView2Loader.dll` 全部内嵌进 exe，运行时再解出，所以交付物只有一个 exe。
- **界面**：内嵌 HTML/CSS/JS 通过虚拟域名 `https://app.local` 加载；宿主与页面通过
  `postMessage` 桥通信（选文件、扫描配对、批量任务、缩略图、设置持久化）。
- **合成**：重建 JPEG 的 APP1 XMP 段（写入 `GCamera:MotionPhoto*`、
  `Container:Directory/Item` 与兼容旧版的 `MicroVideoOffset`），再于 EOI 后拼接视频。
- **配色**：前端按 Material You 规则由种子色实时生成整套色彩角色（OKLab 色域映射），
  可跟随 Windows 系统强调色。
- **窗口**：无边框 + DWM 圆角/深色边框；页面检测指针贴边后交由系统原生缩放循环。

## 构建

需要 **.NET 8 SDK**（仅用于编译，运行时只要系统自带的 .NET Framework 4.8）：

```powershell
# 若把便携 SDK 解压到 .tools\dotnet，脚本会优先使用它，否则用 PATH 里的 dotnet
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

产物在 `dist\MotionPhotoStudio-win-x64\`（exe + 使用说明，可直接压缩分发）。

图标重建（可选，需要 Node 与 Chrome）：

```powershell
node tools\make-icon.js
```

## 命令行

```
MotionPhotoStudio.exe --mux 照片.jpg 视频.mp4 输出.jpg      # 退出码 0 成功 / 2 失败
MotionPhotoStudio.exe --version
```

## 目录结构

```
src/                 C# 工程（宿主、消息桥、合成核心）
src/Assets/          Material 3 Expressive 界面（内嵌进 exe）
src/Core/            Motion Photo 合成核心（纯 C#）
tools/make-icon.js   应用图标打包脚本
docs/                分发说明
build.ps1            一键编译 + 装配 dist
```

## 许可与致谢

本项目以 **GPL-3.0** 发布（见 [LICENSE](LICENSE)），因为它是
[happycola233/MotionPhotoMaker](https://github.com/happycola233/MotionPhotoMaker)
（GPL-3.0）的衍生作品。感谢上游作者与
[MotionPhotoMuxer](https://github.com/mihir-io/MotionPhotoMuxer) 的探索。

界面使用 Microsoft WebView2（BSD-3-Clause，见 `Microsoft.Web.WebView2` 包许可）。

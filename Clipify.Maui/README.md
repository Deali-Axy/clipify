# Clipify - 视频剪辑工具

Clipify是一个跨平台的视频处理工具，支持视频分割、音频提取等功能。

## 项目结构

项目采用模块化设计，主要包含以下几个部分：

### Clipify.Core

核心业务逻辑库，包含与UI无关的业务逻辑，如视频处理、文件操作等。这个库是平台无关的，可以被不同的UI项目引用。

主要组件：
- 接口定义（IVideoService, IDialogService等）
- 模型类（VideoProcessingOptions, AudioExtractionOptions等）
- 服务实现（VideoProcessor, FFmpegCommandGenerator等）

### Clipify.Forms

基于WinForms + Blazor的桌面应用，仅支持Windows平台。

### Clipify.Maui

基于.NET MAUI + Blazor Hybrid的跨平台应用，支持Windows、macOS、iOS、Android等平台。

## 迁移计划

项目正在从WinForms + Blazor向MAUI Blazor Hybrid迁移，迁移步骤如下：

1. 创建Clipify.Core类库，将业务逻辑从WinForms项目中分离出来
2. 创建Clipify.Maui项目，使用MAUI Blazor Hybrid框架
3. 在Clipify.Maui项目中实现平台特定的服务（如对话框、消息通知等）
4. 逐步将UI从WinForms迁移到MAUI

## 开发环境

- .NET 8.0
- Visual Studio 2022 或更高版本
- .NET MAUI 工作负载

## 如何构建

```bash
# 还原依赖
dotnet restore

# 构建项目
dotnet build

# 运行WinForms版本
dotnet run --project Clipify.Forms

# 运行MAUI版本
dotnet run --project Clipify.Maui
```

## 注意事项

MAUI项目需要安装相应的平台SDK：
- Windows: Windows SDK
- macOS: Xcode
- iOS: Xcode
- Android: Android SDK

## 许可证

MIT

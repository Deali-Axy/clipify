# Clipify 转换器使用说明

## 转换器架构

ClipifyConveter 采用模块化的转换器架构，支持多种视频格式转换。

### 目录结构

```
Converters/
├── Options/                    # 转换器选项
│   ├── ConverterOptions.cs    # 选项基类
│   ├── TsToMp4Options.cs      # TS转MP4选项
│   └── MkvToMp4Options.cs     # MKV转MP4选项
├── IConverter.cs              # 转换器接口
├── BaseConverter.cs           # 转换器基类
├── TsToMp4Converter.cs       # TS转MP4转换器
└── MkvToMp4Converter.cs      # MKV转MP4转换器
```

## 已实现的转换器

### 1. TS 转 MP4 转换器

**功能**: 将 .ts 文件转换为 .mp4 格式

**特性**:
- 支持直接封装转换（不重新编码，速度快）
- 可选重新编码模式
- 可配置是否自动覆盖同名文件

**默认配置**:
- 封装转换模式（CopyCodec = true）
- 自动覆盖同名文件（AutoOverwrite = true）

### 2. MKV 转 MP4 转换器（H.264）

**功能**: 将 .mkv 文件转换为 .mp4 格式，支持 H.264 编码

**特性**:
- 支持多种编码器：
  - **软件编码**: libx264（兼容性最好，质量最佳）
  - **NVIDIA 硬件编码**: h264_nvenc（需要 NVIDIA 显卡）
  - **Intel 硬件编码**: h264_qsv（需要 Intel 显卡/CPU）
  - **AMD 硬件编码**: h264_amf（需要 AMD 显卡）

- 编码预设（速度/质量平衡）：
  - ultrafast: 极快（质量较低）
  - fast: 快速
  - medium: 中等（推荐，默认）
  - slow: 慢（质量较好）
  - veryslow: 很慢（质量最好）

- 质量控制：
  - 软件编码：CRF 值（0-51，推荐 18-28，默认 23）
  - 硬件编码：码率控制（kbps）或自动模式

**默认配置**:
- 视频编码器：libx264（软件编码）
- 音频编码器：AAC
- 编码预设：medium
- 质量：CRF 23
- 自动覆盖同名文件

## 使用方法

### 基本用法

```bash
# 在当前目录执行转换
ClipifyConveter.exe

# 指定目标目录
ClipifyConveter.exe "C:\Videos"
```

### 交互式配置

程序运行时会提示：

1. **选择转换器**: 输入编号选择单个转换器，或直接回车执行所有转换器
2. **配置选项**: 对于支持配置的转换器（如 MKV 转 MP4），会提示配置编码器、预设、质量等参数

### 示例流程

```
=== Clipify 视频转换工具 ===

目标目录：C:\Videos

可用的转换器：
1. TS转MP4转换器 (.ts -> .mp4)
2. MKV转MP4转换器(H.264-软件编码) (.mkv -> .mp4)

请选择转换器编号（直接回车执行所有转换器）：2

将执行：MKV转MP4转换器(H.264-软件编码)

===== 开始执行：MKV转MP4转换器(H.264-软件编码) =====

配置 MKV转MP4转换器(H.264-软件编码)
==================================================

请选择视频编码器：
1. 软件编码 (libx264) - 兼容性最好，质量最佳
2. NVIDIA 硬件编码 (h264_nvenc) - 需要 NVIDIA 显卡，速度快
3. Intel 硬件编码 (h264_qsv) - 需要 Intel 显卡/CPU，速度快
4. AMD 硬件编码 (h264_amf) - 需要 AMD 显卡，速度快
选择 (1-4) [默认: 1]: 2

请选择编码预设（速度/质量平衡）：
1. ultrafast - 极快（质量较低）
2. fast - 快速
3. medium - 中等（推荐）
4. slow - 慢（质量较好）
5. veryslow - 很慢（质量最好）
选择 (1-5) [默认: 3]: 2

硬件编码质量控制：
1. 自动 (默认，由编码器决定)
2. 指定码率 (kbps)
选择 (1-2) [默认: 1]: 2
请输入目标码率 (kbps, 例如 5000): 8000

自动覆盖同名文件? (Y/n) [默认: Y]: 

配置完成！
编码器: h264_nvenc
预设: fast
码率: 8000 kbps
自动覆盖: 是

找到 3 个 .mkv 文件
处理文件：C:\Videos\video1.mkv
...
```

## 硬件编码说明

### NVIDIA 硬件编码 (h264_nvenc)

**要求**:
- NVIDIA 显卡（GTX 600 系列及以上）
- 最新的 NVIDIA 驱动

**优点**:
- 编码速度非常快（比软件编码快 5-10 倍）
- CPU 占用低

**缺点**:
- 质量略低于软件编码
- 文件大小可能略大

### Intel 硬件编码 (h264_qsv)

**要求**:
- Intel CPU（第 2 代酷睿及以上）或 Intel 集成显卡
- Intel Media SDK

**优点**:
- 编码速度快
- 低功耗

**缺点**:
- 质量一般

### AMD 硬件编码 (h264_amf)

**要求**:
- AMD 显卡（Radeon HD 7000 系列及以上）
- AMD 驱动

**优点**:
- 编码速度快
- AMD 用户的最佳选择

**缺点**:
- 质量略低于软件编码

## 扩展开发

### 添加新的转换器

1. 在 `Converters/Options/` 目录下创建选项类：

```csharp
public class MyConverterOptions : ConverterOptions
{
    // 添加自定义选项
    public string CustomOption { get; set; } = "default";
}
```

2. 在 `Converters/` 目录下创建转换器类：

```csharp
public class MyConverter : BaseConverter
{
    private readonly MyConverterOptions _options = new();
    
    public override string Name => "我的转换器";
    public override string SourceExtension => ".source";
    public override string TargetExtension => ".target";
    public override ConverterOptions Options => _options;

    protected override string GenerateFfmpegArguments(string sourceFile, string targetFile)
    {
        // 生成 FFmpeg 命令参数
        return $"-i \"{sourceFile}\" ... \"{targetFile}\"";
    }
    
    public override void Configure()
    {
        // 可选：实现交互式配置
    }
}
```

3. 在 `Program.cs` 中注册：

```csharp
var converters = new List<IConverter> {
    new TsToMp4Converter(),
    new MkvToMp4Converter(),
    new MyConverter()  // 添加新转换器
};
```

## 注意事项

1. **FFmpeg 依赖**: 确保系统已安装 FFmpeg 并添加到环境变量 PATH 中
2. **硬件编码**: 使用硬件编码前请确认硬件支持
3. **文件覆盖**: 默认会自动覆盖同名文件，请注意备份
4. **编码质量**: 
   - 软件编码质量最好，但速度慢
   - 硬件编码速度快，但质量略低
   - 根据需求选择合适的编码器

## 许可证

本项目使用 GPLv3 许可证。

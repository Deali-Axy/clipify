using ClipifyConveter.Converters.Options;

namespace ClipifyConveter.Converters;

/// <summary>
/// MKV 转 MP4 转换器（转换为 H.264 编码）
/// </summary>
public class MkvToMp4Converter : BaseConverter {
    private readonly MkvToMp4Options _options = new();

    public override string Name => $"MKV转MP4转换器(H.264-{GetEncoderDisplayName()})";
    public override string SourceExtension => ".mkv";
    public override string TargetExtension => ".mp4";
    public override ConverterOptions Options => _options;

    private string GetEncoderDisplayName() {
        return _options.VideoEncoder switch {
            VideoEncoder.Software_X264 => "软件编码",
            VideoEncoder.Hardware_NVENC => "NVIDIA硬件",
            VideoEncoder.Hardware_QSV => "Intel硬件",
            VideoEncoder.Hardware_AMF => "AMD硬件",
            _ => "软件编码"
        };
    }

    public override void Configure() {
        Console.WriteLine($"\n配置 {Name}");
        Console.WriteLine("=".PadLeft(50, '='));

        // 选择编码器
        Console.WriteLine("\n请选择视频编码器：");
        Console.WriteLine("1. 软件编码 (libx264) - 兼容性最好，质量最佳");
        Console.WriteLine("2. NVIDIA 硬件编码 (h264_nvenc) - 需要 NVIDIA 显卡，速度快");
        Console.WriteLine("3. Intel 硬件编码 (h264_qsv) - 需要 Intel 显卡/CPU，速度快");
        Console.WriteLine("4. AMD 硬件编码 (h264_amf) - 需要 AMD 显卡，速度快");
        Console.Write($"选择 (1-4) [默认: 1]: ");

        var encoderChoice = Console.ReadLine();
        _options.VideoEncoder = encoderChoice switch {
            "2" => VideoEncoder.Hardware_NVENC,
            "3" => VideoEncoder.Hardware_QSV,
            "4" => VideoEncoder.Hardware_AMF,
            _ => VideoEncoder.Software_X264
        };

        // 选择预设
        Console.WriteLine("\n请选择编码预设（速度/质量平衡）：");
        Console.WriteLine("1. ultrafast - 极快（质量较低）");
        Console.WriteLine("2. fast - 快速");
        Console.WriteLine("3. medium - 中等（推荐）");
        Console.WriteLine("4. slow - 慢（质量较好）");
        Console.WriteLine("5. veryslow - 很慢（质量最好）");
        Console.Write($"选择 (1-5) [默认: 3]: ");

        var presetChoice = Console.ReadLine();
        _options.Preset = presetChoice switch {
            "1" => EncodePreset.UltraFast,
            "2" => EncodePreset.Fast,
            "4" => EncodePreset.Slow,
            "5" => EncodePreset.VerySlow,
            _ => EncodePreset.Medium
        };

        // 质量设置
        if (_options.VideoEncoder == VideoEncoder.Software_X264) {
            Console.Write($"\n请输入视频质量 (CRF: 18-28, 越小质量越好) [默认: 23]: ");
            var qualityInput = Console.ReadLine();
            if (int.TryParse(qualityInput, out var quality) && quality >= 0 && quality <= 51) {
                _options.Quality = quality;
            }
        }
        else {
            // 硬件编码使用码率控制
            Console.WriteLine("\n硬件编码质量控制：");
            Console.WriteLine("1. 自动 (默认，由编码器决定)");
            Console.WriteLine("2. 指定码率 (kbps)");
            Console.Write("选择 (1-2) [默认: 1]: ");

            var bitrateChoice = Console.ReadLine();
            if (bitrateChoice == "2") {
                Console.Write("请输入目标码率 (kbps, 例如 5000): ");
                var bitrateInput = Console.ReadLine();
                if (int.TryParse(bitrateInput, out var bitrate) && bitrate > 0) {
                    _options.Bitrate = bitrate;
                }
            }
        }

        // 覆盖设置
        Console.Write($"\n自动覆盖同名文件? (Y/n) [默认: Y]: ");
        var overwriteInput = Console.ReadLine();
        _options.AutoOverwrite = string.IsNullOrWhiteSpace(overwriteInput) ||
                                 !overwriteInput.Trim().Equals("n", StringComparison.OrdinalIgnoreCase);

        Console.WriteLine("\n配置完成！");
        Console.WriteLine($"编码器: {_options.GetVideoEncoderName()}");
        Console.WriteLine($"预设: {_options.GetPresetName()}");
        if (_options.VideoEncoder == VideoEncoder.Software_X264) {
            Console.WriteLine($"质量 (CRF): {_options.Quality}");
        }
        else if (_options.Bitrate > 0) {
            Console.WriteLine($"码率: {_options.Bitrate} kbps");
        }

        Console.WriteLine($"自动覆盖: {(_options.AutoOverwrite ? "是" : "否")}");
        Console.WriteLine();
    }

    protected override List<string> GenerateFfmpegArguments(string sourceFile, string targetFile) {
        var args = new List<string> {
            "-i",
            NormalizePath(sourceFile)
        };
            
        // 视频编码器
        var videoEncoder = _options.GetVideoEncoderName();
        args.Add("-c:v");
        args.Add(videoEncoder);
            
        // 音频编码器
        args.Add("-c:a");
        args.Add(_options.AudioCodec);
            
        // 预设（仅软件编码器支持）
        if (!_options.IsHardwareEncoder()) {
            var preset = _options.GetPresetName();
            args.Add("-preset");
            args.Add(preset);
        }
            
        // 质量控制
        if (_options.IsHardwareEncoder()) {
            // 硬件编码器的质量控制
            switch (_options.VideoEncoder) {
                case VideoEncoder.Hardware_NVENC:
                    // NVIDIA 编码器
                    if (_options.Bitrate > 0) {
                        args.Add("-b:v");
                        args.Add($"{_options.Bitrate}k");
                    }
                    else {
                        args.Add("-rc");
                        args.Add("vbr");
                        args.Add("-cq");
                        args.Add("23");
                    }
                    // NVENC 预设
                    var nvencPreset = _options.Preset switch {
                        EncodePreset.UltraFast => "p1",
                        EncodePreset.Fast => "p3",
                        EncodePreset.Medium => "p4",
                        EncodePreset.Slow => "p6",
                        EncodePreset.VerySlow => "p7",
                        _ => "p4"
                    };
                    args.Add("-preset");
                    args.Add(nvencPreset);
                    break;
                        
                case VideoEncoder.Hardware_QSV:
                    // Intel QSV 编码器
                    if (_options.Bitrate > 0) {
                        args.Add("-b:v");
                        args.Add($"{_options.Bitrate}k");
                    }
                    else {
                        args.Add("-global_quality");
                        args.Add("23");
                    }
                    // QSV 预设
                    var qsvPreset = _options.Preset switch {
                        EncodePreset.UltraFast => "veryfast",
                        EncodePreset.Fast => "fast",
                        EncodePreset.Medium => "medium",
                        EncodePreset.Slow => "slow",
                        EncodePreset.VerySlow => "veryslow",
                        _ => "medium"
                    };
                    args.Add("-preset");
                    args.Add(qsvPreset);
                    break;
                        
                case VideoEncoder.Hardware_AMF:
                    // AMD AMF 编码器（不支持 preset 参数）
                    if (_options.Bitrate > 0) {
                        args.Add("-b:v");
                        args.Add($"{_options.Bitrate}k");
                    }
                    else {
                        // AMF 使用 CQP 模式
                        args.Add("-rc");
                        args.Add("cqp");
                        args.Add("-qp_i");
                        args.Add("23");
                        args.Add("-qp_p");
                        args.Add("23");
                    }
                    // AMF 质量设置
                    var amfQuality = _options.Preset switch {
                        EncodePreset.UltraFast or EncodePreset.Fast => "speed",
                        EncodePreset.Slow or EncodePreset.VerySlow => "quality",
                        _ => "balanced"
                    };
                    args.Add("-quality");
                    args.Add(amfQuality);
                    break;
            }
        }
        else {
            // 软件编码使用 CRF
            args.Add("-crf");
            args.Add(_options.Quality.ToString());
        }
            
        // 覆盖标志
        args.Add(_options.AutoOverwrite ? "-y" : "-n");
            
        // 目标文件
        args.Add(NormalizePath(targetFile));
            
        return args;
    }
}
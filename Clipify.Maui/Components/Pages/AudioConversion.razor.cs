using Clipify.Maui.Components.Shared;
using FFmpeg.NET;

namespace Clipify.Maui.Components.Pages;

public partial class AudioConversion
{
    public string? VideoPath { get; set; }
    public string? OutputDir { get; set; }
    public MetaData? MetaData { get; set; }
    public VideoSelectorPanel VideoSelector { get; set; }
    
    public string OutputFormat { get; set; } = "mp3";
    public bool UseDefaultQuality { get; set; } = true;
    public string Bitrate { get; set; } = "192";

    public string? OutputPath => string.IsNullOrWhiteSpace(OutputDir) || string.IsNullOrWhiteSpace(VideoPath) 
        ? null 
        : Path.Combine(OutputDir, $"{Path.GetFileNameWithoutExtension(VideoPath)}.{OutputFormat}");

    public string? FFmpegCommand => string.IsNullOrWhiteSpace(GenerateFFmpegArguments()) 
        ? null 
        : $"ffmpeg {GenerateFFmpegArguments()}";

    public VideoExportDialog ExportDialogRef { get; set; }

    protected override Task OnInitializedAsync()
    {
        DialogService.OnDirSelected += UpdateSelectedDir;
        return base.OnInitializedAsync();
    }

    private string? GenerateFFmpegArguments()
    {
        if (string.IsNullOrWhiteSpace(VideoPath) || string.IsNullOrWhiteSpace(OutputDir))
        {
            return null;
        }

        var args = "-y -hide_banner -progress pipe:1";
        
        args += $" -i \"{VideoPath}\"";
        
        // 根据用户选择决定音频质量
        if (UseDefaultQuality)
        {
            args += " -c:a copy";
        }
        else
        {
            // 针对不同格式选择适合的编码器
            switch (OutputFormat.ToLower())
            {
                case "mp3":
                    args += $" -c:a libmp3lame -b:a {Bitrate}k";
                    break;
                case "aac":
                    args += $" -c:a aac -b:a {Bitrate}k";
                    break;
                case "ogg":
                    args += $" -c:a libvorbis -b:a {Bitrate}k";
                    break;
                case "flac":
                    args += " -c:a flac -compression_level 8";
                    break;
                case "wav":
                    args += " -c:a pcm_s16le";
                    break;
                default:
                    args += " -c:a copy";
                    break;
            }
        }
        
        // 仅提取音频
        args += " -vn";
        
        args += $" \"{OutputPath}\"";
        return args;
    }

    private async Task OpenDirDialog()
    {
        await DialogService.OpenDirAsync();
    }

    private async Task UpdateSelectedDir(string path)
    {
        OutputDir = path;
        Console.WriteLine($"保存目录：{path}");
        await InvokeAsync(StateHasChanged);
    }

    private async Task ExportAudio(CancellationToken? cancellationToken = null)
    {
        if (string.IsNullOrWhiteSpace(VideoPath))
        {
            await MsgService.Error("未选择文件");
            return;
        }

        if (string.IsNullOrWhiteSpace(OutputDir))
        {
            await MsgService.Error("没有选择输出目录");
            return;
        }

        if (MetaData == null)
        {
            await MsgService.Error("视频读取失败");
            return;
        }

        // 检查视频是否包含音频流
        if (MetaData.AudioData == null)
        {
            await MsgService.Error("该视频没有音频流");
            return;
        }

        var args = GenerateFFmpegArguments();
        if (string.IsNullOrWhiteSpace(args))
        {
            await MsgService.Error("无法生成ffmpeg命令参数");
            return;
        }

        await ExportDialogRef.ExportVideo(MetaData, OutputDir, args, cancellationToken);
    }
} 
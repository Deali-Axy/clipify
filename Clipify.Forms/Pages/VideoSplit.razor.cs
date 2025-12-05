using System.Diagnostics;
using Clipify.Forms.Components;
using FFmpeg.NET;
using InputFile = FFmpeg.NET.InputFile;

namespace Clipify.Forms.Pages;

public partial class VideoSplit {
    public string? VideoPath { get; set; }
    public string? OutputDir { get; set; }
    public MetaData? MetaData { get; set; }
    public VideoSelectorPanel VideoSelector { get; set; }

    public bool EnableStartTime { get; set; }
    public bool EnableEndTime { get; set; }
    
    public int StartHour { get; set; }
    public int StartMinute { get; set; }
    public int StartSecond { get; set; }
    
    public int EndHour { get; set; }
    public int EndMinute { get; set; }
    public int EndSecond { get; set; }
    
    public bool UseSameQuality { get; set; } = true;
    public string OutputFormat { get; set; } = "mp4";

    public string? OutputPath {
        get {
            if (string.IsNullOrWhiteSpace(OutputDir)) {
                return null;
            }

            var filename = $"{Path.GetFileNameWithoutExtension(VideoPath)}_cut.{OutputFormat}";
            var path = Path.Combine(OutputDir, filename);
            return path;
        }
    }

    public string? FFmpegCommand {
        get {
            var args = GenerateFFmpegArguments();
            return string.IsNullOrWhiteSpace(args) ? null : $"ffmpeg {args}";
        }
    }

    public VideoExportDialog ExportDialogRef { get; set; }

    protected override Task OnInitializedAsync() {
        DialogService.OnDirSelected += UpdateSelectedDir;
        return base.OnInitializedAsync();
    }

    private string? GenerateFFmpegArguments() {
        if (string.IsNullOrWhiteSpace(VideoPath) || string.IsNullOrWhiteSpace(OutputDir)) {
            return null;
        }

        var args = "-y -hide_banner -progress pipe:1";

        // 处理开始时间和结束时间
        if (EnableStartTime) {
            var startTime = $"{StartHour}:{StartMinute}:{StartSecond}";
            args += $" -ss {startTime}";
        }

        // 如果仅设置结束时间而没有开始时间，则从0开始
        if (EnableEndTime && !EnableStartTime) {
            args += " -ss 0:0:0";
        }

        // 计算时长（当同时有开始和结束时间）
        if (EnableStartTime && EnableEndTime) {
            var startSeconds = StartHour * 3600 + StartMinute * 60 + StartSecond;
            var endSeconds = EndHour * 3600 + EndMinute * 60 + EndSecond;
            
            // 确保结束时间大于开始时间
            if (endSeconds > startSeconds) {
                var durationSeconds = endSeconds - startSeconds;
                var durationHour = durationSeconds / 3600;
                var durationMinute = (durationSeconds % 3600) / 60;
                var durationSecond = durationSeconds % 60;
                
                args += $" -t {durationHour}:{durationMinute}:{durationSecond}";
            }
        } 
        // 如果只有结束时间，则直接使用结束时间作为时长
        else if (EnableEndTime) {
            var duration = $"{EndHour}:{EndMinute}:{EndSecond}";
            args += $" -t {duration}";
        }

        args += $" -i \"{VideoPath}\"";

        // 根据选择决定是否保持原视频质量
        if (UseSameQuality) {
            args += " -c copy";
        }

        args += $" \"{OutputPath}\"";
        return args;
    }

    private async Task OpenDirDialog() {
        await DialogService.OpenDirAsync();
    }

    private async Task UpdateSelectedDir(string path) {
        OutputDir = path;
        Console.WriteLine($"保存目录：{path}");
        await InvokeAsync(StateHasChanged);
    }

    private async Task ExportVideo(CancellationToken? cancellationToken = null) {
        var args = GenerateFFmpegArguments();
        if (string.IsNullOrWhiteSpace(VideoPath)) {
            await MsgService.Error("未选择文件");
            return;
        }

        if (string.IsNullOrWhiteSpace(OutputDir)) {
            await MsgService.Error("没有选择输出目录");
            return;
        }

        if (MetaData == null) {
            await MsgService.Error("视频读取失败");
            return;
        }

        if (string.IsNullOrWhiteSpace(args)) {
            await MsgService.Error("无法生成ffmpeg命令参数");
            return;
        }

        // 检查时间设置的有效性
        if (EnableStartTime && EnableEndTime) {
            var startSeconds = StartHour * 3600 + StartMinute * 60 + StartSecond;
            var endSeconds = EndHour * 3600 + EndMinute * 60 + EndSecond;
            
            if (endSeconds <= startSeconds) {
                await MsgService.Error("结束时间必须大于开始时间");
                return;
            }
            
            // 检查结束时间是否超过视频总长度
            var totalSeconds = (int)MetaData.Duration.TotalSeconds;
            if (endSeconds > totalSeconds) {
                await MsgService.Warning($"结束时间超过视频总长度 ({totalSeconds / 3600}:{(totalSeconds % 3600) / 60}:{totalSeconds % 60})");
            }
        }

        await ExportDialogRef.ExportVideo(MetaData, OutputDir, args, cancellationToken);
    }
} 
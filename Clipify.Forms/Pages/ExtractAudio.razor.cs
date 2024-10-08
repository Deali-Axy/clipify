using System.Diagnostics;
using Clipify.Forms.Components;
using FFmpeg.NET;
using InputFile = FFmpeg.NET.InputFile;

namespace Clipify.Forms.Pages;

public partial class ExtractAudio {
    public string? VideoPath { get; set; }
    public string? OutputDir { get; set; }

    public string? OutputPath {
        get {
            if (string.IsNullOrWhiteSpace(OutputDir)) {
                return null;
            }

            var filename = $"{Path.GetFileNameWithoutExtension(VideoPath)}.{OutputFormat}";
            var path = Path.Combine(OutputDir, filename);
            return path;
        }
    }

    public string? Thumbnail { get; set; }
    public string OutputFormat { get; set; } = "mp4";
    public MetaData? MetaData { get; set; }

    public string? FFmpegCommand {
        get {
            var args = GenerateFFmpegArguments();
            return string.IsNullOrWhiteSpace(args) ? null : $"ffmpeg {args}";
        }
    }

    public VideoExportDialog ExportDialogRef { get; set; }

    protected override Task OnInitializedAsync() {
        DialogService.OnFileSelected += UpdateSelectedFile;
        DialogService.OnDirSelected += UpdateSelectedDir;
        return base.OnInitializedAsync();
    }

    private string? GenerateFFmpegArguments() {
        if (string.IsNullOrWhiteSpace(VideoPath) || string.IsNullOrWhiteSpace(OutputDir)) {
            return null;
        }

        return $"-y -hide_banner -progress pipe:1 -i \"{VideoPath}\" -map a -c:a \"{OutputPath}\"";
    }

    private async Task OpenFileDialog() {
        await DialogService.OpenFileAsync();
    }

    private async Task OpenDirDialog() {
        await DialogService.OpenDirAsync();
    }

    private void OpenFile() {
        if (string.IsNullOrWhiteSpace(VideoPath)) {
            MsgService.Error("请先打开文件！");
            return;
        }

        if (!Path.Exists(VideoPath)) {
            MsgService.Error("文件不存在！");
            return;
        }

        Process.Start(new ProcessStartInfo(VideoPath) {
            UseShellExecute = true
        });
        MsgService.Info($"打开文件: {VideoPath}");
    }

    private async Task UpdateSelectedFile(string path) {
        VideoPath = path;
        MetaData = await VideoService.FFmpeg.GetMetaDataAsync(new InputFile(VideoPath), CancellationToken.None);
        if (MetaData.VideoData != null) {
            Thumbnail = await VideoService.GenerateThumbnailAsync(VideoPath);
        }

        await InvokeAsync(StateHasChanged);
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

        await ExportDialogRef.ExportVideo(MetaData, OutputDir, args, cancellationToken);
    }
}
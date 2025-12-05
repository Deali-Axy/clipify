using Clipify.Maui.Components.Shared;
using FFmpeg.NET;
using Microsoft.AspNetCore.Components;
using Clipify.Core.Interfaces;

namespace Clipify.Maui.Components.Pages;

public partial class ExtractAudio : ComponentBase {
    [Inject] private IDialogService DialogService { get; set; }
    [Inject] private IMessageService MsgService { get; set; }
    [Inject] private IVideoService VideoService { get; set; }
    
    public string? VideoPath { get; set; }
    public string? OutputDir { get; set; }
    public MetaData? MetaData { get; set; }
    public VideoSelectorPanel VideoSelector { get; set; }

    public string OutputFormat { get; set; } = "mp3";

    public string? OutputPath => string.IsNullOrWhiteSpace(OutputDir) || string.IsNullOrWhiteSpace(VideoPath)
        ? null
        : Path.Combine(OutputDir, $"{Path.GetFileNameWithoutExtension(VideoPath)}.{OutputFormat}");

    public string? FFmpegCommand => $"ffmpeg -y -hide_banner -i \"{VideoPath}\" -map a -c:a copy \"{OutputPath}\"";

    public VideoExportDialog ExportDialogRef { get; set; }

    protected override Task OnInitializedAsync() {
        DialogService.OnDirSelected += UpdateSelectedDir;
        return base.OnInitializedAsync();
    }

    private string? GenerateFFmpegArguments() {
        if (string.IsNullOrWhiteSpace(VideoPath) || string.IsNullOrWhiteSpace(OutputDir)) {
            return null;
        }

        return $"-y -hide_banner -progress pipe:1 -i \"{VideoPath}\" -map a -c:a copy \"{OutputPath}\"";
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

        // 检查视频是否包含音频流
        if (MetaData.AudioData == null) {
            await MsgService.Error("该视频没有音频流");
            return;
        }

        var args = GenerateFFmpegArguments();
        if (string.IsNullOrWhiteSpace(args)) {
            await MsgService.Error("无法生成ffmpeg命令参数");
            return;
        }

        await ExportDialogRef.ExportVideo(MetaData, OutputDir, args, cancellationToken);
    }
}
using FFmpeg.NET;
using FFmpeg.NET.Events;
using Microsoft.AspNetCore.Components;

namespace Clipify.Forms.Components;

public partial class VideoExportDialog : ComponentBase {
    bool _visible = false;

    public class FFmpegStatus {
        public StatusEnum Status { get; set; }
        public string Message { get; set; }
        public double Progress { get; set; } = 0;
    }

    public enum StatusEnum {
        None,
        Running,
        Finish,
        Error
    }

    public MetaData MetaData { get; set; }
    public string OutputDir { get; set; }
    public FFmpegStatus Status { get; set; } = new() { Status = StatusEnum.None, Message = "没有任何操作" };
    public bool CanClose => Status.Status != StatusEnum.Running;

    private async void OnProgress(object? sender, ConversionProgressEventArgs e) {
        Status.Status = StatusEnum.Running;
        // 计算进度百分比
        Status.Progress = Math.Round(e.ProcessedDuration.TotalSeconds / MetaData.Duration.TotalSeconds * 100, 2);
        await InvokeAsync(StateHasChanged);
    }

    private async void OnData(object? sender, ConversionDataEventArgs e) {
        Console.WriteLine("[onData] {0}", e.Data);
        Status.Status = StatusEnum.Running;
        Status.Message = $"正在导出视频, {e.Data}";
        await InvokeAsync(StateHasChanged);
    }

    private async void OnComplete(object? sender, ConversionCompleteEventArgs e) {
        Console.WriteLine("Completed conversion");
        Status.Status = StatusEnum.Finish;
        Status.Message = "已完成操作";
        await InvokeAsync(StateHasChanged);
    }

    private async void OnError(object? sender, ConversionErrorEventArgs e) {
        Console.WriteLine("Error: {0}\n{1}", e.Exception.ExitCode, e.Exception.InnerException);
        Status.Status = StatusEnum.Error;
        Status.Message = $"发生错误，ExitCode: {e.Exception.ExitCode}，错误信息: {e.Exception.Message}";
        await InvokeAsync(StateHasChanged);
    }

    public async Task ExportVideo(MetaData metaData, string outputDir, string arguments,
        CancellationToken? cancellationToken = null) {
        _visible = true;

        MetaData = metaData;
        OutputDir = outputDir;

        VideoService.FFmpeg.Progress += OnProgress;
        VideoService.FFmpeg.Data += OnData;
        VideoService.FFmpeg.Complete += OnComplete;
        VideoService.FFmpeg.Error += OnError;

        Console.WriteLine($"执行命令：{arguments}");
        await InvokeAsync(StateHasChanged);

        Status.Status = StatusEnum.Running;
        Status.Message = "开始导出视频";

        try {
            await VideoService.FFmpeg.ExecuteAsync(arguments, cancellationToken ?? CancellationToken.None);
        }
        finally {
            // 导出完成后取消订阅事件，防止重复触发
            VideoService.FFmpeg.Progress -= OnProgress;
            VideoService.FFmpeg.Data -= OnData;
            VideoService.FFmpeg.Complete -= OnComplete;
            VideoService.FFmpeg.Error -= OnError;
        }
    }
}
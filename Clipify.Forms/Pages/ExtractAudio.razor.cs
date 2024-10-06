using System.Diagnostics;
using Clipify.Forms.Components;
using FFmpeg.NET;
using Microsoft.AspNetCore.Components.Forms;
using MudBlazor;
using InputFile = FFmpeg.NET.InputFile;

namespace Clipify.Forms.Pages;

public partial class ExtractAudio {
    public string? VideoPath { get; set; }
    public string? OutputDir { get; set; }
    public string? Thumbnail { get; set; }
    public string OutputFormat { get; set; } = "mp4";
    public MetaData? MetaData { get; set; }

    public string? FFmpegCommand => $"ffmpeg {GenerateFFmpegArguments()}";

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

        var filename = $"{Path.GetFileNameWithoutExtension(VideoPath)}.{OutputFormat}";
        var path = Path.Combine(OutputDir, filename);
        
        return $"-y -hide_banner -i \"{VideoPath}\" -q:a 0 -map a \"{path}\"";
    }

    private async Task OpenFileDialog() {
        await DialogService.OpenFileAsync();
    }

    private async Task OpenDirDialog() {
        await DialogService.OpenDirAsync();
    }

    private void OpenFile() {
        if (string.IsNullOrWhiteSpace(VideoPath)) {
            Snackbar.Add("请先打开文件！", Severity.Error);
            return;
        }

        if (!Path.Exists(VideoPath)) {
            Snackbar.Add("文件不存在！", Severity.Error);
            return;
        }

        Process.Start(new ProcessStartInfo(VideoPath) {
            UseShellExecute = true
        });
        Snackbar.Add($"打开文件: {VideoPath}", Severity.Info);
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
        if (!string.IsNullOrWhiteSpace(args)) {
            await ExportDialogRef.ExportVideo(args, cancellationToken);   
        }
    }
}
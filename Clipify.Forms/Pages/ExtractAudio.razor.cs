using FFmpeg.NET;
using Microsoft.AspNetCore.Components.Forms;
using InputFile = FFmpeg.NET.InputFile;

namespace Clipify.Forms.Pages;

public partial class ExtractAudio {
    public string? VideoPath { get; set; }
    public string? OutputDir { get; set; }
    public string? Thumbnail { get; set; }
    public string OutputFormat { get; set; } = "mp4";
    public MetaData? MetaData { get; set; }

    public string? FFmpegCommand => GenerateFFmpegCommand();

    protected override Task OnInitializedAsync() {
        DialogService.OnFileSelected += UpdateSelectedFile;
        DialogService.OnDirSelected += UpdateSelectedDir;
        return base.OnInitializedAsync();
    }

    private string? GenerateFFmpegCommand() {
        if (string.IsNullOrWhiteSpace(VideoPath) || string.IsNullOrWhiteSpace(OutputDir)) {
            return null;
        }

        var filename = $"{Path.GetFileNameWithoutExtension(VideoPath)}.{OutputFormat}";
        var path = Path.Combine(OutputDir, filename);

        return $"ffmpeg -y -hide_banner -i {VideoPath} -q:a 0 -map a {path}";
    }

    private async Task OpenFileDialog() {
        await DialogService.OpenFileAsync();
    }

    private async Task OpenDirDialog() {
        await DialogService.OpenDirAsync();
    }

    private async Task UpdateSelectedFile(string path) {
        VideoPath = path;
        MetaData = await _videoService.FFmpeg.GetMetaDataAsync(new InputFile(VideoPath), CancellationToken.None);
        if (MetaData.VideoData != null) {
            Thumbnail = await _videoService.GenerateThumbnailAsync(VideoPath);
        }

        await InvokeAsync(StateHasChanged);
    }

    private async Task UpdateSelectedDir(string path) {
        OutputDir = path;
        Console.WriteLine($"保存目录：{path}");
        await InvokeAsync(StateHasChanged);
    }
}
using System.Diagnostics;
using Clipify.Forms.Services;
using FFmpeg.NET;
using Microsoft.AspNetCore.Components;

namespace Clipify.Forms.Components;

public partial class VideoSelectorPanel {
    [Parameter] public string VideoPath { get; set; } = string.Empty;

    [Parameter] public bool ShowActions { get; set; } = true;
    
    private MetaData? MetaData { get; set; }
    private string? Thumbnail { get; set; }

    protected override async Task OnParametersSetAsync() {
        if (!string.IsNullOrWhiteSpace(VideoPath)) {
            await UpdateVideoInfo();
        }
    }

    protected override Task OnInitializedAsync() {
        DialogService.OnFileSelected += UpdateSelectedFile;
        return base.OnInitializedAsync();
    }
    
    private async Task UpdateSelectedFile(string path) {
        VideoPath = path;
        await InvokeAsync(StateHasChanged);
    }

    private async Task UpdateVideoInfo() {
        MetaData = await VideoService.FFmpeg.GetMetaDataAsync(new InputFile(VideoPath), CancellationToken.None);
        if (MetaData?.VideoData != null) {
            Thumbnail = await VideoService.GenerateThumbnailAsync(VideoPath);
        }
    }
    
    private async Task OpenFileDialog() {
        await DialogService.OpenFileAsync();
    }

    private void OpenCurrentFile() {
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
}
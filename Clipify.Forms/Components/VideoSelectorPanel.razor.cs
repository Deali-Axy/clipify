using System.Diagnostics;
using Clipify.Forms.Services;
using FFmpeg.NET;
using Microsoft.AspNetCore.Components;

namespace Clipify.Forms.Components;

public partial class VideoSelectorPanel {
    [Parameter] public string VideoPath { get; set; } = string.Empty;

    [Parameter] public bool ShowActions { get; set; } = true;

    [Parameter] public MetaData? MetaData { get; set; }

    [Parameter] public string? Thumbnail { get; set; }

    [Parameter] public EventCallback<string> VideoPathChanged { get; set; }

    [Parameter] public EventCallback<MetaData> MetaDataChanged { get; set; }
    
    private string _previousVideoPath = string.Empty;

    protected override async Task OnParametersSetAsync() {
        // 只有当VideoPath从空变为非空，或者VideoPath发生变化时才更新视频信息
        if (!string.IsNullOrWhiteSpace(VideoPath) && VideoPath != _previousVideoPath) {
            _previousVideoPath = VideoPath;
            await UpdateVideoInfo();
        } else if (string.IsNullOrWhiteSpace(VideoPath) && !string.IsNullOrWhiteSpace(_previousVideoPath)) {
            // VideoPath从非空变为空，更新_previousVideoPath
            _previousVideoPath = VideoPath;
        }
    }

    protected override Task OnInitializedAsync() {
        DialogService.OnFileSelected += UpdateSelectedFile;
        return base.OnInitializedAsync();
    }
    
    public async Task ReSelectVideo() {
        MetaData = null;
        await MetaDataChanged.InvokeAsync(MetaData);
        Thumbnail = null;
        VideoPath = string.Empty;
        _previousVideoPath = string.Empty;
        await VideoPathChanged.InvokeAsync(VideoPath);
        await InvokeAsync(StateHasChanged);
    }

    private async Task UpdateSelectedFile(string path) {
        VideoPath = path;
        await VideoPathChanged.InvokeAsync(path);
        await UpdateVideoInfo();
        await InvokeAsync(StateHasChanged);
    }

    private async Task UpdateVideoInfo() {
        if (string.IsNullOrWhiteSpace(VideoPath)) {
            return;
        }
        
        MetaData = await VideoService.FFmpeg.GetMetaDataAsync(new InputFile(VideoPath), CancellationToken.None);
        await MetaDataChanged.InvokeAsync(MetaData);
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
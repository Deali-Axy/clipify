using FFmpeg.NET;
using Microsoft.AspNetCore.Components;

namespace Clipify.Maui.Components.Shared;

public partial class VideoSelectorPanel
{
    [Parameter]
    public string? VideoPath { get; set; }
    
    [Parameter]
    public EventCallback<string> VideoPathChanged { get; set; }
    
    [Parameter]
    public MetaData? MetaData { get; set; }
    
    [Parameter]
    public EventCallback<MetaData> MetaDataChanged { get; set; }

    [Parameter]
    public bool ShowActions { get; set; } = true;
    
    private string? Thumbnail { get; set; }
    
    protected override async Task OnParametersSetAsync()
    {
        await base.OnParametersSetAsync();
        
        if (!string.IsNullOrWhiteSpace(VideoPath) && MetaData == null)
        {
            await UpdateVideoMetadataAsync();
        }
    }

    private async Task OpenFileDialog()
    {
        var filePath = await DialogService.OpenFileAsync();
        
        if (!string.IsNullOrWhiteSpace(filePath))
        {
            VideoPath = filePath;
            await VideoPathChanged.InvokeAsync(VideoPath);
            await UpdateVideoMetadataAsync();
        }
    }
    
    private void OpenCurrentFile()
    {
        if (!string.IsNullOrWhiteSpace(VideoPath))
        {
            try
            {
                // 使用系统默认程序打开文件
#if WINDOWS
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = VideoPath,
                    UseShellExecute = true
                });
#else
                // 在其他平台上，可能需要使用特定的实现方式
                Launcher.OpenAsync(new Uri($"file://{VideoPath}"));
#endif
            }
            catch (Exception ex)
            {
                MsgService.Error($"无法打开文件: {ex.Message}");
            }
        }
    }
    
    public async Task ReSelectVideo()
    {
        VideoPath = string.Empty;
        MetaData = null;
        Thumbnail = null;
        
        await VideoPathChanged.InvokeAsync(VideoPath);
        await MetaDataChanged.InvokeAsync(MetaData);
        
        await InvokeAsync(StateHasChanged);
    }
    
    private async Task UpdateVideoMetadataAsync()
    {
        if (string.IsNullOrWhiteSpace(VideoPath))
        {
            return;
        }
        
        try
        {
            // 获取视频元数据
            var metadata = await VideoService.FFmpeg.GetMetaDataAsync(new InputFile(VideoPath), CancellationToken.None);
            
            MetaData = metadata;
            await MetaDataChanged.InvokeAsync(MetaData);
            
            // 生成缩略图
            Thumbnail = await VideoService.GenerateThumbnailAsync(VideoPath);
            
            await InvokeAsync(StateHasChanged);
        }
        catch (Exception ex)
        {
            await MsgService.Error($"无法获取视频信息: {ex.Message}");
        }
    }
} 
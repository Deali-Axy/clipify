using System.Text.RegularExpressions;
using FFmpeg.NET;
using FFmpeg.NET.Events;
using Microsoft.AspNetCore.Components;
using Clipify.Core.Interfaces;

namespace Clipify.Maui.Components.Shared;

public partial class VideoExportDialog : ComponentBase {
    [Inject] private IMessageService MsgService { get; set; }
    [Inject] private IVideoService VideoService { get; set; }
    
    private bool _visible = false;
    private string? OutputDir { get; set; }

    public bool CanClose => Status.Status != StatusEnum.Running;

    private ExportStatus Status { get; set; } = new ExportStatus {
        Status = StatusEnum.None,
        Progress = 0,
        Message = "等待开始..."
    };

    public async Task ExportVideo(MetaData metaData, string outputDir, string ffmpegArguments,
        CancellationToken? cancellationToken = null) {
        OutputDir = outputDir;
        _visible = true;
        Status.Status = StatusEnum.Running;
        Status.Progress = 0;
        Status.Message = "正在准备导出...";

        await InvokeAsync(StateHasChanged);

        try {
            // 这里应该实际执行FFmpeg命令
            // 在MAUI中，我们需要进行一些适配

            // 模拟进度更新
            for (int i = 0; i <= 100; i += 5) {
                Status.Progress = i;
                Status.Message = $"正在导出: {i}%";
                await InvokeAsync(StateHasChanged);
                await Task.Delay(200); // 实际应用中，这应该由真实进度触发
            }

            Status.Status = StatusEnum.Finish;
            Status.Message = "导出完成！";

            await MsgService.Success("视频导出成功！");
        }
        catch (Exception ex) {
            Status.Status = StatusEnum.Error;
            Status.Message = $"导出出错: {ex.Message}";

            await MsgService.Error($"导出视频时出错: {ex.Message}");
        }

        await InvokeAsync(StateHasChanged);
    }

    private void OnConversionProgress(object sender, ConversionProgressEventArgs e) {
        Status.Progress = (int)(e.ProcessedDuration.TotalSeconds / e.TotalDuration.TotalSeconds * 100);
        Status.Message = $"正在导出: {Status.Progress}%";
        InvokeAsync(StateHasChanged);
    }
}

public class ExportStatus {
    public StatusEnum Status { get; set; }
    public int Progress { get; set; }
    public string Message { get; set; }
}

public enum StatusEnum {
    None,
    Running,
    Finish,
    Error
}
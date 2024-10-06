using FFmpeg.NET;
using Microsoft.AspNetCore.Components.Forms;
using InputFile = FFmpeg.NET.InputFile;

namespace Clipify.Forms.Pages;

public partial class ExtractAudio {
    public string? FilePath { get; set; }
    public string? OutputDir { get; set; }
    public MetaData? MetaData { get; set; }

    protected override Task OnInitializedAsync() {
        DialogService.OnFileSelected += UpdateSelectedFile;
        DialogService.OnDirSelected += UpdateSelectedDir;
        return base.OnInitializedAsync();
    }

    private async Task OpenFileDialog() {
        await DialogService.OpenFileAsync();
    }

    private async Task OpenDirDialog() {
        await DialogService.OpenDirAsync();
    }

    private async Task UpdateSelectedFile(string path) {
        FilePath = path;
        MetaData = await _videoService.FFmpeg.GetMetaDataAsync(new InputFile(FilePath), CancellationToken.None);
        await InvokeAsync(StateHasChanged);
    }

    private async Task UpdateSelectedDir(string path) {
        OutputDir = path;
        Console.WriteLine($"保存目录：{path}");
        await InvokeAsync(StateHasChanged);
    }

    private async Task LoadFiles(InputFileChangeEventArgs e) { }
}
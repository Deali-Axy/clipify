using FFmpeg.NET;
using Microsoft.AspNetCore.Components.Forms;
using InputFile = FFmpeg.NET.InputFile;

namespace Clipify.Forms.Pages;

public partial class ExtractAudio {
    public string? FilePath { get; set; }
    public MetaData? MetaData { get; set; }

    protected override Task OnInitializedAsync() {
        _fileDialogService.OnFileSelected += UpdateSelectedFile;
        return base.OnInitializedAsync();
    }

    private async Task OpenFileDialog() {
        await _fileDialogService.OpenFileDialogAsync();
    }

    private async void UpdateSelectedFile(string fileName) {
        FilePath = fileName;
        MetaData = await _videoService.FFmpeg.GetMetaDataAsync(new InputFile(FilePath), CancellationToken.None);
        await InvokeAsync(StateHasChanged);
    }

    private async Task LoadFiles(InputFileChangeEventArgs e) { }
}
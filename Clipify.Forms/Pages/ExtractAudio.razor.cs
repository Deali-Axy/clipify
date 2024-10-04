using Microsoft.AspNetCore.Components.Forms;

namespace Clipify.Forms.Pages;

public partial class ExtractAudio {
    public string FilePath { get; set; }

    protected override Task OnInitializedAsync() {
        FileDialogService.OnFileSelected += UpdateSelectedFile;
        return base.OnInitializedAsync();
    }

    private async Task OpenFileDialog() {
        await FileDialogService.OpenFileDialogAsync();
    }

    private void UpdateSelectedFile(string fileName) {
        FilePath = fileName;
        InvokeAsync(StateHasChanged);
    }

    private async Task LoadFiles(InputFileChangeEventArgs e) {
        
    }
}
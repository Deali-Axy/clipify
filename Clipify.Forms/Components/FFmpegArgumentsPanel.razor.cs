using Microsoft.AspNetCore.Components;

namespace Clipify.Forms.Components;

public partial class FFmpegArgumentsPanel : ComponentBase {
    [Parameter] public string? FFmpegCommand { get; set; }

    [Parameter] public EventCallback OnRegenerateArguments { get; set; }
}
using Microsoft.AspNetCore.Components;

namespace Clipify.Maui.Components.Shared;

public partial class FFmpegArgumentsPanel
{
    [Parameter]
    public string? FFmpegCommand { get; set; }
    
    [Parameter]
    public EventCallback OnRegenerateArguments { get; set; }
} 
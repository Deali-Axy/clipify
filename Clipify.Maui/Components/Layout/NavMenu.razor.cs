using AntDesign;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace Clipify.Maui.Components.Layout;

public partial class NavMenu : ComponentBase {
    [Inject] private INotificationService NotiService { get; set; }

    [Inject] private IMessageService MsgService { get; set; }

    [Parameter] public bool IsCollapsed { get; set; } = false;

    [Parameter] public EventCallback<bool> IsCollapsedChanged { get; set; }

    // 添加计算属性，用于生成正确的CSS类
    private string GetNavItemClass(bool isActive = false) =>
        $"flex items-center p-2 {(isActive ? "text-blue-700" : "text-gray-900")} rounded-lg dark:text-white hover:bg-gray-100 dark:hover:bg-gray-700 group {(IsCollapsed ? "justify-center" : "")}";

    private string GetDisabledNavItemClass() =>
        $"flex items-center p-2 text-gray-500 rounded-lg dark:text-gray-400 group cursor-default {(IsCollapsed ? "justify-center" : "")}";

    private string GetActiveClass() =>
        IsCollapsed ? "bg-blue-50" : "bg-blue-50 text-blue-700";

    private async Task ToggleNavbar() {
        IsCollapsed = !IsCollapsed;
        await IsCollapsedChanged.InvokeAsync(IsCollapsed);
    }

    private async Task ShowFeatureInDev(string featureName) {
        await NotiService.Open(new NotificationConfig {
            Message = $"{featureName} - 开发中",
            Description = $"{featureName}功能正在开发中，敬请期待！",
            Icon = CustomIcon
        });
        return;

        void CustomIcon(RenderTreeBuilder builder) {
            builder.OpenElement(0, "i");
            builder.AddAttribute(1, "class", "fa-solid fa-code-branch text-2xl text-blue-500");
            builder.CloseElement();
        }
    }

    private async Task OpenFileDialog() {
        await MsgService.SuccessAsync("文件选择器：即将打开文件选择器，请选择要编辑的视频文件。");
        // 这里实际项目中应该调用文件选择器
    }
}
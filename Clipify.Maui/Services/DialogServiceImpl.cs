using Clipify.Core.Interfaces;

namespace Clipify.Maui.Services;

/// <summary>
/// MAUI对话框服务实现
/// </summary>
public class DialogServiceImpl : IDialogService
{
    /// <summary>
    /// 文件选择事件
    /// </summary>
    public event Func<string, Task>? OnFileSelected;
    
    /// <summary>
    /// 目录选择事件
    /// </summary>
    public event Func<string, Task>? OnDirSelected;

    /// <summary>
    /// 打开文件选择对话框
    /// </summary>
    /// <returns>选择的文件路径</returns>
    public async Task<string> OpenFileAsync()
    {
        try
        {
            var options = new PickOptions
            {
                PickerTitle = "请选择视频文件",
                FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                    { DevicePlatform.iOS, new[] { "public.movie" } },
                    { DevicePlatform.Android, new[] { "video/*" } },
                    { DevicePlatform.WinUI, new[] { ".mp4", ".avi", ".mkv", ".mov", ".flv", ".wmv" } },
                    { DevicePlatform.macOS, new[] { "mp4", "avi", "mkv", "mov", "flv", "wmv" } }
                })
            };

            var result = await FilePicker.PickAsync(options);
            if (result != null)
            {
                string filePath = result.FullPath;
                OnFileSelected?.Invoke(filePath);
                return filePath;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"文件选择错误: {ex.Message}");
        }

        return string.Empty;
    }

    /// <summary>
    /// 打开目录选择对话框
    /// </summary>
    /// <returns>选择的目录路径</returns>
    public async Task<string> OpenDirAsync()
    {
        try
        {
            // MAUI中没有直接的文件夹选择器，我们使用文件选择器，然后返回文件所在的目录
            var result = await FilePicker.PickAsync(new PickOptions
            {
                PickerTitle = "请选择任意文件，我们将使用其所在目录"
            });

            if (result != null)
            {
                string dirPath = Path.GetDirectoryName(result.FullPath);
                if (!string.IsNullOrEmpty(dirPath))
                {
                    OnDirSelected?.Invoke(dirPath);
                    return dirPath;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"目录选择错误: {ex.Message}");
        }

        return string.Empty;
    }

    /// <summary>
    /// 通知文件已选择
    /// </summary>
    /// <param name="path">文件路径</param>
    public void NotifyFileSelected(string path)
    {
        OnFileSelected?.Invoke(path);
    }

    /// <summary>
    /// 通知目录已选择
    /// </summary>
    /// <param name="path">目录路径</param>
    public void NotifyDirSelected(string path)
    {
        OnDirSelected?.Invoke(path);
    }
} 
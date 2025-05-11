namespace Clipify.Core.Interfaces;

/// <summary>
/// 对话框服务接口
/// </summary>
public interface IDialogService
{
    /// <summary>
    /// 文件选择事件
    /// </summary>
    event Func<string, Task>? OnFileSelected;
    
    /// <summary>
    /// 目录选择事件
    /// </summary>
    event Func<string, Task>? OnDirSelected;
    
    /// <summary>
    /// 打开文件选择对话框
    /// </summary>
    /// <returns>选择的文件路径</returns>
    Task<string> OpenFileAsync();
    
    /// <summary>
    /// 打开目录选择对话框
    /// </summary>
    /// <returns>选择的目录路径</returns>
    Task<string> OpenDirAsync();
    
    /// <summary>
    /// 通知文件已选择
    /// </summary>
    /// <param name="path">文件路径</param>
    void NotifyFileSelected(string path);
    
    /// <summary>
    /// 通知目录已选择
    /// </summary>
    /// <param name="path">目录路径</param>
    void NotifyDirSelected(string path);
} 
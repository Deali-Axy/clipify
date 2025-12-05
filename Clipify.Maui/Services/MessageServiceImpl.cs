using Clipify.Core.Interfaces;

namespace Clipify.Maui.Services;

/// <summary>
/// MAUI消息服务实现
/// </summary>
public class MessageServiceImpl : IMessageService
{
    /// <summary>
    /// 显示成功消息
    /// </summary>
    /// <param name="message">消息内容</param>
    /// <returns>异步任务</returns>
    public async Task Success(string message)
    {
        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            await Application.Current.MainPage.DisplayAlert("成功", message, "确定");
        });
    }

    /// <summary>
    /// 显示错误消息
    /// </summary>
    /// <param name="message">消息内容</param>
    /// <returns>异步任务</returns>
    public async Task Error(string message)
    {
        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            await Application.Current.MainPage.DisplayAlert("错误", message, "确定");
        });
    }

    /// <summary>
    /// 显示警告消息
    /// </summary>
    /// <param name="message">消息内容</param>
    /// <returns>异步任务</returns>
    public async Task Warning(string message)
    {
        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            await Application.Current.MainPage.DisplayAlert("警告", message, "确定");
        });
    }

    /// <summary>
    /// 显示信息消息
    /// </summary>
    /// <param name="message">消息内容</param>
    /// <returns>异步任务</returns>
    public async Task Info(string message)
    {
        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            await Application.Current.MainPage.DisplayAlert("信息", message, "确定");
        });
    }
} 
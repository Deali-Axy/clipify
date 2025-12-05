namespace Clipify.Core.Interfaces;

/// <summary>
/// 消息服务接口
/// </summary>
public interface IMessageService
{
    /// <summary>
    /// 显示成功消息
    /// </summary>
    /// <param name="message">消息内容</param>
    /// <returns>异步任务</returns>
    Task Success(string message);
    
    /// <summary>
    /// 显示错误消息
    /// </summary>
    /// <param name="message">消息内容</param>
    /// <returns>异步任务</returns>
    Task Error(string message);
    
    /// <summary>
    /// 显示警告消息
    /// </summary>
    /// <param name="message">消息内容</param>
    /// <returns>异步任务</returns>
    Task Warning(string message);
    
    /// <summary>
    /// 显示信息消息
    /// </summary>
    /// <param name="message">消息内容</param>
    /// <returns>异步任务</returns>
    Task Info(string message);
} 
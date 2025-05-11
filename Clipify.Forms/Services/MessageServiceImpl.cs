using AntDesign;
using CoreMessageService = Clipify.Core.Interfaces.IMessageService;

namespace Clipify.Forms.Services;

/// <summary>
/// 消息服务实现
/// </summary>
public class MessageServiceImpl : CoreMessageService
{
    private readonly MessageService _messageService;

    /// <summary>
    /// 初始化消息服务
    /// </summary>
    /// <param name="messageService">AntDesign消息服务</param>
    public MessageServiceImpl(MessageService messageService)
    {
        _messageService = messageService;
    }

    /// <summary>
    /// 显示成功消息
    /// </summary>
    /// <param name="message">消息内容</param>
    /// <returns>异步任务</returns>
    public async Task Success(string message)
    {
        await _messageService.Success(message);
    }

    /// <summary>
    /// 显示错误消息
    /// </summary>
    /// <param name="message">消息内容</param>
    /// <returns>异步任务</returns>
    public async Task Error(string message)
    {
        await _messageService.Error(message);
    }

    /// <summary>
    /// 显示警告消息
    /// </summary>
    /// <param name="message">消息内容</param>
    /// <returns>异步任务</returns>
    public async Task Warning(string message)
    {
        await _messageService.Warning(message);
    }

    /// <summary>
    /// 显示信息消息
    /// </summary>
    /// <param name="message">消息内容</param>
    /// <returns>异步任务</returns>
    public async Task Info(string message)
    {
        await _messageService.Info(message);
    }
} 
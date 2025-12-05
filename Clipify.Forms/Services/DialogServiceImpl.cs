using Clipify.Core.Interfaces;
using Clipify.Forms.EventBus.Request;
using MediatR;

namespace Clipify.Forms.Services;

/// <summary>
/// 对话框服务实现
/// </summary>
public class DialogServiceImpl : IDialogService
{
    private readonly IMediator _mediator;

    /// <summary>
    /// 文件选择事件
    /// </summary>
    public event Func<string, Task>? OnFileSelected;
    
    /// <summary>
    /// 目录选择事件
    /// </summary>
    public event Func<string, Task>? OnDirSelected;

    /// <summary>
    /// 初始化对话框服务
    /// </summary>
    /// <param name="mediator">中介者</param>
    public DialogServiceImpl(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// 打开文件选择对话框
    /// </summary>
    /// <returns>选择的文件路径</returns>
    public async Task<string> OpenFileAsync()
    {
        return await _mediator.Send(new OpenFileRequest());
    }

    /// <summary>
    /// 打开目录选择对话框
    /// </summary>
    /// <returns>选择的目录路径</returns>
    public async Task<string> OpenDirAsync()
    {
        return await _mediator.Send(new OpenDirRequest());
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
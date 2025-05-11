using MediatR;

namespace Clipify.Forms.Services;

/// <summary>
/// 对话框服务（兼容性类）
/// </summary>
public class DialogService : DialogServiceImpl
{
    /// <summary>
    /// 初始化对话框服务
    /// </summary>
    /// <param name="mediator">中介者</param>
    public DialogService(IMediator mediator) : base(mediator)
    {
    }
} 
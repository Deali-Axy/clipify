using Clipify.Forms.EventBus.Request;
using MediatR;

namespace Clipify.Forms.Services;

public class DialogService {
    private readonly IMediator _mediator;

    public event Func<string, Task>? OnFileSelected;
    public event Func<string, Task>? OnDirSelected;

    public DialogService(IMediator mediator) {
        _mediator = mediator;
    }

    /// <summary>
    /// 发送请求，通知 WinForms 打开文件对话框
    /// </summary>
    public async Task<string> OpenFileAsync() {
        return await _mediator.Send(new OpenFileRequest());
    }

    public async Task<string> OpenDirAsync() {
        return await _mediator.Send(new OpenDirRequest());
    }

    public void NotifyFileSelected(string path) {
        OnFileSelected?.Invoke(path);
    }

    public void NotifyDirSelected(string path) {
        OnDirSelected?.Invoke(path);
    }
}
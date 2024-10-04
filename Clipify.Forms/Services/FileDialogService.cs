using Clipify.Forms.EventBus.Request;
using MediatR;

namespace Clipify.Forms.Services;

public class FileDialogService {
    private readonly IMediator _mediator;

    public event Action<string> OnFileSelected;

    public FileDialogService(IMediator mediator) {
        _mediator = mediator;
    }

    /// <summary>
    /// 发送请求，通知 WinForms 打开文件对话框
    /// </summary>
    public async Task<string> OpenFileDialogAsync() {
        return await _mediator.Send(new OpenFileRequest());
    }

    public void NotifyFileSelected(string fileName) {
        OnFileSelected?.Invoke(fileName);
    }
}
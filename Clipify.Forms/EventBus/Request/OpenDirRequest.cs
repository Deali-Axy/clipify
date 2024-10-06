using Clipify.Forms.EventBus.Notification;
using MediatR;

namespace Clipify.Forms.EventBus.Request;

public class OpenDirRequest : IRequest<string> { }

public class OpenDirHandler : IRequestHandler<OpenDirRequest, string> {
    private readonly IMediator _mediator;
    private readonly FormMain _formMain;

    public OpenDirHandler(IMediator mediator, FormMain formMain) {
        _mediator = mediator;
        _formMain = formMain;
    }

    public Task<string> Handle(OpenDirRequest request, CancellationToken cancellationToken) {
        // 如果之前没选过，就设置初始路径为视频文件夹
        if (string.IsNullOrWhiteSpace(_formMain.folderBrowserDialog.SelectedPath)) {
            _formMain.folderBrowserDialog.RootFolder = Environment.SpecialFolder.MyVideos;
        }

        var result = _formMain.folderBrowserDialog.ShowDialog();

        if (result == DialogResult.OK) {
            var path = _formMain.folderBrowserDialog.SelectedPath;
            _mediator.Publish(new DirSelectedNoti {
                SelectedPath = path
            }, cancellationToken);
            return Task.FromResult(path);
        }

        return Task.FromResult("");
    }
}
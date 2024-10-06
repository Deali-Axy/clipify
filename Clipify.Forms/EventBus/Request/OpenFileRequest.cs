using Clipify.Forms.EventBus.Notification;
using MediatR;

namespace Clipify.Forms.EventBus.Request;

public class OpenFileRequest : IRequest<string> { }

public class OpenFileHandler : IRequestHandler<OpenFileRequest, string> {
    private readonly IMediator _mediator;
    private readonly FormMain _formMain;

    public OpenFileHandler(FormMain formMain, IMediator mediator) {
        _formMain = formMain;
        _mediator = mediator;
    }

    public Task<string> Handle(OpenFileRequest request, CancellationToken cancellationToken) {
        var result = _formMain.openFileDialog.ShowDialog();
        
        if (result == DialogResult.OK) {
            var path = _formMain.openFileDialog.FileName;
            _mediator.Publish(new FileSelectedNoti {
                SelectedPath = path
            }, cancellationToken);
            return Task.FromResult(path);
        }

        return Task.FromResult("");
    }
}
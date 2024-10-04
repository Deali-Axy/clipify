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
            _mediator.Publish(new FileSelectedNoti {
                FilePath = _formMain.openFileDialog.FileName
            }, cancellationToken);
            return Task.FromResult(_formMain.openFileDialog.FileName);
        }

        return Task.FromResult("");
    }
}
using MediatR;

namespace Clipify.Forms.EventBus.Request;

public class OpenFileRequest : IRequest<string> {

}

public class OpenFileHandler : IRequestHandler<OpenFileRequest, string> {
    private readonly FormMain _formMain;

    public OpenFileHandler(FormMain formMain) {
        _formMain = formMain;
    }

    public Task<string> Handle(OpenFileRequest request, CancellationToken cancellationToken) {
        Console.WriteLine($"打开文件！！！");

        var result = _formMain.openFileDialog.ShowDialog();
        if (result == DialogResult.OK) {
            Console.WriteLine($"打开文件: {_formMain.openFileDialog.FileName}");
            return Task.FromResult(_formMain.openFileDialog.FileName);
        }

        return Task.FromResult("");
    }
}
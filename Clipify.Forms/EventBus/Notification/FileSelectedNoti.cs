using Clipify.Forms.Services;
using MediatR;

namespace Clipify.Forms.EventBus.Notification;

public class FileSelectedNoti : INotification {
    public string SelectedPath { get; set; }
}

public class FileSelectedHandler : INotificationHandler<FileSelectedNoti> {
    private readonly DialogService _dialogService;

    public FileSelectedHandler(DialogService dialogService) {
        _dialogService = dialogService;
    }

    public Task Handle(FileSelectedNoti notification, CancellationToken cancellationToken) {
        _dialogService.NotifyFileSelected(notification.SelectedPath);
        return Task.CompletedTask;
    }
}
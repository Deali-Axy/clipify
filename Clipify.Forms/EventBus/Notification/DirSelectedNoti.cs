using Clipify.Forms.Services;
using MediatR;

namespace Clipify.Forms.EventBus.Notification;

public class DirSelectedNoti : INotification {
    public string SelectedPath { get; set; }
}

public class DirSelectedHandler : INotificationHandler<DirSelectedNoti> {
    private readonly DialogService _dialogService;

    public DirSelectedHandler(DialogService dialogService) {
        _dialogService = dialogService;
    }

    public Task Handle(DirSelectedNoti notification, CancellationToken cancellationToken) {
        _dialogService.NotifyDirSelected(notification.SelectedPath);
        return Task.CompletedTask;
    }
}
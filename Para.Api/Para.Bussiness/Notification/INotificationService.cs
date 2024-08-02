namespace Para.Bussiness.Notification;

public interface INotificationService
{
    public void SendEmail(string subject, string email, string content);
    public void SendEmailDirect(string subject, string email, string content);
    public void SendQueuedEmails();
}
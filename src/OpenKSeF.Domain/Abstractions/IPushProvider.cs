using OpenKSeF.Domain.Models;

namespace OpenKSeF.Domain.Abstractions;

public interface IPushProvider
{
    Task<bool> SendAsync(string deviceToken, PushNotification notification);
}

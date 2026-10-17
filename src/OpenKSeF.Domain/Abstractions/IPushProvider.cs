using OpenKSeF.Domain.Enums;
using OpenKSeF.Domain.Models;

namespace OpenKSeF.Domain.Abstractions;

public interface IPushProvider
{
    Task<PushResult> SendAsync(string deviceToken, Platform platform, PushNotification notification);
}

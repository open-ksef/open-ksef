using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using KSeF.Client.Core.Exceptions;
using Microsoft.EntityFrameworkCore;
using OpenKSeF.Domain.Abstractions;
using OpenKSeF.Domain.Entities;
using OpenKSeF.Sync.Issuing;

namespace OpenKSeF.Sync;

/// <summary>Maps a failed sync to a stored category and its REQ-0013 Polish reason. Never keeps the KSeF response.</summary>
public static partial class SyncFailure
{
    public const string InvalidToken = "InvalidToken";
    public const string InvalidCertificate = "InvalidCertificate";
    public const string LoginRefused = "LoginRefused";
    public const string RateLimited = "RateLimited";
    public const string KsefUnavailable = "KsefUnavailable";
    public const string StoreFailed = "StoreFailed";
    public const string Other = "Other";

    public static string Classify(Exception ex, CredentialType credentialType) => ex switch
    {
        StoredCertificateUnreadableException => InvalidCertificate,
        DbUpdateException => StoreFailed,
        KsefApiException { StatusCode: HttpStatusCode.TooManyRequests } => RateLimited,
        KsefApiException { StatusCode: HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden } => LoginRefused,
        KsefApiException { StatusCode: >= HttpStatusCode.InternalServerError } or HttpRequestException or SocketException or TaskCanceledException => KsefUnavailable,
        TimeoutException timeout => AuthStatus(timeout.Message) switch
        {
            450 when credentialType == CredentialType.Token && IssuedInvoiceSendProcessor.TokenFaults.Any(timeout.Message.Contains) => InvalidToken,
            460 when credentialType == CredentialType.Certificate => InvalidCertificate,
            >= 400 and < 500 => LoginRefused,
            _ => KsefUnavailable,
        },
        _ => Other,
    };

    public static string? Message(string? category) => category switch
    {
        InvalidToken => IssuedInvoiceSendProcessor.InvalidTokenMessage,
        InvalidCertificate => IssuedInvoiceSendProcessor.InvalidCertificateMessage,
        LoginRefused => "KSeF nie pozwolił zalogować się na tę firmę. Sprawdź, czy token lub certyfikat jest wystawiony dla tego NIP-u i ma uprawnienie do przeglądania faktur.",
        RateLimited => "KSeF chwilowo ograniczył liczbę zapytań. Spróbujemy ponownie przy następnym pobraniu, nic nie musisz robić.",
        KsefUnavailable => "Nie udało się połączyć z KSeF. Spróbujemy ponownie automatycznie. Jeśli problem trwa dłużej niż dobę, napisz do administratora OpenKSeF.",
        StoreFailed => "Nie udało się zapisać pobranych faktur. Spróbujemy ponownie przy następnym pobraniu. Jeśli błąd wraca, napisz do administratora OpenKSeF.",
        Other => "Pobieranie z KSeF nie powiodło się. Spróbujemy ponownie automatycznie. Jeśli błąd wraca, napisz do administratora OpenKSeF.",
        _ => null,
    };

    public static bool IsCredentialCause(string? category) => category is InvalidToken or InvalidCertificate or LoginRefused;

    // KSeF.Client's AuthCoordinator hides a refused login in a TimeoutException; its message carries the last auth status code.
    private static int? AuthStatus(string message) =>
        AuthStatusPattern().Match(message) is { Success: true } m ? int.Parse(m.Groups[1].Value) : null;

    [GeneratedRegex(@"Ostatni status: (\d{3})")]
    private static partial Regex AuthStatusPattern();
}

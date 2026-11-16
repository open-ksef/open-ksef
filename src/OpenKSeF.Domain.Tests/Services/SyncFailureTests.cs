using OpenKSeF.Sync;

namespace OpenKSeF.Domain.Tests.Services;

public class SyncFailureTests
{
    [Theory]
    [InlineData(SyncFailure.InvalidToken, "KSeF odrzucił token (nieprawidłowy lub unieważniony). Wygeneruj nowy token i podmień go w ustawieniach firmy.", true)]
    [InlineData(SyncFailure.InvalidCertificate, "KSeF odrzucił certyfikat (nieprawidłowy, wygasły lub unieważniony). Wgraj nowy certyfikat KSeF w ustawieniach firmy.", true)]
    [InlineData(SyncFailure.LoginRefused, "KSeF nie pozwolił zalogować się na tę firmę. Sprawdź, czy token lub certyfikat jest wystawiony dla tego NIP-u i ma uprawnienie do przeglądania faktur.", true)]
    [InlineData(SyncFailure.RateLimited, "KSeF chwilowo ograniczył liczbę zapytań. Spróbujemy ponownie przy następnym pobraniu, nic nie musisz robić.", false)]
    [InlineData(SyncFailure.KsefUnavailable, "Nie udało się połączyć z KSeF. Spróbujemy ponownie automatycznie. Jeśli problem trwa dłużej niż dobę, napisz do administratora OpenKSeF.", false)]
    [InlineData(SyncFailure.StoreFailed, "Nie udało się zapisać pobranych faktur. Spróbujemy ponownie przy następnym pobraniu. Jeśli błąd wraca, napisz do administratora OpenKSeF.", false)]
    [InlineData(SyncFailure.Other, "Pobieranie z KSeF nie powiodło się. Spróbujemy ponownie automatycznie. Jeśli błąd wraca, napisz do administratora OpenKSeF.", false)]
    public void Category_HasReq0013Text_AndCredentialLinkRule(string category, string text, bool credentialCause)
    {
        Assert.Equal(text, SyncFailure.Message(category));
        Assert.Equal(credentialCause, SyncFailure.IsCredentialCause(category));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Unknown")]
    public void UnknownOrMissingCategory_HasNoText_NoLink(string? category)
    {
        Assert.Null(SyncFailure.Message(category));
        Assert.False(SyncFailure.IsCredentialCause(category));
    }
}

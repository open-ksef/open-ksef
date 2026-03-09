using System.Net.Http.Headers;

namespace OpenKSeF.Api.IntegrationTests.Infrastructure;

public static class TokenHelper
{
    public static void SetBearerToken(this HttpClient client, string token)
    {
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
    }
}

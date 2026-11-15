using System.Net;
using KSeF.Client.Core.Exceptions;
using KSeF.Client.Core.Interfaces;
using KSeF.Client.Core.Interfaces.Services;
using KSeF.Client.DI;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using OpenKSeF.Domain.Abstractions;
using OpenKSeF.Domain.Services;
using OpenKSeF.Sync;

using CirfmfKSeFClient = KSeF.Client.Core.Interfaces.Clients.IKSeFClient;

namespace OpenKSeF.Api.Tests.Services;

public class KSeFGatewayDownloadTests
{
    private static readonly KSeFSession Session = new("session-token", DateTime.UtcNow.AddHours(1));

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(respond(request));
        }
    }

    private static KSeFGateway Gateway(StubHandler handler)
    {
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient(handler, disposeHandler: false));
        return new KSeFGateway(
            Substitute.For<CirfmfKSeFClient>(), Substitute.For<IAuthCoordinator>(), Substitute.For<ICryptographyService>(), Substitute.For<IEncryptionService>(),
            factory, new KSeFClientOptions { BaseUrl = KsefEnvironmentsUris.TEST }, NullLogger<KSeFGateway>.Instance);
    }

    [Fact]
    public async Task Download_ReturnsBodyByteIdentical_IncludingBom_FromInvoiceRouteWithBearer()
    {
        byte[] body = [0xEF, 0xBB, 0xBF, .. "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Faktura>Zażółć</Faktura>"u8];
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) });

        var result = await Gateway(handler).DownloadInvoiceAsync(Session, "1111111111-20261115-ABC/1");

        Assert.Equal(body, result);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://api-test.ksef.mf.gov.pl/v2/invoices/ksef/1111111111-20261115-ABC%2F1", request.RequestUri!.AbsoluteUri);
        Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
        Assert.Equal("session-token", request.Headers.Authorization.Parameter);
        Assert.NotNull(new KSeFInvoiceXmlParser(NullLogger<KSeFInvoiceXmlParser>.Instance).ExtractInvoiceDetails(result));
    }

    [Fact]
    public async Task Download_429_ThrowsRateLimitWithRetryAfter()
    {
        var handler = new StubHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.TryAddWithoutValidation("Retry-After", "120");
            return response;
        });

        var ex = await Assert.ThrowsAsync<KsefRateLimitException>(() => Gateway(handler).DownloadInvoiceAsync(Session, "KSEF-1"));

        Assert.Equal(HttpStatusCode.TooManyRequests, ex.StatusCode);
        Assert.Equal(TimeSpan.FromSeconds(120), ex.RecommendedDelay);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task Download_OtherError_ThrowsApiExceptionWithStatus(HttpStatusCode status)
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(status));

        var ex = await Assert.ThrowsAsync<KsefApiException>(() => Gateway(handler).DownloadInvoiceAsync(Session, "KSEF-1"));

        Assert.Equal(status, ex.StatusCode);
    }
}

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using OpenKSeF.Api.Models;
using OpenKSeF.Api.Services;
using OpenKSeF.Domain.Services;

namespace OpenKSeF.Api.Controllers;

[ApiController]
[Route("api/nip-lookup")]
[Authorize]
[EnableRateLimiting(RateLimitPolicy)]
public class NipLookupController(WhiteListClient whiteList) : ControllerBase
{
    public const string RateLimitPolicy = "nip-lookup";

    [HttpGet("{nip}")]
    [ProducesResponseType(typeof(NipLookupResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationEnvelope), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ValidationEnvelope), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ValidationEnvelope), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Get(string nip, CancellationToken ct)
    {
        var normalized = nip.Replace("-", "").Trim();
        if (!NipValidator.HasValidChecksum(normalized))
            return BadRequest(Envelope("NIP-INVALID", "Niepoprawny NIP"));

        try
        {
            var result = await whiteList.LookupAsync(normalized, ct);
            return result is null
                ? NotFound(Envelope("NIP-NOT-FOUND", "Brak podmiotu w wykazie podatników VAT, wpisz dane ręcznie"))
                : Ok(result);
        }
        catch (WhiteListInvalidNipException)
        {
            return BadRequest(Envelope("NIP-INVALID", "Niepoprawny NIP"));
        }
        catch (WhiteListUnavailableException)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                Envelope("NIP-LOOKUP-UNAVAILABLE", "Wyszukiwarka MF niedostępna, wpisz dane ręcznie"));
        }
    }

    private static ValidationEnvelope Envelope(string code, string message) => new([new ValidationIssue(code, "nip", message)], []);
}

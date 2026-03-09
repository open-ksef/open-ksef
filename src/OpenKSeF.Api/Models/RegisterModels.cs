using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace OpenKSeF.Api.Models;

public record RegisterRequest(
    [property: JsonPropertyName("email")]
    [Required, EmailAddress]
    string Email,

    [property: JsonPropertyName("password")]
    [Required, MinLength(8)]
    string Password,

    [property: JsonPropertyName("firstName")]
    string? FirstName,

    [property: JsonPropertyName("lastName")]
    string? LastName);

public record RegisterResponse(
    [property: JsonPropertyName("message")] string Message);

using System.Text.Json.Serialization;

namespace OpenKSeF.Domain.Entities;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CredentialType
{
    Token = 0,
    Certificate = 1
}

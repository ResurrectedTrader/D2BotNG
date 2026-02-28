using System.Text.Json.Serialization;
using D2BotNG.Core.Protos;
using JetBrains.Annotations;

namespace D2BotNG.Legacy.Models;

/// <summary>
/// Represents the server.json format used by the legacy D2Bot framework.
/// Contains web API configuration including profiles and user credentials.
/// </summary>
[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
public class LegacyWebConfig
{
    [JsonPropertyName("profiles")]
    public string[] Profiles { get; init; } = [];

    [JsonPropertyName("users")]
    public LegacyWebUser[] Users { get; init; } = [];

    [JsonPropertyName("path")]
    public string Path { get; init; } = "";

    [JsonPropertyName("ip")]
    public string Ip { get; init; } = "";

    [JsonPropertyName("certificate")]
    public string Certificate { get; init; } = "";

    [JsonPropertyName("port")]
    public int Port { get; init; }

    [JsonPropertyName("secure")]
    public int Secure { get; init; }
}

/// <summary>
/// Represents a web API user from the legacy server.json format.
/// </summary>
[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
public class LegacyWebUser
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("apikey")]
    public string ApiKey { get; init; } = "";

    /// <summary>
    /// Permission flag: 0 = public, 1 = admin.
    /// </summary>
    [JsonPropertyName("flag")]
    public int Flag { get; init; }

    public LegacyApiUser ToModern() => new()
    {
        Name = Name,
        ApiKey = ApiKey,
        Permissions = Flag == 1
            ? LegacyApiPermissions.Admin
            : LegacyApiPermissions.Public,
    };
}

using System.Text.Json;
using AssetHub.Application.Dtos;

namespace AssetHub.Application.Services;

/// <summary>
/// Reads and writes <see cref="S3SourceConfigDto"/> to/from the untyped
/// <c>Migration.SourceConfig</c> JSONB dictionary, encrypting the secret key
/// on the write path and decrypting it on the read path.
/// </summary>
/// <remarks>
/// The dictionary is persisted as <c>Dictionary&lt;string, object&gt;</c>;
/// when EF Core round-trips it through <see cref="JsonSerializer"/> the values
/// come back as <see cref="JsonElement"/>, so the read path has to handle both
/// raw strings (freshly populated in memory) and JsonElements (loaded from DB).
/// </remarks>
public static class MigrationS3ConfigCodec
{
    public static class Keys
    {
        public const string Endpoint = "endpoint";
        public const string Bucket = "bucket";
        public const string Prefix = "prefix";
        public const string AccessKey = "access_key";
        public const string SecretKeyEncrypted = "secret_key_encrypted";
        public const string Region = "region";
    }

    /// <summary>
    /// Encrypts the secret key and writes every S3 field into a new dictionary
    /// suitable for assignment to <c>Migration.SourceConfig</c>.
    /// </summary>
    public static Dictionary<string, object> Write(
        S3SourceConfigDto dto, IMigrationSecretProtector protector)
    {
        ArgumentNullException.ThrowIfNull(dto);
        ArgumentNullException.ThrowIfNull(protector);

        var result = new Dictionary<string, object>
        {
            [Keys.Endpoint] = dto.Endpoint,
            [Keys.Bucket] = dto.Bucket,
            [Keys.AccessKey] = dto.AccessKey,
            [Keys.SecretKeyEncrypted] = protector.Protect(dto.SecretKey)
        };
        if (!string.IsNullOrWhiteSpace(dto.Prefix))
            result[Keys.Prefix] = dto.Prefix;
        if (!string.IsNullOrWhiteSpace(dto.Region))
            result[Keys.Region] = dto.Region;
        return result;
    }

    /// <summary>
    /// Reads an <see cref="S3SourceConfigDto"/> from the persisted dictionary,
    /// decrypting the secret key. Throws <see cref="InvalidOperationException"/>
    /// when required fields are missing or malformed.
    /// </summary>
    public static S3SourceConfigDto Read(
        IReadOnlyDictionary<string, object> sourceConfig, IMigrationSecretProtector protector)
    {
        ArgumentNullException.ThrowIfNull(sourceConfig);
        ArgumentNullException.ThrowIfNull(protector);

        var endpoint = RequireString(sourceConfig, Keys.Endpoint);
        var bucket = RequireString(sourceConfig, Keys.Bucket);
        var accessKey = RequireString(sourceConfig, Keys.AccessKey);
        var secretEncrypted = RequireString(sourceConfig, Keys.SecretKeyEncrypted);

        return new S3SourceConfigDto
        {
            Endpoint = endpoint,
            Bucket = bucket,
            AccessKey = accessKey,
            SecretKey = protector.Unprotect(secretEncrypted),
            Prefix = TryString(sourceConfig, Keys.Prefix),
            Region = TryString(sourceConfig, Keys.Region)
        };
    }

    private static string RequireString(IReadOnlyDictionary<string, object> dict, string key)
        => TryString(dict, key)
            ?? throw new InvalidOperationException($"S3 source config is missing '{key}'.");

    private static string? TryString(IReadOnlyDictionary<string, object> dict, string key)
    {
        if (!dict.TryGetValue(key, out var raw) || raw is null) return null;
        return raw switch
        {
            string s => string.IsNullOrEmpty(s) ? null : s,
            JsonElement el when el.ValueKind == JsonValueKind.String => el.GetString(),
            _ => raw.ToString()
        };
    }
}

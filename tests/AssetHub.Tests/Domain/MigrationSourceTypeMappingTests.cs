using AssetHub.Domain.Entities;

namespace AssetHub.Tests.Domain;

/// <summary>
/// Round-trip coverage for <see cref="MigrationSourceType"/> DB-string extensions.
/// Migrations are stored as strings so every enum value must have a stable mapping.
/// </summary>
public class MigrationSourceTypeMappingTests
{
    [Theory]
    [InlineData(MigrationSourceType.CsvUpload, "csv_upload")]
    [InlineData(MigrationSourceType.S3, "s3")]
    public void ToDbString_MapsEnumToExpectedString(MigrationSourceType value, string expected)
    {
        Assert.Equal(expected, value.ToDbString());
    }

    [Theory]
    [InlineData("csv_upload", MigrationSourceType.CsvUpload)]
    [InlineData("s3", MigrationSourceType.S3)]
    public void ToMigrationSourceType_ParsesKnownStrings(string raw, MigrationSourceType expected)
    {
        Assert.Equal(expected, raw.ToMigrationSourceType());
    }

    [Fact]
    public void ToMigrationSourceType_UnknownValue_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => "ftp".ToMigrationSourceType());
    }

    [Fact]
    public void RoundTrip_EveryEnumValueSurvivesToStringAndBack()
    {
        foreach (MigrationSourceType value in Enum.GetValues<MigrationSourceType>())
        {
            var roundTripped = value.ToDbString().ToMigrationSourceType();
            Assert.Equal(value, roundTripped);
        }
    }
}

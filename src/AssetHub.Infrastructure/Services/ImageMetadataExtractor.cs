using AssetHub.Application.Configuration;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.Iptc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AssetHub.Infrastructure.Services;

/// <summary>
/// Extracts EXIF/IPTC/XMP metadata from image files.
/// </summary>
public sealed class ImageMetadataExtractor(
    IOptions<ImageProcessingSettings> imageProcessingSettings,
    ILogger<ImageMetadataExtractor> logger)
{
    private readonly ImageProcessingSettings _imageSettings = imageProcessingSettings.Value;

    /// <summary>
    /// Extract EXIF/IPTC/XMP metadata from an image file.
    /// </summary>
    public Dictionary<string, object> ExtractImageMetadata(string imagePath)
    {
        var result = new Dictionary<string, object>();

        try
        {
            var directories = ImageMetadataReader.ReadMetadata(imagePath);

            logger.LogDebug("Found {DirectoryCount} metadata directories in image: {DirectoryTypes}",
                directories.Count, string.Join(", ", directories.Select(d => d.Name).Distinct()));

            ExtractExifData(directories, result);
            logger.LogDebug("After EXIF extraction: {FieldCount} fields", result.Count);

            ExtractIptcData(directories, result);
            logger.LogDebug("After IPTC extraction: {FieldCount} fields", result.Count);

            if (_imageSettings.IncludeGpsData)
            {
                ExtractGpsData(directories, result);
                logger.LogDebug("After GPS extraction: {FieldCount} fields", result.Count);
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Metadata extraction is non-critical — file may not have EXIF data
            logger.LogWarning(ex, "Metadata extraction failed for {ImagePath}: {ErrorType} - {ErrorMessage}",
                imagePath, ex.GetType().Name, ex.Message);
        }

        return result;
    }

    private static void ExtractExifData(IReadOnlyList<MetadataExtractor.Directory> directories, Dictionary<string, object> result)
    {
        var exifIfd0 = directories.OfType<ExifIfd0Directory>().FirstOrDefault();
        if (exifIfd0 != null)
            ExtractExifIfd0Data(exifIfd0, result);

        var exifSubIfd = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();
        if (exifSubIfd != null)
            ExtractExifSubIfdData(exifSubIfd, result);
    }

    private static void ExtractExifIfd0Data(ExifIfd0Directory exifIfd0, Dictionary<string, object> result)
    {
        var artist = exifIfd0.GetString(ExifDirectoryBase.TagArtist);
        if (!string.IsNullOrWhiteSpace(artist))
            result["artist"] = artist;

        var copyright = exifIfd0.GetString(ExifDirectoryBase.TagCopyright);
        if (!string.IsNullOrWhiteSpace(copyright))
            result["copyright"] = copyright;

        var make = exifIfd0.GetString(ExifDirectoryBase.TagMake);
        if (!string.IsNullOrWhiteSpace(make))
            result["cameraMake"] = make;

        var model = exifIfd0.GetString(ExifDirectoryBase.TagModel);
        if (!string.IsNullOrWhiteSpace(model))
            result["cameraModel"] = model;

        var software = exifIfd0.GetString(ExifDirectoryBase.TagSoftware);
        if (!string.IsNullOrWhiteSpace(software))
            result["software"] = software;
    }

    private static void ExtractExifSubIfdData(ExifSubIfdDirectory exifSubIfd, Dictionary<string, object> result)
    {
        if (exifSubIfd.TryGetDateTime(ExifDirectoryBase.TagDateTimeOriginal, out var dateTimeOriginal))
            result["dateTaken"] = dateTimeOriginal.ToString("yyyy-MM-dd HH:mm:ss");

        if (exifSubIfd.TryGetRational(ExifDirectoryBase.TagExposureTime, out var exposureTime))
            result["exposureTime"] = exposureTime.Denominator > 1
                ? $"1/{(int)(exposureTime.Denominator / exposureTime.Numerator)}s"
                : $"{exposureTime.ToDouble():F1}s";

        if (exifSubIfd.TryGetRational(ExifDirectoryBase.TagFNumber, out var fNumber))
            result["aperture"] = $"f/{fNumber.ToDouble():F1}";

        if (exifSubIfd.TryGetInt32(ExifDirectoryBase.TagIsoEquivalent, out var iso))
            result["iso"] = iso;

        if (exifSubIfd.TryGetRational(ExifDirectoryBase.TagFocalLength, out var focalLength))
            result["focalLength"] = $"{focalLength.ToDouble():F0}mm";

        if (exifSubIfd.TryGetInt32(ExifDirectoryBase.TagExifImageWidth, out var width))
            result["imageWidth"] = width;
        if (exifSubIfd.TryGetInt32(ExifDirectoryBase.TagExifImageHeight, out var height))
            result["imageHeight"] = height;

        if (exifSubIfd.TryGetInt32(ExifDirectoryBase.TagFlash, out var flash))
            result["flash"] = (flash & 1) == 1 ? "Fired" : "Did not fire";
    }

    private static void ExtractIptcData(IReadOnlyList<MetadataExtractor.Directory> directories, Dictionary<string, object> result)
    {
        var iptc = directories.OfType<IptcDirectory>().FirstOrDefault();
        if (iptc == null) return;

        var byLine = iptc.GetString(IptcDirectory.TagByLine);
        if (!string.IsNullOrWhiteSpace(byLine) && !result.ContainsKey("artist"))
            result["artist"] = byLine;

        var credit = iptc.GetString(IptcDirectory.TagCredit);
        if (!string.IsNullOrWhiteSpace(credit))
            result["credit"] = credit;

        var caption = iptc.GetString(IptcDirectory.TagCaption);
        if (!string.IsNullOrWhiteSpace(caption))
            result["caption"] = caption;

        var source = iptc.GetString(IptcDirectory.TagSource);
        if (!string.IsNullOrWhiteSpace(source))
            result["source"] = source;

        var keywords = iptc.GetStringArray(IptcDirectory.TagKeywords);
        if (keywords != null && keywords.Length > 0)
            result["keywords"] = string.Join(", ", keywords);
    }

    private static void ExtractGpsData(IReadOnlyList<MetadataExtractor.Directory> directories, Dictionary<string, object> result)
    {
        var gps = directories.OfType<MetadataExtractor.Formats.Exif.GpsDirectory>().FirstOrDefault();
        if (gps == null) return;

        var location = gps.GetGeoLocation();
        if (location != null)
        {
            result["gpsLatitude"] = location.Latitude;
            result["gpsLongitude"] = location.Longitude;
        }
    }
}

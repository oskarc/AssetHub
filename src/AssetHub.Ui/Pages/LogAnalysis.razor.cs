using System.Text;
using AssetHub.Application.Dtos;
using AssetHub.Ui.Services;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.Localization;
using MudBlazor;

namespace AssetHub.Ui.Pages;

/// <summary>
/// Code-behind for LogAnalysis.razor.
/// Kept in a separate file to avoid Razor parser issues with
/// C# switch expressions and string literals that contain HTML-like characters.
/// </summary>
public partial class LogAnalysis
{
    // ── Injected services (declared in .razor via @inject) ─────────────────
    // Api, Feedback, CommonLoc, Loc are injected in the .razor file.
    // These properties must be declared here to be accessible from code-behind.
    [Microsoft.AspNetCore.Components.Inject]
    private AssetHubApiClient Api { get; set; } = default!;

    [Microsoft.AspNetCore.Components.Inject]
    private IUserFeedbackService Feedback { get; set; } = default!;

    [Microsoft.AspNetCore.Components.Inject]
    private IStringLocalizer<AssetHub.Ui.Resources.CommonResource> CommonLoc { get; set; } = default!;

    [Microsoft.AspNetCore.Components.Inject]
    private IStringLocalizer<AssetHub.Ui.Resources.LogAnalysisResource> Loc { get; set; } = default!;

    // ── State ──────────────────────────────────────────────────────────────
    private IBrowserFile? _selectedFile;
    private LogAnalysisResult? _result;
    private bool _analyzing;
    private string? _uploadError;

    // ── SVG chart constants ────────────────────────────────────────────────
    private const int SvgWidth = 900;
    private const int SvgHeight = 200;
    private const int SvgPaddingLeft = 10;
    private const int SvgPaddingRight = 10;
    private const int SvgPaddingTop = 10;
    private const int SvgPaddingBottom = 20;

    private static readonly string[] LevelDisplayOrder = new[] { "ERROR", "WARN", "INFO", "DEBUG", "UNKNOWN" };

    // ── Level display helpers ──────────────────────────────────────────────

    private IEnumerable<(string Label, int Count, string BorderStyle, string TextStyle)> GetLevelItems()
    {
        if (_result is null) yield break;
        foreach (var level in LevelDisplayOrder)
        {
            if (_result.CountByLevel.TryGetValue(level, out var count) && count > 0)
            {
                var color = GetLevelColor(level);
                yield return (
                    Label: GetLevelLabel(level),
                    Count: count,
                    BorderStyle: string.Concat("border-left: 4px solid ", color),
                    TextStyle: string.Concat("color:", color));
            }
        }
    }

    // ── Event handlers ─────────────────────────────────────────────────────

    private void OnFileChanged(IBrowserFile? file)
    {
        _selectedFile = file;
        _uploadError = null;
        _result = null;
    }

    private async Task AnalyzeAsync()
    {
        if (_selectedFile is null)
        {
            _uploadError = Loc["Upload_Error_NoFile"];
            return;
        }

        var ext = Path.GetExtension(_selectedFile.Name).ToLowerInvariant();
        var allowed = new HashSet<string> { ".log", ".txt", ".ndjson", ".jsonl" };
        if (!allowed.Contains(ext))
        {
            _uploadError = Loc["Upload_Error_InvalidType"];
            return;
        }

        _uploadError = null;
        _analyzing = true;
        _result = null;
        StateHasChanged();

        try
        {
            const long maxBytes = 50L * 1024 * 1024;
            await using var stream = _selectedFile.OpenReadStream(maxAllowedSize: maxBytes);
            _result = await Api.AnalyzeLogFileAsync(stream, _selectedFile.Name);
        }
        catch (ApiException ex)
        {
            Feedback.HandleApiError(ex, "analyze log file");
        }
        catch (Exception ex)
        {
            Feedback.HandleError(ex, "analyze log file");
        }
        finally
        {
            _analyzing = false;
        }
    }

    private void ClearResults()
    {
        _selectedFile = null;
        _result = null;
        _uploadError = null;
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static string FormatFileSize(long bytes)
    {
        if (bytes < 1024) return string.Concat(bytes, " B");
        if (bytes < 1024 * 1024) return string.Concat((bytes / 1024.0).ToString("F1"), " KB");
        return string.Concat((bytes / 1024.0 / 1024.0).ToString("F1"), " MB");
    }

    private string GetLevelLabel(string level) => level switch
    {
        "ERROR" => Loc["Results_Level_ERROR"],
        "WARN" => Loc["Results_Level_WARN"],
        "INFO" => Loc["Results_Level_INFO"],
        "DEBUG" => Loc["Results_Level_DEBUG"],
        _ => Loc["Results_Level_UNKNOWN"]
    };

    private static string GetLevelColor(string level) => level switch
    {
        "ERROR" => "#f44336",
        "WARN" => "#ff9800",
        "INFO" => "#2196f3",
        "DEBUG" => "#9e9e9e",
        _ => "#607d8b"
    };

    private static Color GetLevelChipColor(string level) => level switch
    {
        "ERROR" => Color.Error,
        "WARN" => Color.Warning,
        "INFO" => Color.Info,
        _ => Color.Default
    };

    private string GetTrendGranularityLabel()
    {
        var g = _result?.TrendGranularity;
        return string.IsNullOrEmpty(g) ? string.Empty : Loc[string.Concat("Results_Trend_Granularity_", g)];
    }

    /// <summary>
    /// Builds the SVG markup for the trend bar chart as a string.
    /// Kept in code-behind to avoid Razor parser issues with angle brackets in strings.
    /// </summary>
    private string BuildTrendSvg()
    {
        if (_result is null || _result.TrendData.Count == 0)
            return string.Empty;

        var pts = _result.TrendData;
        var barArea = SvgHeight - SvgPaddingTop - SvgPaddingBottom;
        var maxVal = pts.Max(p => p.Errors + p.Warnings + p.Info);
        if (maxVal == 0) maxVal = 1;

        var availableWidth = SvgWidth - SvgPaddingLeft - SvgPaddingRight;
        var barW = Math.Max(2, availableWidth / pts.Count - 2);
        var step = availableWidth / (double)pts.Count;
        var labelEvery = Math.Max(1, pts.Count / 10);

        var sb = new StringBuilder(pts.Count * 200);
        sb.Append("<svg viewBox=\"0 0 ")
          .Append(SvgWidth).Append(' ').Append(SvgHeight)
          .Append("\" style=\"width:100%;min-width:300px;height:").Append(SvgHeight)
          .Append("px\" role=\"img\" aria-label=\"Log volume trend chart\">");

        // Grid lines
        for (int gi = 0; gi <= 4; gi++)
        {
            var gy = SvgPaddingTop + (int)(barArea * gi / 4.0);
            sb.Append("<line x1=\"").Append(SvgPaddingLeft)
              .Append("\" y1=\"").Append(gy)
              .Append("\" x2=\"").Append(SvgWidth - SvgPaddingRight)
              .Append("\" y2=\"").Append(gy)
              .Append("\" stroke=\"currentColor\" stroke-opacity=\"0.1\" stroke-width=\"1\"/>");
        }

        for (int i = 0; i < pts.Count; i++)
        {
            var p = pts[i];
            var x = (int)(SvgPaddingLeft + i * step + (step - barW) / 2);
            var total = p.Errors + p.Warnings + p.Info;
            var totalH = (int)(barArea * total / (double)maxVal);
            var errH = total > 0 ? (int)(totalH * p.Errors / (double)total) : 0;
            var warnH = total > 0 ? (int)(totalH * p.Warnings / (double)total) : 0;
            var infoH = totalH - errH - warnH;
            var baseY = SvgPaddingTop + barArea;

            if (infoH > 0)
                AppendRect(sb, x, baseY - totalH + errH + warnH, barW, infoH, "#4caf50", "0.75",
                    EscapeXml(p.Label) + " \u2014 Info: " + p.Info);
            if (warnH > 0)
                AppendRect(sb, x, baseY - totalH + errH, barW, warnH, "#ff9800", "0.85",
                    EscapeXml(p.Label) + " \u2014 Warnings: " + p.Warnings);
            if (errH > 0)
                AppendRect(sb, x, baseY - totalH, barW, errH, "#f44336", "0.9",
                    EscapeXml(p.Label) + " \u2014 Errors: " + p.Errors);

            if (i % labelEvery == 0)
            {
                sb.Append("<text x=\"").Append(x + barW / 2)
                  .Append("\" y=\"").Append(SvgHeight - 4)
                  .Append("\" text-anchor=\"middle\" font-size=\"9\" fill=\"currentColor\" opacity=\"0.6\">")
                  .Append(EscapeXml(p.Label)).Append("</text>");
            }
        }

        sb.Append("</svg>");
        return sb.ToString();
    }

    private static void AppendRect(StringBuilder sb, int x, int y, int w, int h,
        string fill, string opacity, string title)
    {
        sb.Append("<rect x=\"").Append(x)
          .Append("\" y=\"").Append(y)
          .Append("\" width=\"").Append(w)
          .Append("\" height=\"").Append(h)
          .Append("\" fill=\"").Append(fill)
          .Append("\" opacity=\"").Append(opacity)
          .Append("\"><title>").Append(title).Append("</title></rect>");
    }

    private static string EscapeXml(string value) =>
        value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
}

namespace AssetHub.Application.Messages;

// ── Commands ─────────────────────────────────────────────────────────────

/// <summary>
/// Build the analytics dashboard PDF for a queued <c>AnalyticsPdfJob</c>
/// (T5-ANL-01). Worker handler: renders the dashboard via QuestPDF, uploads
/// to MinIO at <c>analytics-pdfs/{jobId}.pdf</c>, updates the job row with
/// <c>Status = Ready</c> + <c>ObjectKey</c>, audits <c>analytics.exported</c>.
/// On exception, marks the job <c>Failed</c> with the message and audits
/// the failure.
/// </summary>
public record BuildAnalyticsPdfCommand
{
    public Guid JobId { get; init; }
}

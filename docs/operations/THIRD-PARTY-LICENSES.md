# Third-party licenses with operational constraints

Most of AssetHub's NuGet dependencies ship under permissive licenses (MIT,
Apache-2.0, BSD) and require nothing beyond inclusion in the OSS notices.
This file lists the dependencies whose license **terms can constrain how
you deploy AssetHub** — typically because the upstream project gates
commercial use on revenue, license keys, or both.

If you operate AssetHub for a customer or organisation, audit this list
before going live.

---

## QuestPDF (analytics PDF export — T5-ANL-01)

| | |
|---|---|
| **Package** | `QuestPDF` |
| **Used by** | `src/AssetHub.Worker/Handlers/BuildAnalyticsPdfHandler.cs` (renders the analytics dashboard as a PDF on demand) |
| **Default license tier** | **Community** — free for organisations with **annual gross revenue under $1M USD** or for non-commercial / educational use. |
| **Commercial tiers** | **Professional** and **Enterprise** — paid, required when revenue ≥ $1M USD or for redistribution. See <https://www.questpdf.com/license/>. |
| **Activation** | The handler calls `QuestPDF.Settings.License = LicenseType.Community;` once per process. To use a commercial license, change this to `LicenseType.Professional` (or `Enterprise`) and set the license key via the `QUESTPDF_LICENSE_KEY` environment variable per QuestPDF docs. |

### Why this matters

QuestPDF Community is a **license type**, not just a free tier — calling
`Settings.License = LicenseType.Community` is a legal assertion that the
deploying organisation is under the revenue threshold. Operators above
the threshold who deploy AssetHub as-is are out of compliance even though
the build succeeds.

### What deployers need to do

Pick **one** of the following before deploying T5-ANL-01 to a production
environment for an organisation at or above $1M USD annual revenue:

1. **Buy a QuestPDF license.** Update the handler to use `LicenseType.Professional`
   (or Enterprise) and provide the key via `QUESTPDF_LICENSE_KEY`. License
   purchasing: <https://www.questpdf.com/license/>.
2. **Replace the renderer.** `BuildAnalyticsPdfHandler` is a small file —
   the rendering happens in two `Render*Section` methods plus a single
   `Document.Create(...).GeneratePdf()` call. Swap in another PDF library
   (`IronPDF`, `Spire.PDF.Free`, `PdfSharp`, raw `Aspose.PDF`, etc.). The
   rest of the analytics pipeline is engine-agnostic.
3. **Disable the PDF export.** If your organisation can't satisfy either
   of the above and PDF exports aren't required, remove the
   `Btn_ExportPdf` button from `Pages/AdminAnalytics.razor` and the
   `MapPdfExport` call in `Endpoints/AnalyticsEndpoints.cs`. The CSV
   exports on each panel still work and don't depend on QuestPDF.

### What we ship

Build pipelines reference QuestPDF via `<PackageReference>` in
`src/AssetHub.Worker/AssetHub.Worker.csproj` and the Community license is
asserted at handler-bootstrap time. There is no key in the repo. The
inline XML comment on the package reference points back to this file as
the canonical constraint description so future maintainers don't lose
context.

---

## Other constrained dependencies

None at present. If a future feature pulls in another revenue-gated or
key-required dependency, add a section above following the same layout
(package, used by, default tier, commercial tiers, activation, what
deployers need to do).

namespace AssetHub.Ui.Resources;

// Empty by design — IStringLocalizer<T> requires a non-generic marker type
// per resource file. Suppressing S2094 ("Remove this empty class…") for the
// whole file because every line below is intentionally hollow.
#pragma warning disable S2094

/// <summary>
/// Marker classes used by IStringLocalizer&lt;T&gt; to resolve .resx resource files.
/// Each class corresponds to a resource file pair (e.g., CommonResource.resx / CommonResource.sv.resx).
/// </summary>
/// <remarks>
/// Resource keys follow the pattern: Area_Context_Element (e.g., Nav_Home, Btn_Cancel, Empty_Title).
/// </remarks>
public class CommonResource { }
public class AssetsResource { }
public class CollectionsResource { }
public class AdminResource { }
public class SharesResource { }
public class ImageEditorResource { }
public class AccountResource { }
public class NotificationsResource { }
public class CommentsResource { }
public class WorkflowResource { }
public class WebhooksResource { }
public class BrandsResource { }
public class GuestsResource { }
public class WatermarksResource { }
public class AnalyticsResource { }
public class AuditAdminResource { }
public class TaxonomiesAdminResource { }
public class TrashAdminResource { }
public class ExportPresetsAdminResource { }
public class MetadataSchemasAdminResource { }
public class MigrationsResource { }

#pragma warning restore S2094

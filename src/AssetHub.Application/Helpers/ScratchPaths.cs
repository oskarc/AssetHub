namespace AssetHub.Application.Helpers;

/// <summary>
/// Process-private scratch directory for short-lived files written
/// during media processing and zip building. Replaces
/// <c>Path.GetTempPath()</c> at write sites — Sonar S5443.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Directory.CreateTempSubdirectory(string)"/> creates a
/// fresh sub-directory under the system temp dir with permissions
/// <c>0700</c> on Unix (owner-read/write/execute only). On Windows the
/// inherited ACL of the temp root applies.
/// </para>
/// <para>
/// The directory is process-scoped and lazily created on first use.
/// Files inside are still cleaned up by the caller's <c>finally</c>
/// blocks; the directory itself lives for the process lifetime.
/// </para>
/// </remarks>
public static class ScratchPaths
{
    private static readonly Lazy<string> _root = new(
        () => Directory.CreateTempSubdirectory("assethub-").FullName);

    /// <summary>
    /// Returns a path inside the process-private scratch directory by
    /// joining the supplied <paramref name="fileName"/>. Caller is
    /// responsible for choosing a unique name (typically
    /// <c>Path.GetRandomFileName()</c> or a Guid).
    /// </summary>
    public static string Combine(string fileName)
        => Path.Combine(_root.Value, fileName);
}

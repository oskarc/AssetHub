namespace Dam.Application.Services;

/// <summary>
/// Framework-agnostic abstraction for streaming a ZIP response.
/// Removes the need for HttpContext in the Application layer.
/// Created from HttpContext at the endpoint level.
/// </summary>
public sealed class ZipStreamContext
{
    /// <summary>The output stream to write the ZIP data to (e.g. HttpResponse.BodyWriter.AsStream()).</summary>
    public required Stream OutputStream { get; init; }

    /// <summary>Callback to set a response header (e.g. Content-Type, Content-Disposition).</summary>
    public required Action<string, string> SetHeader { get; init; }
}

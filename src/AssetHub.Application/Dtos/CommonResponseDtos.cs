namespace AssetHub.Application.Dtos;

/// <summary>
/// A simple response containing only a message.
/// </summary>
public class MessageResponse
{
    public string Message { get; set; } = "";

    public MessageResponse() { }
    public MessageResponse(string message) => Message = message;
}

/// <summary>
/// Response after revoking a share.
/// </summary>
public class ShareRevokedResponse
{
    public string Message { get; set; } = "";
    public DateTime? RevokedAt { get; set; }
}

/// <summary>
/// Response after setting or updating collection access.
/// </summary>
public class AccessUpdatedResponse
{
    public string Message { get; set; } = "";
    public Guid CollectionId { get; set; }
    public string PrincipalId { get; set; } = "";
    public string Role { get; set; } = "";
}

/// <summary>
/// Response after revoking collection access.
/// </summary>
public class AccessRevokedResponse
{
    public string Message { get; set; } = "";
    public Guid CollectionId { get; set; }
    public string PrincipalId { get; set; } = "";
}

/// <summary>
/// Response after adding an asset to a collection.
/// </summary>
public class AssetAddedToCollectionResponse
{
    public Guid AssetId { get; set; }
    public Guid CollectionId { get; set; }
    public DateTime AddedAt { get; set; }
    public string Message { get; set; } = "";
}

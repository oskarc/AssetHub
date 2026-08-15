namespace AssetHub.Application.Services;

/// <summary>
/// Mints a single-use password-reset link for a user and emails it.
/// </summary>
/// <remarks>
/// Declared in Application so <c>IUserDirectoryAdmin</c> implementations can
/// depend on it, but deliberately generic over the user type — the concrete
/// Identity user lives in Infrastructure, and Application must not reference it.
/// </remarks>
/// <typeparam name="TUser">The identity provider's user type.</typeparam>
public interface IPasswordResetLinkSender<in TUser>
{
    /// <summary>
    /// Generates a reset token for <paramref name="user"/> and sends the link.
    /// The token is never returned to the caller or logged.
    /// </summary>
    /// <param name="user">The account to send the link for.</param>
    /// <param name="isNewAccount">Changes the wording to account setup.</param>
    /// <param name="ct">Cancellation token.</param>
    Task SendAsync(TUser user, bool isNewAccount = false, CancellationToken ct = default);
}

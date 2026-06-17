using AssetHub.Application.Dtos;

namespace AssetHub.Ui.Services;

public sealed partial class AssetHubApiClient
{
    public async Task<List<GuestInvitationResponseDto>> GetGuestInvitationsAsync(CancellationToken ct = default)
    {
        var result = await guestInvitationService.ListAsync(ct);
        return Unwrap(result, "Get guest invitations");
    }

    public async Task<CreatedGuestInvitationDto> CreateGuestInvitationAsync(
        CreateGuestInvitationDto dto, CancellationToken ct = default)
    {
        Validate(dto, "Create guest invitation");
        var result = await guestInvitationService.CreateAsync(dto, BaseUrl(), ct);
        return Unwrap(result, "Create guest invitation");
    }

    public async Task RevokeGuestInvitationAsync(Guid id, CancellationToken ct = default)
    {
        var result = await guestInvitationService.RevokeAsync(id, ct);
        EnsureSuccess(result, "Revoke guest invitation");
    }

    public async Task<AcceptGuestInvitationResponseDto> AcceptGuestInvitationAsync(
        string token, CancellationToken ct = default)
    {
        var result = await guestInvitationService.AcceptAsync(token, ct);
        return Unwrap(result, "Accept guest invitation");
    }
}

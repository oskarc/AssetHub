using AssetHub.Application.Dtos;

namespace AssetHub.Ui.Services;

public sealed partial class AssetHubApiClient
{
    public async Task<List<PersonalAccessTokenDto>> GetMyPersonalAccessTokensAsync(CancellationToken ct = default)
    {
        var result = await personalAccessTokenService.ListMineAsync(ct);
        return Unwrap(result, "List personal access tokens");
    }

    public async Task<CreatedPersonalAccessTokenDto> CreatePersonalAccessTokenAsync(
        CreatePersonalAccessTokenRequest request,
        CancellationToken ct = default)
    {
        Validate(request, "Create personal access token");
        var result = await personalAccessTokenService.CreateAsync(request, ct);
        return Unwrap(result, "Create personal access token");
    }

    public async Task RevokePersonalAccessTokenAsync(Guid id, CancellationToken ct = default)
    {
        var result = await personalAccessTokenService.RevokeAsync(id, ct);
        EnsureSuccess(result, "Revoke personal access token");
    }
}

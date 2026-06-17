using AssetHub.Application.Dtos;

namespace AssetHub.Ui.Services;

public sealed partial class AssetHubApiClient
{
    public async Task<List<WebhookResponseDto>> GetWebhooksAsync(CancellationToken ct = default)
    {
        var result = await webhookService.ListAsync(ct);
        return Unwrap(result, "Get webhooks");
    }

    public async Task<CreatedWebhookDto> CreateWebhookAsync(CreateWebhookDto dto, CancellationToken ct = default)
    {
        Validate(dto, "Create webhook");
        var result = await webhookService.CreateAsync(dto, ct);
        return Unwrap(result, "Create webhook");
    }

    public async Task<WebhookResponseDto> UpdateWebhookAsync(Guid id, UpdateWebhookDto dto, CancellationToken ct = default)
    {
        Validate(dto, "Update webhook");
        var result = await webhookService.UpdateAsync(id, dto, ct);
        return Unwrap(result, "Update webhook");
    }

    public async Task DeleteWebhookAsync(Guid id, CancellationToken ct = default)
    {
        var result = await webhookService.DeleteAsync(id, ct);
        EnsureSuccess(result, "Delete webhook");
    }

    public async Task<CreatedWebhookDto> RotateWebhookSecretAsync(Guid id, CancellationToken ct = default)
    {
        var result = await webhookService.RotateSecretAsync(id, ct);
        return Unwrap(result, "Rotate webhook secret");
    }

    public async Task<WebhookDeliveryResponseDto> SendWebhookTestAsync(Guid id, CancellationToken ct = default)
    {
        var result = await webhookService.SendTestAsync(id, ct);
        return Unwrap(result, "Send webhook test");
    }

    public async Task<List<WebhookDeliveryResponseDto>> GetWebhookDeliveriesAsync(
        Guid id, int take = 50, CancellationToken ct = default)
    {
        var result = await webhookService.ListDeliveriesAsync(id, take, ct);
        return Unwrap(result, "Get webhook deliveries");
    }
}

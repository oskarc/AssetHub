using AssetHub.Ui.Resources;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using MudBlazor;
using System.Net;

namespace AssetHub.Ui.Services;

/// <summary>
/// Implementation of <see cref="IUserFeedbackService"/> using MudBlazor's Snackbar.
/// Provides consistent user-facing feedback with proper error message sanitization.
/// </summary>
public sealed class UserFeedbackService : IUserFeedbackService
{
    private readonly ISnackbar _snackbar;
    private readonly ILogger<UserFeedbackService> _logger;
    private readonly IStringLocalizer<CommonResource> _loc;

    // Configuration for snackbar display
    private const int SuccessDurationMs = 3000;
    private const int InfoDurationMs = 4000;
    private const int WarningDurationMs = 5000;
    private const int ErrorDurationMs = 6000;

    public UserFeedbackService(ISnackbar snackbar, ILogger<UserFeedbackService> logger, IStringLocalizer<CommonResource> loc)
    {
        _snackbar = snackbar;
        _logger = logger;
        _loc = loc;
    }

    public void ShowSuccess(string message)
    {
        _snackbar.Add(message, Severity.Success, config =>
        {
            config.VisibleStateDuration = SuccessDurationMs;
            config.Icon = Icons.Material.Filled.CheckCircle;
        });
    }

    public void ShowActionableInfo(string message, string actionLabel, Func<Task> onAction, int durationMs = 10000)
    {
        _snackbar.Add(message, Severity.Info, config =>
        {
            config.VisibleStateDuration = durationMs;
            config.Icon = Icons.Material.Filled.Info;
            config.Action = actionLabel;
            config.ActionColor = Color.Primary;
            config.OnClick = async _ =>
            {
                try
                {
                    await onAction();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Snackbar action callback failed");
                }
            };
        });
    }

    public void ShowInfo(string message)
    {
        _snackbar.Add(message, Severity.Info, config =>
        {
            config.VisibleStateDuration = InfoDurationMs;
            config.Icon = Icons.Material.Filled.Info;
        });
    }

    public void ShowWarning(string message)
    {
        _snackbar.Add(message, Severity.Warning, config =>
        {
            config.VisibleStateDuration = WarningDurationMs;
            config.Icon = Icons.Material.Filled.Warning;
        });
    }

    public void ShowError(string message)
    {
        _snackbar.Add(message, Severity.Error, config =>
        {
            config.VisibleStateDuration = ErrorDurationMs;
            config.Icon = Icons.Material.Filled.Error;
            config.RequireInteraction = true; // Require user to dismiss errors
        });
    }

    public void HandleError(Exception ex, string operationName)
    {
        // Log the full exception for debugging
        _logger.LogError(ex, "Error during operation: {OperationName}", operationName);

        // Show user-friendly message
        var userMessage = GetUserFriendlyMessage(ex, operationName);
        ShowError(userMessage);
    }

    public void HandleApiError(ApiException ex, string operationName)
    {
        // Log with context
        _logger.LogError(ex, "API error during '{OperationName}': {Message} (Status: {StatusCode})",
            operationName, ex.Message, ex.StatusCode);

        // For API exceptions, we can often use the message directly as it's already sanitized
        var userMessage = GetApiErrorMessage(ex, operationName);
        ShowError(userMessage);
    }

    public async Task<bool> ExecuteWithFeedbackAsync(Func<Task> operation, string operationName, string? successMessage = null)
    {
        try
        {
            await operation();
            if (successMessage is not null)
            {
                ShowSuccess(successMessage);
            }
            return true;
        }
        catch (OperationCanceledException)
        {
            // Component disposed or navigation — not a user-facing error. (TaskCanceledException derives from this.)
            return false;
        }
        catch (ApiException ex)
        {
            HandleApiError(ex, operationName);
            return false;
        }
        catch (Exception ex)
        {
            HandleError(ex, operationName);
            return false;
        }
    }

    public async Task<(bool Success, T? Result)> ExecuteWithFeedbackAsync<T>(Func<Task<T>> operation, string operationName, string? successMessage = null)
    {
        try
        {
            var result = await operation();
            if (successMessage is not null)
            {
                ShowSuccess(successMessage);
            }
            return (true, result);
        }
        catch (OperationCanceledException)
        {
            // Component disposed or navigation — not a user-facing error. (TaskCanceledException derives from this.)
            return (false, default);
        }
        catch (ApiException ex)
        {
            HandleApiError(ex, operationName);
            return (false, default);
        }
        catch (Exception ex)
        {
            HandleError(ex, operationName);
            return (false, default);
        }
    }

    /// <summary>
    /// Converts exceptions to user-friendly messages.
    /// </summary>
    private string GetUserFriendlyMessage(Exception ex, string operationName)
    {
        // Handle specific exception types
        return ex switch
        {
            ApiException apiEx => GetApiErrorMessage(apiEx, operationName),
            TaskCanceledException => _loc["Feedback_RequestTimedOut"],
            OperationCanceledException => _loc["Feedback_OperationCancelled"],
            UnauthorizedAccessException => _loc["Feedback_NoPermission"],
            ArgumentException argEx when !string.IsNullOrEmpty(argEx.Message) => argEx.Message,
            _ => string.Format(_loc["Feedback_GenericError"], operationName)
        };
    }

    private static readonly Dictionary<HttpStatusCode, string> ApiErrorResourceKeys = new()
    {
        [HttpStatusCode.BadRequest] = "Feedback_InvalidRequest",
        [HttpStatusCode.Unauthorized] = "Feedback_SignInRequired",
        [HttpStatusCode.Forbidden] = "Feedback_NoPermission",
        [HttpStatusCode.NotFound] = "Feedback_ItemNotFound",
        [HttpStatusCode.Conflict] = "Feedback_ConflictError",
        [HttpStatusCode.RequestEntityTooLarge] = "Feedback_FileTooLarge",
        [HttpStatusCode.UnprocessableEntity] = "Feedback_InvalidInput",
        [HttpStatusCode.TooManyRequests] = "Feedback_TooManyRequests",
        [HttpStatusCode.InternalServerError] = "Feedback_ServerError",
        [HttpStatusCode.BadGateway] = "Feedback_ServiceUnavailable",
        [HttpStatusCode.ServiceUnavailable] = "Feedback_ServiceUnavailable",
        [HttpStatusCode.GatewayTimeout] = "Feedback_ServiceUnavailable",
    };

    /// <summary>
    /// Converts API exceptions to user-friendly messages based on status code.
    /// </summary>
    private string GetApiErrorMessage(ApiException ex, string operationName)
    {
        // If the API returned a specific error message, use it (already sanitized by API)
        if (!string.IsNullOrWhiteSpace(ex.Message) && ex.Message != "null")
            return ex.Message;

        return ApiErrorResourceKeys.TryGetValue(ex.StatusCode, out var key)
            ? _loc[key]
            : string.Format(_loc["Feedback_GenericApiError"], operationName);
    }
}

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
public class UserFeedbackService : IUserFeedbackService
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

    public async Task<bool> ExecuteWithFeedbackAsync(Func<Task> operation, string operationName, string? successMessage = null, int maxRetries = 0)
    {
        for (var attempt = 0; attempt <= maxRetries; attempt++)
        {
            try
            {
                await operation();

                if (successMessage != null)
                {
                    ShowSuccess(successMessage);
                }

                return true;
            }
            catch (Exception ex) when (attempt < maxRetries && IsTransient(ex))
            {
                _logger.LogWarning(ex, "Transient error during '{OperationName}', retrying ({Attempt}/{MaxRetries})",
                    operationName, attempt + 1, maxRetries);
                ShowWarning(string.Format(_loc["Feedback_Retrying"], attempt + 1, maxRetries));
                await Task.Delay(1000 * (attempt + 1));
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

        return false;
    }

    public async Task<(bool Success, T? Result)> ExecuteWithFeedbackAsync<T>(Func<Task<T>> operation, string operationName, string? successMessage = null, int maxRetries = 0)
    {
        for (var attempt = 0; attempt <= maxRetries; attempt++)
        {
            try
            {
                var result = await operation();

                if (successMessage != null)
                {
                    ShowSuccess(successMessage);
                }

                return (true, result);
            }
            catch (Exception ex) when (attempt < maxRetries && IsTransient(ex))
            {
                _logger.LogWarning(ex, "Transient error during '{OperationName}', retrying ({Attempt}/{MaxRetries})",
                    operationName, attempt + 1, maxRetries);
                ShowWarning(string.Format(_loc["Feedback_Retrying"], attempt + 1, maxRetries));
                await Task.Delay(1000 * (attempt + 1));
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

        return (false, default);
    }

    private static bool IsTransient(Exception ex) => ex switch
    {
        HttpRequestException => true,
        TaskCanceledException => true,
        IOException => true,
        ApiException apiEx => apiEx.StatusCode is
            System.Net.HttpStatusCode.InternalServerError or
            System.Net.HttpStatusCode.BadGateway or
            System.Net.HttpStatusCode.ServiceUnavailable or
            System.Net.HttpStatusCode.GatewayTimeout or
            System.Net.HttpStatusCode.RequestTimeout,
        _ => false
    };

    /// <summary>
    /// Converts exceptions to user-friendly messages.
    /// </summary>
    private string GetUserFriendlyMessage(Exception ex, string operationName)
    {
        // Handle specific exception types
        return ex switch
        {
            ApiException apiEx => GetApiErrorMessage(apiEx, operationName),
            HttpRequestException httpEx => GetHttpErrorMessage(httpEx, operationName),
            TaskCanceledException => _loc["Feedback_RequestTimedOut"],
            OperationCanceledException => _loc["Feedback_OperationCancelled"],
            UnauthorizedAccessException => _loc["Feedback_NoPermission"],
            ArgumentException argEx when !string.IsNullOrEmpty(argEx.Message) => argEx.Message,
            _ => string.Format(_loc["Feedback_GenericError"], operationName)
        };
    }

    /// <summary>
    /// Converts API exceptions to user-friendly messages based on status code.
    /// </summary>
    private string GetApiErrorMessage(ApiException ex, string operationName)
    {
        // If the API returned a specific error message, use it (already sanitized by API)
        if (!string.IsNullOrWhiteSpace(ex.Message) && ex.Message != "null")
        {
            return ex.Message;
        }

        // Otherwise, provide a generic message based on status code
        return ex.StatusCode switch
        {
            HttpStatusCode.BadRequest => _loc["Feedback_InvalidRequest"],
            HttpStatusCode.Unauthorized => _loc["Feedback_SignInRequired"],
            HttpStatusCode.Forbidden => _loc["Feedback_NoPermission"],
            HttpStatusCode.NotFound => _loc["Feedback_ItemNotFound"],
            HttpStatusCode.Conflict => _loc["Feedback_ConflictError"],
            HttpStatusCode.RequestEntityTooLarge => _loc["Feedback_FileTooLarge"],
            HttpStatusCode.UnprocessableEntity => _loc["Feedback_InvalidInput"],
            HttpStatusCode.TooManyRequests => _loc["Feedback_TooManyRequests"],
            HttpStatusCode.InternalServerError => _loc["Feedback_ServerError"],
            HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout 
                => _loc["Feedback_ServiceUnavailable"],
            _ => string.Format(_loc["Feedback_GenericApiError"], operationName)
        };
    }

    /// <summary>
    /// Converts HTTP request exceptions to user-friendly messages.
    /// </summary>
    private string GetHttpErrorMessage(HttpRequestException ex, string operationName)
    {
        if (ex.Message.Contains("No connection could be made", StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("Connection refused", StringComparison.OrdinalIgnoreCase))
        {
            return _loc["Feedback_ConnectionFailed"];
        }

        if (ex.Message.Contains("SSL", StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("certificate", StringComparison.OrdinalIgnoreCase))
        {
            return _loc["Feedback_SecureConnectionFailed"];
        }

        return string.Format(_loc["Feedback_NetworkError"], operationName);
    }
}

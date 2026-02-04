using Microsoft.Extensions.Logging;
using MudBlazor;
using System.Net;

namespace Dam.Ui.Services;

/// <summary>
/// Implementation of <see cref="IUserFeedbackService"/> using MudBlazor's Snackbar.
/// Provides consistent user-facing feedback with proper error message sanitization.
/// </summary>
public class UserFeedbackService : IUserFeedbackService
{
    private readonly ISnackbar _snackbar;
    private readonly ILogger<UserFeedbackService> _logger;

    // Configuration for snackbar display
    private const int SuccessDurationMs = 3000;
    private const int InfoDurationMs = 4000;
    private const int WarningDurationMs = 5000;
    private const int ErrorDurationMs = 6000;

    public UserFeedbackService(ISnackbar snackbar, ILogger<UserFeedbackService> logger)
    {
        _snackbar = snackbar;
        _logger = logger;
    }

    public void ShowSuccess(string message)
    {
        _snackbar.Add(message, Severity.Success, config =>
        {
            config.VisibleStateDuration = SuccessDurationMs;
            config.Icon = Icons.Material.Filled.CheckCircle;
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
        _logger.LogError(ex, $"Error during operation: {operationName}");

        // Show user-friendly message
        var userMessage = GetUserFriendlyMessage(ex, operationName);
        ShowError(userMessage);
    }

    public void HandleApiError(ApiException ex, string operationName)
    {
        // Log with context
        _logger.LogError($"API error during '{operationName}': {ex.Message} (Status: {ex.StatusCode})");

        // For API exceptions, we can often use the message directly as it's already sanitized
        var userMessage = GetApiErrorMessage(ex, operationName);
        ShowError(userMessage);
    }

    public async Task<bool> ExecuteWithFeedbackAsync(Func<Task> operation, string operationName, string? successMessage = null)
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

            if (successMessage != null)
            {
                ShowSuccess(successMessage);
            }

            return (true, result);
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
            HttpRequestException httpEx => GetHttpErrorMessage(httpEx, operationName),
            TaskCanceledException => $"The request timed out. Please try again.",
            OperationCanceledException => "The operation was cancelled.",
            UnauthorizedAccessException => "You don't have permission to perform this action.",
            ArgumentException argEx when !string.IsNullOrEmpty(argEx.Message) => argEx.Message,
            _ => $"Something went wrong while trying to {operationName}. Please try again."
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
            HttpStatusCode.BadRequest => $"Invalid request. Please check your input and try again.",
            HttpStatusCode.Unauthorized => "You need to sign in to perform this action.",
            HttpStatusCode.Forbidden => "You don't have permission to perform this action.",
            HttpStatusCode.NotFound => $"The requested item was not found. It may have been deleted.",
            HttpStatusCode.Conflict => "This operation conflicts with existing data. Please refresh and try again.",
            HttpStatusCode.RequestEntityTooLarge => "The file is too large. Please try a smaller file.",
            HttpStatusCode.UnprocessableEntity => "The request couldn't be processed. Please check your input.",
            HttpStatusCode.TooManyRequests => "Too many requests. Please wait a moment and try again.",
            HttpStatusCode.InternalServerError => "A server error occurred. Please try again later.",
            HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout 
                => "The service is temporarily unavailable. Please try again in a few moments.",
            _ => $"Failed to {operationName}. Please try again."
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
            return "Unable to connect to the server. Please check your internet connection.";
        }

        if (ex.Message.Contains("SSL", StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("certificate", StringComparison.OrdinalIgnoreCase))
        {
            return "A secure connection could not be established. Please try again.";
        }

        return $"A network error occurred while trying to {operationName}. Please check your connection and try again.";
    }
}

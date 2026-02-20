using MudBlazor;

namespace AssetHub.Ui.Services;

/// <summary>
/// Service for providing consistent user feedback (toasts, loading states, error handling).
/// Centralizes user-facing messaging to ensure consistent UX across the application.
/// </summary>
public interface IUserFeedbackService
{
    /// <summary>
    /// Shows a success message.
    /// </summary>
    void ShowSuccess(string message);

    /// <summary>
    /// Shows an informational message.
    /// </summary>
    void ShowInfo(string message);

    /// <summary>
    /// Shows a warning message.
    /// </summary>
    void ShowWarning(string message);

    /// <summary>
    /// Shows an error message with a user-friendly format.
    /// </summary>
    void ShowError(string message);

    /// <summary>
    /// Handles an exception and shows a user-friendly error message.
    /// Logs the full exception details while showing a sanitized message to the user.
    /// </summary>
    /// <param name="ex">The exception that occurred.</param>
    /// <param name="operationName">A friendly name for the operation that failed (e.g., "load assets", "delete collection").</param>
    void HandleError(Exception ex, string operationName);

    /// <summary>
    /// Handles an ApiException specifically, using the API's error message.
    /// </summary>
    void HandleApiError(ApiException ex, string operationName);

    /// <summary>
    /// Wraps an async operation with automatic success/error feedback.
    /// </summary>
    /// <param name="operation">The async operation to execute.</param>
    /// <param name="operationName">Friendly name for the operation (e.g., "Delete asset").</param>
    /// <param name="successMessage">Optional custom success message. If null, no success message is shown.</param>
    /// <returns>True if the operation succeeded, false if it failed.</returns>
    Task<bool> ExecuteWithFeedbackAsync(Func<Task> operation, string operationName, string? successMessage = null);

    /// <summary>
    /// Wraps an async operation that returns a result with automatic success/error feedback.
    /// </summary>
    /// <typeparam name="T">The type of the result.</typeparam>
    /// <param name="operation">The async operation to execute.</param>
    /// <param name="operationName">Friendly name for the operation.</param>
    /// <param name="successMessage">Optional custom success message. If null, no success message is shown.</param>
    /// <returns>The result if successful, or default(T) if failed.</returns>
    Task<(bool Success, T? Result)> ExecuteWithFeedbackAsync<T>(Func<Task<T>> operation, string operationName, string? successMessage = null);
}

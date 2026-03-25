using Microsoft.Extensions.Logging;

namespace AssetHub.Ui.Tests.Services;

/// <summary>
/// Tests for the UserFeedbackService.
/// Verifies snackbar message display, error handling, and ExecuteWithFeedback patterns.
/// </summary>
public class UserFeedbackServiceTests
{
    private readonly Mock<ISnackbar> _mockSnackbar;
    private readonly Mock<ILogger<UserFeedbackService>> _mockLogger;
    private readonly Mock<IStringLocalizer<CommonResource>> _mockLocalizer;
    private readonly UserFeedbackService _sut;

    public UserFeedbackServiceTests()
    {
        _mockSnackbar = new Mock<ISnackbar>();
        _mockLogger = new Mock<ILogger<UserFeedbackService>>();
        _mockLocalizer = new Mock<IStringLocalizer<CommonResource>>();
        // Return realistic English strings for known resource keys so assertions match
        var feedbackStrings = new Dictionary<string, string>
        {
            ["Feedback_RequestTimedOut"] = "The request timed out. Please try again.",
            ["Feedback_OperationCancelled"] = "The operation was cancelled.",
            ["Feedback_NoPermission"] = "You don't have permission to perform this action.",
            ["Feedback_GenericError"] = "Could not {0}. Please try again.",
            ["Feedback_InvalidRequest"] = "The request was invalid.",
            ["Feedback_SignInRequired"] = "Please sign in to continue.",
            ["Feedback_ItemNotFound"] = "The requested item was not found.",
            ["Feedback_ConflictError"] = "A conflict occurred. Please refresh and try again.",
            ["Feedback_FileTooLarge"] = "The file is too large.",
            ["Feedback_InvalidInput"] = "The input is invalid.",
            ["Feedback_TooManyRequests"] = "Too many requests. Please wait.",
            ["Feedback_ServerError"] = "A server error occurred.",
            ["Feedback_ServiceUnavailable"] = "The service is temporarily unavailable.",
            ["Feedback_GenericApiError"] = "Could not {0}. Please try again.",
            ["Feedback_ConnectionFailed"] = "Could not connect to the server.",
            ["Feedback_SecureConnectionFailed"] = "Could not establish a secure connection.",
            ["Feedback_NetworkError"] = "A network error occurred. Check your connection.",
        };
        _mockLocalizer.Setup(l => l[It.IsAny<string>()])
            .Returns((string key) => new LocalizedString(key, feedbackStrings.GetValueOrDefault(key, key)));
        _mockLocalizer.Setup(l => l[It.IsAny<string>(), It.IsAny<object[]>()])
            .Returns((string key, object[] args) =>
            {
                var template = feedbackStrings.GetValueOrDefault(key, key);
                return new LocalizedString(key, string.Format(template, args));
            });
        _sut = new UserFeedbackService(_mockSnackbar.Object, _mockLogger.Object, _mockLocalizer.Object);
    }

    // ===== ShowSuccess =====

    [Fact]
    public void ShowSuccess_Calls_Snackbar_With_Success_Severity()
    {
        _sut.ShowSuccess("Operation completed");

        _mockSnackbar.Verify(s => s.Add(
            "Operation completed",
            Severity.Success,
            It.IsAny<Action<SnackbarOptions>>(),
            It.IsAny<string>()), Times.Once());
    }

    // ===== ShowInfo =====

    [Fact]
    public void ShowInfo_Calls_Snackbar_With_Info_Severity()
    {
        _sut.ShowInfo("FYI message");

        _mockSnackbar.Verify(s => s.Add(
            "FYI message",
            Severity.Info,
            It.IsAny<Action<SnackbarOptions>>(),
            It.IsAny<string>()), Times.Once());
    }

    // ===== ShowWarning =====

    [Fact]
    public void ShowWarning_Calls_Snackbar_With_Warning_Severity()
    {
        _sut.ShowWarning("Watch out");

        _mockSnackbar.Verify(s => s.Add(
            "Watch out",
            Severity.Warning,
            It.IsAny<Action<SnackbarOptions>>(),
            It.IsAny<string>()), Times.Once());
    }

    // ===== ShowError =====

    [Fact]
    public void ShowError_Calls_Snackbar_With_Error_Severity()
    {
        _sut.ShowError("Something broke");

        _mockSnackbar.Verify(s => s.Add(
            "Something broke",
            Severity.Error,
            It.IsAny<Action<SnackbarOptions>>(),
            It.IsAny<string>()), Times.Once());
    }

    // ===== HandleError =====

    [Fact]
    public void HandleError_Shows_FriendlyMessage_For_GenericException()
    {
        _sut.HandleError(new Exception("Internal details"), "load assets");

        _mockSnackbar.Verify(s => s.Add(
            It.Is<string>(msg => msg.Contains("load assets")),
            Severity.Error,
            It.IsAny<Action<SnackbarOptions>>(),
            It.IsAny<string>()), Times.Once());
    }

    [Fact]
    public void HandleError_Shows_Permission_Message_For_UnauthorizedAccessException()
    {
        _sut.HandleError(new UnauthorizedAccessException(), "delete asset");

        _mockSnackbar.Verify(s => s.Add(
            It.Is<string>(msg => msg.Contains("permission")),
            Severity.Error,
            It.IsAny<Action<SnackbarOptions>>(),
            It.IsAny<string>()), Times.Once());
    }

    [Fact]
    public void HandleError_Shows_Timeout_Message_For_TaskCancelledException()
    {
        _sut.HandleError(new TaskCanceledException(), "upload file");

        _mockSnackbar.Verify(s => s.Add(
            It.Is<string>(msg => msg.Contains("timed out")),
            Severity.Error,
            It.IsAny<Action<SnackbarOptions>>(),
            It.IsAny<string>()), Times.Once());
    }

    // ===== HandleApiError =====

    [Fact]
    public void HandleApiError_Shows_ApiMessage_When_Available()
    {
        var apiEx = new ApiException("User not found", System.Net.HttpStatusCode.NotFound);

        _sut.HandleApiError(apiEx, "find user");

        _mockSnackbar.Verify(s => s.Add(
            "User not found",
            Severity.Error,
            It.IsAny<Action<SnackbarOptions>>(),
            It.IsAny<string>()), Times.Once());
    }

    [Fact]
    public void HandleApiError_Shows_ForbiddenMessage_For_403()
    {
        var apiEx = new ApiException("", System.Net.HttpStatusCode.Forbidden);

        _sut.HandleApiError(apiEx, "create collection");

        _mockSnackbar.Verify(s => s.Add(
            It.Is<string>(msg => msg.Contains("permission")),
            Severity.Error,
            It.IsAny<Action<SnackbarOptions>>(),
            It.IsAny<string>()), Times.Once());
    }

    // ===== ExecuteWithFeedbackAsync (void) =====

    [Fact]
    public async Task ExecuteWithFeedback_ReturnsTrue_OnSuccess()
    {
        var result = await _sut.ExecuteWithFeedbackAsync(
            () => Task.CompletedTask,
            "test operation",
            "Success!");

        Assert.True(result);
        _mockSnackbar.Verify(s => s.Add(
            "Success!",
            Severity.Success,
            It.IsAny<Action<SnackbarOptions>>(),
            It.IsAny<string>()), Times.Once());
    }

    [Fact]
    public async Task ExecuteWithFeedback_ReturnsFalse_OnException()
    {
        var result = await _sut.ExecuteWithFeedbackAsync(
            () => throw new Exception("Oops"),
            "test operation");

        Assert.False(result);
    }

    [Fact]
    public async Task ExecuteWithFeedback_ReturnsFalse_OnApiException()
    {
        var result = await _sut.ExecuteWithFeedbackAsync(
            () => throw new ApiException("Bad request", System.Net.HttpStatusCode.BadRequest),
            "test operation");

        Assert.False(result);
    }

    [Fact]
    public async Task ExecuteWithFeedback_NoSuccessMessage_When_Null()
    {
        var result = await _sut.ExecuteWithFeedbackAsync(
            () => Task.CompletedTask,
            "test operation",
            null);

        Assert.True(result);
        _mockSnackbar.Verify(s => s.Add(
            It.IsAny<string>(),
            Severity.Success,
            It.IsAny<Action<SnackbarOptions>>(),
            It.IsAny<string>()), Times.Never());
    }

    // ===== ExecuteWithFeedbackAsync<T> =====

    [Fact]
    public async Task ExecuteWithFeedback_Generic_ReturnsResult_OnSuccess()
    {
        var (success, result) = await _sut.ExecuteWithFeedbackAsync(
            () => Task.FromResult(42),
            "compute value",
            "Done!");

        Assert.True(success);
        Assert.Equal(42, result);
    }

    [Fact]
    public async Task ExecuteWithFeedback_Generic_ReturnsDefault_OnFailure()
    {
        var (success, result) = await _sut.ExecuteWithFeedbackAsync<string>(
            () => throw new Exception("fail"),
            "compute");

        Assert.False(success);
        Assert.Null(result);
    }
}

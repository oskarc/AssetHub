using Microsoft.Extensions.Logging;

namespace Dam.Ui.Tests.Services;

/// <summary>
/// Tests for the UserFeedbackService.
/// Verifies snackbar message display, error handling, and ExecuteWithFeedback patterns.
/// </summary>
public class UserFeedbackServiceTests
{
    private readonly Mock<ISnackbar> _mockSnackbar;
    private readonly Mock<ILogger<UserFeedbackService>> _mockLogger;
    private readonly UserFeedbackService _sut;

    public UserFeedbackServiceTests()
    {
        _mockSnackbar = new Mock<ISnackbar>();
        _mockLogger = new Mock<ILogger<UserFeedbackService>>();
        _sut = new UserFeedbackService(_mockSnackbar.Object, _mockLogger.Object);
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

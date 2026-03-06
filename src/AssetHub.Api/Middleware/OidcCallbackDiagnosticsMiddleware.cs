using Serilog;

namespace AssetHub.Api.Middleware;

/// <summary>
/// Development-only middleware that logs OIDC callback details for diagnosing
/// missing state/code and content-type mismatches on /signin-oidc.
/// </summary>
internal sealed class OidcCallbackDiagnosticsMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path != "/signin-oidc")
        {
            await next(context);
            return;
        }

        var logger = context.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("OidcCallback");

        await LogCallbackDetailsAsync(context, logger);
        await InvokeWithExceptionGuardAsync(context, logger);
    }

    private static async Task LogCallbackDetailsAsync(HttpContext context, Microsoft.Extensions.Logging.ILogger logger)
    {
        logger.LogInformation(
            "OIDC callback received: {Method} {Path}{Query}. ContentType={ContentType}",
            context.Request.Method, context.Request.Path,
            context.Request.QueryString, context.Request.ContentType ?? "<null>");

        try
        {
            context.Request.EnableBuffering();

            if (context.Request.HasFormContentType)
            {
                var form = await context.Request.ReadFormAsync(context.RequestAborted);
                logger.LogInformation("OIDC callback form keys: {Keys}",
                    string.Join(", ", form.Keys.OrderBy(k => k, StringComparer.Ordinal)));
                logger.LogInformation(
                    "OIDC callback has state={HasState}, code={HasCode}",
                    form.TryGetValue("state", out var state) &&
                        !string.IsNullOrWhiteSpace(state.ToString()),
                    form.TryGetValue("code", out var code) &&
                        !string.IsNullOrWhiteSpace(code.ToString()));
            }
            else
            {
                logger.LogInformation("OIDC callback query keys: {Keys}",
                    string.Join(", ", context.Request.Query.Keys
                        .OrderBy(k => k, StringComparer.Ordinal)));
                logger.LogInformation(
                    "OIDC callback has state={HasState}, code={HasCode}",
                    context.Request.Query.ContainsKey("state") &&
                        !string.IsNullOrWhiteSpace(context.Request.Query["state"].ToString()),
                    context.Request.Query.ContainsKey("code") &&
                        !string.IsNullOrWhiteSpace(context.Request.Query["code"].ToString()));
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to inspect OIDC callback request");
        }
        finally
        {
            if (context.Request.Body.CanSeek)
                context.Request.Body.Position = 0;
        }
    }

    private async Task InvokeWithExceptionGuardAsync(HttpContext context, Microsoft.Extensions.Logging.ILogger logger)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            if (!context.Response.HasStarted)
            {
                logger.LogError(ex,
                    "Unhandled exception during /signin-oidc pipeline: {Message}", ex.Message);
                context.Response.Redirect("/?authError=signin_oidc_exception");
                return;
            }
            throw;
        }
    }
}

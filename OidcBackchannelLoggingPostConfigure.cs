using System.Net.Http;
using System.Text;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AssetHub;

public sealed class OidcBackchannelLoggingPostConfigure : IPostConfigureOptions<OpenIdConnectOptions>
{
    private readonly IHostEnvironment _environment;
    private readonly ILogger<OidcBackchannelLoggingPostConfigure> _logger;

    public OidcBackchannelLoggingPostConfigure(
        IHostEnvironment environment,
        ILogger<OidcBackchannelLoggingPostConfigure> logger)
    {
        _environment = environment;
        _logger = logger;
    }

    public void PostConfigure(string? name, OpenIdConnectOptions options)
    {
        if (!_environment.IsDevelopment())
            return;

        if (!string.Equals(name, OpenIdConnectDefaults.AuthenticationScheme, StringComparison.Ordinal))
            return;

        var innerHandler = options.BackchannelHttpHandler ?? new HttpClientHandler();
        var loggingHandler = new UserInfoLoggingHandler(innerHandler, _logger);

        options.BackchannelHttpHandler = loggingHandler;
        options.Backchannel = new HttpClient(loggingHandler)
        {
            Timeout = options.BackchannelTimeout
        };
    }

    private sealed class UserInfoLoggingHandler : DelegatingHandler
    {
        private readonly ILogger _logger;

        public UserInfoLoggingHandler(HttpMessageHandler innerHandler, ILogger logger)
            : base(innerHandler)
        {
            _logger = logger;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

            var isUserInfo = request.RequestUri?.AbsolutePath.EndsWith("/userinfo", StringComparison.OrdinalIgnoreCase) == true;
            if (!isUserInfo)
                return response;

            if (response.IsSuccessStatusCode)
                return response;

            var authScheme = request.Headers.Authorization?.Scheme;
            var authParamLen = request.Headers.Authorization?.Parameter?.Length ?? 0;

            var wwwAuthenticate = response.Headers.WwwAuthenticate.Count > 0
                ? string.Join(", ", response.Headers.WwwAuthenticate)
                : null;

            string? body = null;
            if (response.Content is not null)
            {
                body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

                // Preserve content so the OIDC handler can still read it.
                var mediaType = response.Content.Headers.ContentType?.MediaType ?? "application/json";
                response.Content = new StringContent(body ?? string.Empty, Encoding.UTF8, mediaType);
            }

            if (body is { Length: > 4000 })
                body = body[..4000] + "…(truncated)";

            _logger.LogError(
                "Keycloak userinfo failed: {StatusCode} {ReasonPhrase}. Endpoint={Endpoint}. Authorization={AuthScheme}({AuthLen}). WWW-Authenticate={WwwAuthenticate}. Body={Body}",
                (int)response.StatusCode,
                response.ReasonPhrase,
                request.RequestUri?.ToString(),
                authScheme,
                authParamLen,
                wwwAuthenticate,
                body);

            return response;
        }
    }
}

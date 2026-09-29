using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

namespace AssetHub.Tests.Fixtures;

/// <summary>
/// Adds <c>GET /__test/throw</c>, a route that always throws, to the test host only.
/// </summary>
/// <remarks>
/// Nothing in production code throws on demand, and the dev stack runs in
/// Development, where the developer exception page replaces the /Error page. The
/// test host runs in "Testing" (non-Development), so it is the one place the
/// production 500 path can be judged. A startup filter appends the route AFTER the
/// app's own pipeline, so the exception travels back out through the app's real
/// exception handling.
/// </remarks>
internal sealed class ThrowRouteStartupFilter : IStartupFilter
{
    public const string Path = "/__test/throw";
    public const string ExceptionMessage = "Deliberate test exception - must never reach the response";

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        next(app);
        app.Map(Path, branch => branch.Run(_ => throw new InvalidOperationException(ExceptionMessage)));
    };
}

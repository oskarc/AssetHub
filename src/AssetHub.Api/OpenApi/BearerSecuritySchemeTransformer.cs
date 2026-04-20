using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi.Models;

namespace AssetHub.Api.OpenApi;

/// <summary>
/// Advertises the two accepted bearer formats (Personal Access Token and OIDC JWT)
/// under a single <c>Bearer</c> security scheme and applies it as a document-wide
/// requirement. The API itself distinguishes the two at runtime via the smart
/// authentication scheme selector — OpenAPI consumers only need to know that both
/// are sent in the <c>Authorization</c> header as <c>Bearer &lt;token&gt;</c>.
/// </summary>
internal sealed class BearerSecuritySchemeTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, OpenApiSecurityScheme>();

        document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "Personal Access Token (pat_*) or OIDC JWT",
            Description =
                "Send either a Personal Access Token (starting with `pat_`) or a Keycloak-issued " +
                "JWT as `Authorization: Bearer <token>`. Mint PATs from your account page at `/account`."
        };

        document.SecurityRequirements ??= new List<OpenApiSecurityRequirement>();
        document.SecurityRequirements.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            }] = Array.Empty<string>()
        });

        return Task.CompletedTask;
    }
}

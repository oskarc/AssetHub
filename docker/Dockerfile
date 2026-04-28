FROM mcr.microsoft.com/dotnet/sdk:9.0.312-bookworm-slim AS build
WORKDIR /src

# Copy Directory.Build.props first (affects restore/build behavior)
COPY ["Directory.Build.props", "."]

# Copy project files
COPY ["src/AssetHub.Api/AssetHub.Api.csproj", "src/AssetHub.Api/"]
COPY ["src/AssetHub.Domain/AssetHub.Domain.csproj", "src/AssetHub.Domain/"]
COPY ["src/AssetHub.Application/AssetHub.Application.csproj", "src/AssetHub.Application/"]
COPY ["src/AssetHub.Infrastructure/AssetHub.Infrastructure.csproj", "src/AssetHub.Infrastructure/"]
COPY ["src/AssetHub.Ui/AssetHub.Ui.csproj", "src/AssetHub.Ui/"]

# Restore dependencies
RUN dotnet restore "src/AssetHub.Api/AssetHub.Api.csproj"

# Copy source code (explicit path to avoid copying sensitive files - S6470)
COPY src/ src/

# Build
RUN dotnet build "src/AssetHub.Api/AssetHub.Api.csproj" -c Release -o /app/build

# Publish
FROM build AS publish
RUN dotnet publish "src/AssetHub.Api/AssetHub.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Runtime
FROM mcr.microsoft.com/dotnet/aspnet:9.0.14-bookworm-slim AS final

# Install ImageMagick for image processing (thumbnails, resizing)
# Install ffmpeg for video processing (poster frames)
# Install curl for Docker health checks
# Install ca-certificates for SSL trust
#
# Patch known HIGH CVEs in Debian base packages until the upstream image is rebuilt:
# - CVE-2026-28390: OpenSSL NULL-ptr DoS in CMS (fixed in 3.0.19-1~deb12u2)
RUN apt-get update && apt-get install -y --no-install-recommends \
    imagemagick \
    ffmpeg \
    curl \
    ca-certificates \
    && apt-get install -y --only-upgrade libssl3 openssl \
    && rm -rf /var/lib/apt/lists/*

# Apply restrictive ImageMagick policy to disable dangerous coders (SVG, MVG, etc.)
# This mitigates ImageTragick and similar vulnerabilities
COPY docker/imagemagick-policy.xml /etc/ImageMagick-6/policy.xml

# Optional: install the local dev cert into the OS trust store at build time.
# Used by the "production compose locally" workflow where Keycloak presents a
# self-signed cert that the API container needs to validate during OIDC
# discovery. Real production builds leave TRUST_LOCAL_DEV_CERT unset (default
# 0); the system CA bundle is then the sole trust source.
#
# Selective: only certs/dev-cert.crt is in the build context (.dockerignore
# allows that single file). Private keys are never present.
ARG TRUST_LOCAL_DEV_CERT=0
COPY certs/ /tmp/dev-certs/
RUN if [ "$TRUST_LOCAL_DEV_CERT" = "1" ]; then \
        if [ ! -f /tmp/dev-certs/dev-cert.crt ]; then \
            echo "ERROR: TRUST_LOCAL_DEV_CERT=1 but certs/dev-cert.crt is missing from build context" >&2; \
            exit 1; \
        fi; \
        cp /tmp/dev-certs/dev-cert.crt /usr/local/share/ca-certificates/assethub-dev.crt && \
        update-ca-certificates; \
    fi && \
    rm -rf /tmp/dev-certs

WORKDIR /app
COPY --from=publish /app/publish .

# Run as non-root user (app user is created by the aspnet base image)
USER app

EXPOSE 7252
ENTRYPOINT ["dotnet", "AssetHub.Api.dll"]

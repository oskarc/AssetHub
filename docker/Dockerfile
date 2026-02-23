FROM mcr.microsoft.com/dotnet/sdk:9.0.102-bookworm-slim AS build
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

# Copy source code
COPY . .

# Build
RUN dotnet build "src/AssetHub.Api/AssetHub.Api.csproj" -c Release -o /app/build

# Publish
FROM build AS publish
RUN dotnet publish "src/AssetHub.Api/AssetHub.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Runtime
FROM mcr.microsoft.com/dotnet/aspnet:9.0.2-bookworm-slim AS final

# Install ImageMagick for image processing (thumbnails, resizing)
# Install ffmpeg for video processing (poster frames)
# Install curl for Docker health checks
# Install ca-certificates for SSL trust
RUN apt-get update && apt-get install -y --no-install-recommends \
    imagemagick \
    ffmpeg \
    curl \
    ca-certificates \
    && rm -rf /var/lib/apt/lists/*

# Apply restrictive ImageMagick policy to disable dangerous coders (SVG, MVG, etc.)
# This mitigates ImageTragick and similar vulnerabilities
COPY docker/imagemagick-policy.xml /etc/ImageMagick-6/policy.xml

# Custom CA certificates can be mounted at runtime via docker-compose volumes:
#   - ./certs/custom-ca.crt:/usr/local/share/ca-certificates/custom-ca.crt:ro
# Then run update-ca-certificates in an entrypoint script if needed.

WORKDIR /app
COPY --from=publish /app/publish .

# Run as non-root user (app user is created by the aspnet base image)
USER app

EXPOSE 7252
ENTRYPOINT ["dotnet", "AssetHub.Api.dll"]

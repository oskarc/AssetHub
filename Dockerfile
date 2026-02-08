FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy project files
COPY ["AssetHub.csproj", "./"]
COPY ["src/Dam.Domain/Dam.Domain.csproj", "src/Dam.Domain/"]
COPY ["src/Dam.Application/Dam.Application.csproj", "src/Dam.Application/"]
COPY ["src/Dam.Infrastructure/Dam.Infrastructure.csproj", "src/Dam.Infrastructure/"]
COPY ["src/Dam.Ui/Dam.Ui.csproj", "src/Dam.Ui/"]

# Restore dependencies
RUN dotnet restore "AssetHub.csproj"

# Copy source code
COPY . .

# Build
RUN dotnet build "AssetHub.csproj" -c Release -o /app/build

# Publish
FROM build AS publish
RUN dotnet publish "AssetHub.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Runtime
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final

# Install ImageMagick for image processing (thumbnails, resizing)
# Install curl for Docker health checks
RUN apt-get update && apt-get install -y --no-install-recommends \
    imagemagick \
    curl \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=publish /app/publish .

EXPOSE 7252
ENTRYPOINT ["dotnet", "AssetHub.dll"]

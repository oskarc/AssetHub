# Contributing to AssetHub

Thank you for your interest in contributing to AssetHub! This guide will help you get started.

## Table of Contents

- [Getting Started](#getting-started)
- [Development Environment](#development-environment)
- [Project Structure](#project-structure)
- [Architecture](#architecture)
- [Building & Running](#building--running)
- [Testing](#testing)
- [Submitting Changes](#submitting-changes)
- [Code Style](#code-style)
- [Reporting Issues](#reporting-issues)

## Getting Started

1. Fork the repository
2. Clone your fork:
   ```bash
   git clone https://github.com/oskarc/AssetHub.git
   ```
3. Create a feature branch from `main`:
   ```bash
   git checkout -b feature/your-feature-name
   ```

## Development Environment

### Prerequisites

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- [Docker & Docker Compose](https://docs.docker.com/get-docker/)
- [Node.js](https://nodejs.org/) (for E2E tests)
- Git

### Setting Up

1. Copy the environment template and configure it:
   ```bash
   cp .env.template .env
   ```
2. Start the infrastructure services:
   ```bash
   docker compose -f docker/docker-compose.yml up -d
   ```
3. Restore dependencies and build:
   ```bash
   dotnet restore
   dotnet build
   ```
4. Run the API:
   ```bash
   dotnet run --project src/AssetHub.Api
   ```

See [CREDENTIALS.md](CREDENTIALS.md) for default service passwords and [DEPLOYMENT.md](DEPLOYMENT.md) for full deployment details.

## Project Structure

```
src/
├── AssetHub.Domain/           # Entities and domain logic (zero dependencies)
├── AssetHub.Application/      # Interfaces, DTOs, configuration, services
├── AssetHub.Infrastructure/   # EF Core, repositories, service implementations
├── AssetHub.Api/              # ASP.NET Core host, minimal APIs, Blazor UI host
├── AssetHub.Ui/               # Blazor Server components (Razor Class Library)
└── AssetHub.Worker/           # Hangfire background job processor

tests/
├── AssetHub.Tests/            # Unit and integration tests (xUnit)
├── AssetHub.Ui.Tests/         # Blazor component tests (bUnit)
└── E2E/                       # Playwright end-to-end tests (TypeScript)
```

## Architecture

AssetHub follows **Clean Architecture** with strict dependency flow:

```
Domain → Application → Infrastructure → Api / Worker
                                      → Ui
```

- **Domain** has no external dependencies — pure entities and logic.
- **Application** defines interfaces and DTOs consumed by outer layers.
- **Infrastructure** implements persistence, storage, and external services.
- **Api** and **Worker** are composition roots that wire everything together.
- **Ui** is a Razor Class Library that depends only on Application.

When adding new features, respect these layer boundaries.

## Building & Running

```bash
# Restore packages
dotnet restore

# Build
dotnet build --configuration Release

# Run the API
dotnet run --project src/AssetHub.Api

# Run the Worker (separate terminal)
dotnet run --project src/AssetHub.Worker
```

### Docker

```bash
# Development stack
docker compose -f docker/docker-compose.yml up -d

# Production stack
docker compose -f docker/docker-compose.prod.yml up -d
```

## Testing

### Unit & Integration Tests (C#)

```bash
dotnet test --configuration Release
```

Tests use xUnit with Moq for mocking and Testcontainers for PostgreSQL integration tests. A running Docker daemon is required for integration tests.

### Component Tests (Blazor)

Component tests live in `tests/AssetHub.Ui.Tests/` and use bUnit.

### End-to-End Tests (Playwright)

```bash
cd tests/E2E
npm install
npx playwright install chromium
npx playwright test
```

Additional E2E modes:

```bash
npm run test:headed   # Run with visible browser
npm run test:ui       # Playwright UI mode
```

### Writing Tests

- Place unit/integration tests in `tests/AssetHub.Tests/` mirroring the source structure.
- Place Blazor component tests in `tests/AssetHub.Ui.Tests/`.
- Place E2E tests in `tests/E2E/tests/specs/`.
- All new features should include appropriate test coverage.

## Submitting Changes

1. Ensure your code builds without warnings:
   ```bash
   dotnet build --configuration Release
   ```
2. Run the test suite and confirm all tests pass:
   ```bash
   dotnet test --configuration Release
   ```
3. Commit your changes with a clear, descriptive message.
4. Push your branch and open a Pull Request against `main`.
5. Fill in the PR description with a summary of changes and any relevant context.

### Pull Request Guidelines

- Keep PRs focused — one feature or fix per PR.
- Include tests for new functionality.
- Update documentation if your change affects user-facing behavior.
- Ensure CI checks pass before requesting review.

## Code Style

### C#

- Use **PascalCase** for classes, methods, and properties.
- Use **camelCase** for local variables and parameters.
- Enable nullable reference types (`#nullable enable` is set globally).
- Follow the existing patterns in the codebase for consistency.
- Keep domain logic in the Domain layer; keep infrastructure concerns out of Application and Domain.

### TypeScript (E2E)

- Follow the existing Page Object pattern in `tests/E2E/tests/pages/`.
- Use helper classes in `tests/E2E/tests/helpers/` for reusable test utilities.

## Reporting Issues

- Use [GitHub Issues](https://github.com/oskarc/AssetHub/issues) to report bugs or request features.
- Include steps to reproduce, expected behavior, and actual behavior.
- Attach logs or screenshots when applicable.
- Check existing issues before creating a new one.

## License

By contributing, you agree that your contributions will be licensed under the same license as the project.

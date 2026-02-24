# NuGet Proxy

[![Build and Publish](https://github.com/robsonfelix/nuget-proxy/actions/workflows/docker-publish.yml/badge.svg)](https://github.com/robsonfelix/nuget-proxy/actions/workflows/docker-publish.yml)
[![Docker Image](https://img.shields.io/docker/v/robsonfelix/nuget-proxy?sort=semver&label=Docker%20Hub)](https://hub.docker.com/r/robsonfelix/nuget-proxy)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

A lightweight, read-only NuGet V3 proxy that unifies a self-hosted GitLab package registry and one or more upstream NuGet feeds behind a single source URL. Point `dotnet restore` at the proxy, and it transparently routes each package to the right place.

## How it works

```
dotnet restore
      |
      v
 NuGet Proxy ──> Is the package on GitLab?
      |                 |
      |     yes         |  no
      |  ┌──────┐       |
      v  v      |       v
   GitLab    try upstream feeds in order
   registry    (nuget.org, Azure Artifacts, ...)
```

On startup, the proxy:

1. **Discovers all GitLab groups** and indexes every NuGet package name across them (refreshed periodically).
2. **Resolves upstream feed endpoints** by fetching each feed's NuGet V3 service index.

When a request arrives:

- If the package is in the GitLab index, the request is forwarded to GitLab.
- Otherwise, upstream feeds are tried in order until one returns a successful response.
- JSON responses have their URLs rewritten so the NuGet client always talks through the proxy.
- Binary downloads (`.nupkg`) are streamed through without buffering.

## Quick start

### Docker Hub (easiest)

```bash
docker run -d -p 5000:8080 \
  -e NuGetProxy__BaseUrl=http://localhost:5000 \
  -e NuGetProxy__GitLab__Url=https://gitlab.example.com \
  -e NuGetProxy__GitLab__ServiceToken=glpat-xxxxxxxxxxxxxxxxxxxx \
  robsonfelix/nuget-proxy
```

The proxy is now available at `http://localhost:5000/v3/index.json`.

### Docker Compose

Create a `.env` file next to `docker-compose.yml`:

```env
GITLAB_URL=https://gitlab.example.com
GITLAB_SERVICE_TOKEN=glpat-xxxxxxxxxxxxxxxxxxxx
```

Then:

```bash
docker compose up -d
```

### Build from source

```bash
cd src/NuGetProxy
dotnet run
```

Pass configuration via environment variables (see below).

## Configuration

All settings live under the `NuGetProxy` section and can be set via `appsettings.json`, environment variables, or any .NET configuration provider.

| Environment variable | Default | Description |
|---|---|---|
| `NuGetProxy__BaseUrl` | `http://localhost:5000` | Public URL of the proxy (used for URL rewriting) |
| `NuGetProxy__GitLab__Url` | _(empty)_ | GitLab instance URL. Leave empty to disable GitLab routing. |
| `NuGetProxy__GitLab__ServiceToken` | _(empty)_ | Personal access token with `api` scope |
| `NuGetProxy__GitLab__GroupIds__0` | _(auto-discover)_ | Specific group IDs to scan. If omitted, all accessible groups are discovered automatically. |
| `NuGetProxy__GitLab__RefreshIntervalSeconds` | `300` | How often to re-scan GitLab for new packages |
| `NuGetProxy__UpstreamFeeds__0__Name` | `nuget.org` | Display name for the feed |
| `NuGetProxy__UpstreamFeeds__0__ServiceIndexUrl` | `https://api.nuget.org/v3/index.json` | NuGet V3 service index URL |
| `NuGetProxy__UpstreamFeeds__0__ForwardAuth` | `false` | Forward client `Authorization` header to this feed |

Add more upstream feeds by incrementing the index (`__1__`, `__2__`, etc.). Feeds are tried in order.

### GitLab token requirements

The service token needs the **`api`** scope. On GitLab CE, the `read_package_registry` scope does not exist, so `api` is required for NuGet registry access. The proxy auto-detects the token's username via the GitLab API.

## Client setup

Add the proxy as your sole NuGet source in a `nuget.config`:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="proxy" value="http://localhost:5000/v3/index.json" protocolVersion="3" />
  </packageSources>
</configuration>
```

If your GitLab packages require authentication, add credentials:

```xml
<packageSourceCredentials>
  <proxy>
    <add key="Username" value="your-gitlab-username" />
    <add key="ClearTextPassword" value="your-gitlab-token" />
  </proxy>
</packageSourceCredentials>
```

Then `dotnet restore` just works -- private packages resolve from GitLab, everything else from the upstream feeds.

## API endpoints

| Endpoint | Description |
|---|---|
| `GET /v3/index.json` | NuGet V3 service index |
| `GET /v3/package/{id}/index.json` | List all versions of a package |
| `GET /v3/package/{id}/{version}/{file}` | Download `.nupkg` / `.nuspec` |
| `GET /v3/registration/{id}/index.json` | Package registration (metadata) |
| `GET /v3/registration/{id}/{version}.json` | Registration leaf |
| `GET /v3/search?q={query}` | Search packages |
| `GET /v3/autocomplete?q={query}` | Autocomplete |
| `GET /health` | Health check |

## Running tests

```bash
dotnet test tests/NuGetProxy.Tests/
```

Tests hit the real nuget.org API. GitLab-specific tests run automatically when `GITLAB_URL` and `GITLAB_SERVICE_TOKEN` are set, and are skipped otherwise.

## Project structure

```
src/NuGetProxy/
  Program.cs                           Entry point, DI, endpoint mapping
  Configuration/ProxyOptions.cs        Strongly-typed configuration
  Services/
    GitLabPackageIndex.cs              Background service: indexes GitLab packages
    GitLabAuthHandler.cs               HTTP handler: auto-detects username, Basic auth
    UpstreamFeedRegistry.cs            Resolves upstream feed endpoints on startup
    UrlRewriter.cs                     Rewrites URLs in proxied JSON responses
  Endpoints/
    ServiceIndexEndpoint.cs            /v3/index.json
    PackageBaseAddressEndpoint.cs       /v3/package/...
    RegistrationEndpoint.cs            /v3/registration/...
    SearchEndpoint.cs                  /v3/search, /v3/autocomplete

tests/NuGetProxy.Tests/               xunit v3 integration tests
```

## Docker image tags

Images are published to [Docker Hub](https://hub.docker.com/r/robsonfelix/nuget-proxy) automatically via GitHub Actions.

| Tag | Description |
|---|---|
| `latest` | Latest build from `master` |
| `1.0.0` | Exact release version |
| `1.0` | Latest patch in the 1.0.x line |
| `1` | Latest minor/patch in the 1.x line |
| `a1b2c3d` | Specific commit (short SHA) |

### Creating a release

Tag a commit and push:

```bash
git tag v1.0.0
git push origin v1.0.0
```

This triggers the CI pipeline to:

1. Build the Docker image
2. Push it to Docker Hub with all matching tags (`1.0.0`, `1.0`, `1`, `latest`)
3. Create a GitHub Release with auto-generated release notes

## CI/CD

The GitHub Actions workflow (`.github/workflows/docker-publish.yml`) runs on:

- **Push to `master`** -- builds and pushes `latest` + commit SHA tag
- **Version tags (`v*`)** -- builds and pushes semver tags + creates a GitHub Release
- **Pull requests** -- builds only (no push), to validate the Dockerfile

### Required secrets

Set these in your GitHub repo under **Settings > Secrets and variables > Actions**:

| Secret | Description |
|---|---|
| `DOCKERHUB_USERNAME` | Docker Hub username (`robsonfelix`) |
| `DOCKERHUB_TOKEN` | Docker Hub [access token](https://hub.docker.com/settings/security) |

## Built with

- .NET 10 / ASP.NET Core Minimal API
- xunit v3 + `Microsoft.AspNetCore.Mvc.Testing`
- Docker

## License

[MIT](LICENSE)

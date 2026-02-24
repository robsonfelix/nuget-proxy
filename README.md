# NuGet Proxy

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

### Docker Compose (recommended)

Create a `.env` file next to `docker-compose.yml`:

```env
GITLAB_URL=https://gitlab.example.com
GITLAB_SERVICE_TOKEN=glpat-xxxxxxxxxxxxxxxxxxxx
```

Then:

```bash
docker compose up -d
```

The proxy is now available at `http://localhost:5000/v3/index.json`.

### Standalone

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

## Built with

- .NET 10 / ASP.NET Core Minimal API
- xunit v3 + `Microsoft.AspNetCore.Mvc.Testing`
- Docker

## License

[MIT](LICENSE)

namespace NuGetProxy.Endpoints;

using System.Text.Json;
using Microsoft.Extensions.Options;
using NuGetProxy.Configuration;
using NuGetProxy.Services;

public static class PackageBaseAddressEndpoint
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/v3/package/{id}/index.json", HandleVersionListAsync);
        app.MapGet("/v3/package/{id}/{version}/{filename}", HandleDownloadAsync);
    }

    private static async Task HandleVersionListAsync(
        string id,
        HttpContext ctx,
        GitLabPackageIndex gitLabIndex,
        UpstreamFeedRegistry feedRegistry,
        IHttpClientFactory httpClientFactory,
        IOptions<ProxyOptions> options)
    {
        var lowerId = id.ToLowerInvariant();

        if (gitLabIndex.TryGetGroup(id, out var groupId))
        {
            // GitLab has no flat container — build version list from registration metadata
            var gitlabUrl = options.Value.GitLab.Url.TrimEnd('/');
            var url = $"{gitlabUrl}/api/v4/groups/{groupId}/-/packages/nuget/metadata/{id}/index.json";
            var client = httpClientFactory.CreateClient("gitlab");
            var response = await client.GetAsync(url);

            if (!response.IsSuccessStatusCode)
            {
                ctx.Response.StatusCode = (int)response.StatusCode;
                if (response.Content.Headers.ContentType != null)
                    ctx.Response.ContentType = response.Content.Headers.ContentType.ToString();
                await response.Content.CopyToAsync(ctx.Response.Body);
                return;
            }

            var json = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(json);
            var versions = new List<string>();

            // Extract versions from the registration pages/items
            if (doc.RootElement.TryGetProperty("items", out var pages))
            {
                foreach (var page in pages.EnumerateArray())
                {
                    if (page.TryGetProperty("items", out var items))
                    {
                        foreach (var item in items.EnumerateArray())
                        {
                            if (item.TryGetProperty("catalogEntry", out var entry) &&
                                entry.TryGetProperty("version", out var ver))
                            {
                                versions.Add(ver.GetString()!);
                            }
                        }
                    }
                    else
                    {
                        // Some responses have lower/upper directly on the page
                        if (page.TryGetProperty("lower", out var lower))
                            versions.Add(lower.GetString()!);
                    }
                }
            }

            ctx.Response.ContentType = "application/json";
            await ctx.Response.WriteAsJsonAsync(new { versions });
            return;
        }

        foreach (var feed in feedRegistry.Feeds)
        {
            var url = $"{feed.PackageBaseAddress}/{lowerId}/index.json";
            var success = await ProxyTo(ctx, url, httpClientFactory, "upstream",
                forwardAuth: feed.ForwardAuth, continueOn404: true);
            if (success) return;
        }

        ctx.Response.StatusCode = 404;
    }

    private static async Task HandleDownloadAsync(
        string id, string version, string filename,
        HttpContext ctx,
        GitLabPackageIndex gitLabIndex,
        UpstreamFeedRegistry feedRegistry,
        IHttpClientFactory httpClientFactory,
        IOptions<ProxyOptions> options)
    {
        var lowerId = id.ToLowerInvariant();
        var lowerVersion = version.ToLowerInvariant();

        if (gitLabIndex.TryGetGroup(id, out var groupId))
        {
            // GitLab download URLs are project-level, but we try group-level first
            var gitlabUrl = options.Value.GitLab.Url.TrimEnd('/');
            var url = $"{gitlabUrl}/api/v4/groups/{groupId}/-/packages/nuget/download/{id}/{version}/{filename}";
            await ProxyTo(ctx, url, httpClientFactory, "gitlab", forwardAuth: false);
            return;
        }

        foreach (var feed in feedRegistry.Feeds)
        {
            var url = $"{feed.PackageBaseAddress}/{lowerId}/{lowerVersion}/{filename}";
            var success = await ProxyTo(ctx, url, httpClientFactory, "upstream",
                forwardAuth: feed.ForwardAuth, continueOn404: true);
            if (success) return;
        }

        ctx.Response.StatusCode = 404;
    }

    private static async Task<bool> ProxyTo(
        HttpContext ctx, string url, IHttpClientFactory httpClientFactory,
        string clientName, bool forwardAuth, bool continueOn404 = false)
    {
        var client = httpClientFactory.CreateClient(clientName);
        using var request = new HttpRequestMessage(HttpMethod.Get, url);

        if (forwardAuth && ctx.Request.Headers.TryGetValue("Authorization", out var authHeader))
            request.Headers.TryAddWithoutValidation("Authorization", authHeader.ToString());

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound && continueOn404)
            return false;

        ctx.Response.StatusCode = (int)response.StatusCode;
        if (response.Content.Headers.ContentType != null)
            ctx.Response.ContentType = response.Content.Headers.ContentType.ToString();

        await response.Content.CopyToAsync(ctx.Response.Body);
        return true;
    }
}

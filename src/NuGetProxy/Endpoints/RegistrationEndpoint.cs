namespace NuGetProxy.Endpoints;

using Microsoft.Extensions.Options;
using NuGetProxy.Configuration;
using NuGetProxy.Services;

public static class RegistrationEndpoint
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/v3/registration/{id}/index.json", HandleRegistrationIndexAsync);
        app.MapGet("/v3/registration/{id}/{version}.json", HandleRegistrationLeafAsync);
    }

    private static async Task HandleRegistrationIndexAsync(
        string id,
        HttpContext ctx,
        GitLabPackageIndex gitLabIndex,
        UpstreamFeedRegistry feedRegistry,
        UrlRewriter urlRewriter,
        IHttpClientFactory httpClientFactory,
        IOptions<ProxyOptions> options)
    {
        var lowerId = id.ToLowerInvariant();

        if (gitLabIndex.TryGetGroup(id, out var groupId))
        {
            var gitlabUrl = options.Value.GitLab.Url.TrimEnd('/');
            var url = $"{gitlabUrl}/api/v4/groups/{groupId}/-/packages/nuget/metadata/{id}/index";
            await ProxyWithRewrite(ctx, url, httpClientFactory, "gitlab",
                json => urlRewriter.RewriteGitLab(json, groupId), forwardAuth: false);
            return;
        }

        foreach (var feed in feedRegistry.Feeds)
        {
            var url = $"{feed.RegistrationsBaseUrl}/{lowerId}/index.json";
            var success = await ProxyWithRewrite(ctx, url, httpClientFactory, "upstream",
                json => urlRewriter.Rewrite(json, feed), forwardAuth: feed.ForwardAuth, continueOn404: true);
            if (success) return;
        }

        ctx.Response.StatusCode = 404;
    }

    private static async Task HandleRegistrationLeafAsync(
        string id, string version,
        HttpContext ctx,
        GitLabPackageIndex gitLabIndex,
        UpstreamFeedRegistry feedRegistry,
        UrlRewriter urlRewriter,
        IHttpClientFactory httpClientFactory,
        IOptions<ProxyOptions> options)
    {
        var lowerId = id.ToLowerInvariant();
        var lowerVersion = version.ToLowerInvariant();

        if (gitLabIndex.TryGetGroup(id, out var groupId))
        {
            var gitlabUrl = options.Value.GitLab.Url.TrimEnd('/');
            var url = $"{gitlabUrl}/api/v4/groups/{groupId}/-/packages/nuget/metadata/{id}/{version}";
            await ProxyWithRewrite(ctx, url, httpClientFactory, "gitlab",
                json => urlRewriter.RewriteGitLab(json, groupId), forwardAuth: false);
            return;
        }

        foreach (var feed in feedRegistry.Feeds)
        {
            var url = $"{feed.RegistrationsBaseUrl}/{lowerId}/{lowerVersion}.json";
            var success = await ProxyWithRewrite(ctx, url, httpClientFactory, "upstream",
                json => urlRewriter.Rewrite(json, feed), forwardAuth: feed.ForwardAuth, continueOn404: true);
            if (success) return;
        }

        ctx.Response.StatusCode = 404;
    }

    private static async Task<bool> ProxyWithRewrite(
        HttpContext ctx, string url, IHttpClientFactory httpClientFactory,
        string clientName, Func<string, string> rewrite, bool forwardAuth, bool continueOn404 = false)
    {
        var client = httpClientFactory.CreateClient(clientName);
        using var request = new HttpRequestMessage(HttpMethod.Get, url);

        if (forwardAuth && ctx.Request.Headers.TryGetValue("Authorization", out var authHeader))
            request.Headers.TryAddWithoutValidation("Authorization", authHeader.ToString());

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound && continueOn404)
            return false;

        ctx.Response.StatusCode = (int)response.StatusCode;

        var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
        if (response.IsSuccessStatusCode && contentType.Contains("json"))
        {
            var json = await response.Content.ReadAsStringAsync();
            var rewritten = rewrite(json);
            ctx.Response.ContentType = "application/json";
            await ctx.Response.WriteAsync(rewritten);
        }
        else
        {
            if (response.Content.Headers.ContentType != null)
                ctx.Response.ContentType = response.Content.Headers.ContentType.ToString();
            await response.Content.CopyToAsync(ctx.Response.Body);
        }

        return true;
    }
}

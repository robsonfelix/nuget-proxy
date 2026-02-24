namespace NuGetProxy.Endpoints;

using NuGetProxy.Services;

public static class SearchEndpoint
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/v3/search", HandleSearchAsync);
        app.MapGet("/v3/autocomplete", HandleAutocompleteAsync);
    }

    private static async Task HandleSearchAsync(
        HttpContext ctx,
        UpstreamFeedRegistry feedRegistry,
        UrlRewriter urlRewriter,
        IHttpClientFactory httpClientFactory)
    {
        // Forward search to the first upstream feed that has a search service
        foreach (var feed in feedRegistry.Feeds)
        {
            if (string.IsNullOrEmpty(feed.SearchQueryService))
                continue;

            var url = $"{feed.SearchQueryService}{ctx.Request.QueryString}";
            var success = await ProxyWithRewrite(ctx, url, httpClientFactory, "upstream",
                json => urlRewriter.Rewrite(json, feed), forwardAuth: feed.ForwardAuth);
            if (success) return;
        }

        ctx.Response.StatusCode = 404;
    }

    private static async Task HandleAutocompleteAsync(
        HttpContext ctx,
        UpstreamFeedRegistry feedRegistry,
        UrlRewriter urlRewriter,
        IHttpClientFactory httpClientFactory)
    {
        foreach (var feed in feedRegistry.Feeds)
        {
            if (string.IsNullOrEmpty(feed.SearchAutocompleteService))
                continue;

            var url = $"{feed.SearchAutocompleteService}{ctx.Request.QueryString}";
            var success = await ProxyWithRewrite(ctx, url, httpClientFactory, "upstream",
                json => urlRewriter.Rewrite(json, feed), forwardAuth: feed.ForwardAuth);
            if (success) return;
        }

        ctx.Response.StatusCode = 404;
    }

    private static async Task<bool> ProxyWithRewrite(
        HttpContext ctx, string url, IHttpClientFactory httpClientFactory,
        string clientName, Func<string, string> rewrite, bool forwardAuth)
    {
        var client = httpClientFactory.CreateClient(clientName);
        using var request = new HttpRequestMessage(HttpMethod.Get, url);

        if (forwardAuth && ctx.Request.Headers.TryGetValue("Authorization", out var authHeader))
            request.Headers.TryAddWithoutValidation("Authorization", authHeader.ToString());

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
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

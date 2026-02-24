namespace NuGetProxy.Endpoints;

using System.Text.Json;
using Microsoft.Extensions.Options;
using NuGetProxy.Configuration;

public static class ServiceIndexEndpoint
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/v3/index.json", (IOptions<ProxyOptions> options) =>
        {
            var baseUrl = options.Value.BaseUrl.TrimEnd('/');
            var index = new Dictionary<string, object>
            {
                ["version"] = "3.0.0",
                ["resources"] = new object[]
                {
                    new Dictionary<string, string>
                    {
                        ["@id"] = $"{baseUrl}/v3/package/",
                        ["@type"] = "PackageBaseAddress/3.0.0"
                    },
                    new Dictionary<string, string>
                    {
                        ["@id"] = $"{baseUrl}/v3/registration/",
                        ["@type"] = "RegistrationsBaseUrl/3.6.0"
                    },
                    new Dictionary<string, string>
                    {
                        ["@id"] = $"{baseUrl}/v3/search",
                        ["@type"] = "SearchQueryService/3.5.0"
                    },
                    new Dictionary<string, string>
                    {
                        ["@id"] = $"{baseUrl}/v3/autocomplete",
                        ["@type"] = "SearchAutocompleteService/3.5.0"
                    },
                }
            };
            return Results.Json(index);
        });
    }
}

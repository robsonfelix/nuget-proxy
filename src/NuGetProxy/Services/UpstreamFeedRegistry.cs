namespace NuGetProxy.Services;

using System.Text.Json;
using Microsoft.Extensions.Options;
using NuGetProxy.Configuration;

public record ResolvedFeed(
    string Name,
    string PackageBaseAddress,
    string RegistrationsBaseUrl,
    string SearchQueryService,
    string SearchAutocompleteService,
    bool ForwardAuth);

public class UpstreamFeedRegistry : IHostedService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ProxyOptions _options;
    private readonly ILogger<UpstreamFeedRegistry> _logger;
    private List<ResolvedFeed> _feeds = [];

    public IReadOnlyList<ResolvedFeed> Feeds => _feeds;

    public UpstreamFeedRegistry(
        IHttpClientFactory httpClientFactory,
        IOptions<ProxyOptions> options,
        ILogger<UpstreamFeedRegistry> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var resolved = new List<ResolvedFeed>();

        foreach (var feed in _options.UpstreamFeeds)
        {
            try
            {
                var client = _httpClientFactory.CreateClient("upstream");
                var response = await client.GetAsync(feed.ServiceIndexUrl, cancellationToken);
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                var doc = JsonDocument.Parse(json);

                string packageBaseAddress = "";
                string registrationsBaseUrl = "";
                string searchQueryService = "";
                string searchAutocompleteService = "";

                foreach (var resource in doc.RootElement.GetProperty("resources").EnumerateArray())
                {
                    var type = resource.GetProperty("@type").GetString() ?? "";
                    var id = resource.GetProperty("@id").GetString() ?? "";

                    if (type.StartsWith("PackageBaseAddress") && packageBaseAddress == "")
                        packageBaseAddress = id.TrimEnd('/');
                    else if (type.StartsWith("RegistrationsBaseUrl") && registrationsBaseUrl == "")
                        registrationsBaseUrl = id.TrimEnd('/');
                    else if (type.StartsWith("SearchQueryService") && searchQueryService == "")
                        searchQueryService = id.TrimEnd('/');
                    else if (type.StartsWith("SearchAutocompleteService") && searchAutocompleteService == "")
                        searchAutocompleteService = id.TrimEnd('/');
                }

                resolved.Add(new ResolvedFeed(
                    feed.Name,
                    packageBaseAddress,
                    registrationsBaseUrl,
                    searchQueryService,
                    searchAutocompleteService,
                    feed.ForwardAuth));

                _logger.LogInformation(
                    "Resolved upstream feed {Name}: PackageBase={PackageBase}, Registration={Registration}, Search={Search}",
                    feed.Name, packageBaseAddress, registrationsBaseUrl, searchQueryService);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to resolve service index for upstream feed {Name} at {Url}",
                    feed.Name, feed.ServiceIndexUrl);
            }
        }

        _feeds = resolved;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

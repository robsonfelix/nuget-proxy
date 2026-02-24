namespace NuGetProxy.Services;

using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using NuGetProxy.Configuration;

public partial class UrlRewriter
{
    private readonly string _baseUrl;
    private readonly ProxyOptions _options;
    private readonly Regex? _gitlabDownloadRegex;
    private readonly Regex? _gitlabMetadataRegex;
    private readonly Regex? _gitlabQueryRegex;

    public UrlRewriter(IOptions<ProxyOptions> options)
    {
        _options = options.Value;
        _baseUrl = _options.BaseUrl.TrimEnd('/');

        var gitlabUrl = _options.GitLab.Url.TrimEnd('/');
        if (!string.IsNullOrEmpty(gitlabUrl))
        {
            var escaped = Regex.Escape(gitlabUrl);
            // Match both group-level and project-level NuGet URLs
            _gitlabDownloadRegex = new Regex(
                $@"{escaped}/api/v4/(?:groups|projects)/\d+(?:/-)?/packages/nuget/download");
            _gitlabMetadataRegex = new Regex(
                $@"{escaped}/api/v4/(?:groups|projects)/\d+(?:/-)?/packages/nuget/metadata");
            _gitlabQueryRegex = new Regex(
                $@"{escaped}/api/v4/(?:groups|projects)/\d+(?:/-)?/packages/nuget/query");
        }
    }

    public string Rewrite(string json, ResolvedFeed feed)
    {
        var result = json;

        if (!string.IsNullOrEmpty(feed.PackageBaseAddress))
            result = result.Replace(feed.PackageBaseAddress, $"{_baseUrl}/v3/package");

        if (!string.IsNullOrEmpty(feed.RegistrationsBaseUrl))
            result = result.Replace(feed.RegistrationsBaseUrl, $"{_baseUrl}/v3/registration");

        if (!string.IsNullOrEmpty(feed.SearchQueryService))
            result = result.Replace(feed.SearchQueryService, $"{_baseUrl}/v3/search");

        if (!string.IsNullOrEmpty(feed.SearchAutocompleteService))
            result = result.Replace(feed.SearchAutocompleteService, $"{_baseUrl}/v3/autocomplete");

        return result;
    }

    public string RewriteGitLab(string json, int groupId)
    {
        var result = json;

        if (_gitlabDownloadRegex != null)
            result = _gitlabDownloadRegex.Replace(result, $"{_baseUrl}/v3/package");

        if (_gitlabMetadataRegex != null)
            result = _gitlabMetadataRegex.Replace(result, $"{_baseUrl}/v3/registration");

        if (_gitlabQueryRegex != null)
            result = _gitlabQueryRegex.Replace(result, $"{_baseUrl}/v3/search");

        return result;
    }
}

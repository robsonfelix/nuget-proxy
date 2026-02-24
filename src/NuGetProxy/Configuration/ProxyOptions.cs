namespace NuGetProxy.Configuration;

public class ProxyOptions
{
    public const string SectionName = "NuGetProxy";

    public string BaseUrl { get; set; } = "http://localhost:5000";
    public GitLabOptions GitLab { get; set; } = new();
    public List<UpstreamFeedOptions> UpstreamFeeds { get; set; } = [];
}

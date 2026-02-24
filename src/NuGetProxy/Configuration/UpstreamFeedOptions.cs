namespace NuGetProxy.Configuration;

public class UpstreamFeedOptions
{
    public string Name { get; set; } = string.Empty;
    public string ServiceIndexUrl { get; set; } = string.Empty;
    public bool ForwardAuth { get; set; }
}

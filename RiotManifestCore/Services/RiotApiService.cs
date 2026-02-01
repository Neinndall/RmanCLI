using System.Text.Json;
using Spectre.Console;
using RiotManifestCore.Models;

namespace RiotManifestCore.Services;

public class RiotApiService
{
    private readonly HttpClient _httpClient;

    public RiotApiService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<List<RiotVersionInfo>> FetchVersionsAsync(Action<string, string>? onProgress = null)
    {
        var versions = new List<RiotVersionInfo>();

        // Agrupamos por ParentName para el progreso
        var productsByGroup = RmanEndpoints.Products.GroupBy(p => p.ParentName);

        foreach (var group in productsByGroup)
        {
            string groupName = group.Key;
            onProgress?.Invoke(groupName, "Searching regions...");
            
            int startCount = versions.Count;

            foreach (var product in group)
            {
                await FetchSieveVersions(product, versions);
                await FetchConfigVersions(product, versions);
            }

            int foundCount = versions.Count - startCount;
            onProgress?.Invoke(groupName, foundCount > 0 ? $"[green]Success ({foundCount} manifests found)[/]" : "[red]Not found[/]");
        }

        return versions;
    }

    private async Task FetchSieveVersions(RmanProductConfig product, List<RiotVersionInfo> list)
    {
        try 
        {
            string sieveUrl = $"https://sieve.services.riotcdn.net/api/v1/products/{product.SieveId}/version-sets/{product.Region}?q[platform]=windows";
            var response = await _httpClient.GetStringAsync(sieveUrl);
            using var doc = JsonDocument.Parse(response);
            if (doc.RootElement.TryGetProperty("releases", out var releases))
            {
                foreach (var release in releases.EnumerateArray())
                {
                    var labels = release.GetProperty("release").GetProperty("labels");
                    var artifactId = labels.GetProperty("riot:artifact_type_id").GetProperty("values")[0].GetString();
                    var version = labels.GetProperty("riot:artifact_version_id").GetProperty("values")[0].GetString()?.Split('+')[0];
                    var url = release.GetProperty("download").GetProperty("url").GetString();

                    if (!string.IsNullOrEmpty(url))
                    {
                        list.Add(new RiotVersionInfo 
                        { 
                            Product = product.ParentName, // Usamos el nombre del juego
                            Abbreviation = product.Abbreviation,
                            Region = product.Region.ToUpper(),
                            Category = artifactId ?? "game", 
                            Version = version ?? "latest", 
                            ManifestUrl = url 
                        });
                    }
                }
            }
        }
        catch { }
    }

    private async Task FetchConfigVersions(RmanProductConfig product, List<RiotVersionInfo> list)
    {
        try 
        {
            var response = await _httpClient.GetStringAsync(product.ConfigUrl);
            using var doc = JsonDocument.Parse(response);
            
            if (doc.RootElement.TryGetProperty(product.ConfigPath, out var configEntry))
            {
                if (configEntry.TryGetProperty("platforms", out var platforms) && 
                    platforms.TryGetProperty("win", out var win) && 
                    win.TryGetProperty("configurations", out var configs))
                {
                    foreach (var conf in configs.EnumerateArray())
                    {
                        var manifestUrl = conf.GetProperty("patch_url").GetString();
                        if (string.IsNullOrEmpty(manifestUrl)) continue;

                        list.Add(new RiotVersionInfo 
                        { 
                            Product = product.ParentName, // Usamos el nombre del juego
                            Abbreviation = product.Abbreviation,
                            Region = product.Region.ToUpper(),
                            Category = product.Abbreviation == "valorant" ? "valorant-client" : "league-client", 
                            Version = "latest", 
                            ManifestUrl = manifestUrl 
                        });
                    }
                }
            }
        }
        catch { }
    }
}

public class RiotVersionInfo
{
    public string Product { get; set; } = string.Empty;
    public string Abbreviation { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string ManifestUrl { get; set; } = string.Empty;
}

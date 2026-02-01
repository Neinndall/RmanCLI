using System.CommandLine;
using System.Text;
using System.Text.RegularExpressions;
using System.Diagnostics;
using System.Collections.Concurrent;
using System.Reflection;
using Spectre.Console;
using RiotManifestCore.Services;
using RiotManifestCore.Models;

namespace RiotManifestCore;

class Program
{
    private static readonly HttpClient _httpClient = new();
    private static readonly RmanService _rmanService = new();
    private static readonly DownloadService _downloadService = new(_httpClient);
    private static readonly RiotApiService _apiService = new(_httpClient);

    static async Task<int> Main(string[] args)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var version = assembly.GetName().Version?.ToString(3) ?? "1.0.0";
        var description = assembly.GetCustomAttribute<AssemblyDescriptionAttribute>()?.Description ?? "Riot Manifest Utility";

        AnsiConsole.Write(new FigletText("RMAN CORE").Centered().Color(Color.Red));
        AnsiConsole.MarkupLine($"[bold white]Riot Manifest Core[/] [grey]v{version}[/]");
        AnsiConsole.WriteLine();

        var rootCommand = new RootCommand(description);
        rootCommand.AddCommand(CreateDownloadCommand());
        rootCommand.AddCommand(CreateFetchCommand());

        return await rootCommand.InvokeAsync(args);
    }

    private static Command CreateDownloadCommand()
    {
        var command = new Command("download", "Download and patch game assets.");
        command.AddAlias("dl");
        var manifestArg = new Argument<string>("manifest", "URL or path to .rman file.");
        var outputOpt = new Option<string>(new[] { "--output", "-o" }, () => "output", "Target directory.");
        var threadsOpt = new Option<int>(new[] { "--threads", "-t" }, () => 4, "Download threads.");
        var filterOpt = new Option<string>(new[] { "--filter", "-f" }, "Regex filter.");
        var langsOpt = new Option<string[]>(new[] { "--langs", "-l" }, "Languages.") { AllowMultipleArgumentsPerToken = true };
        var jsonOpt = new Option<bool>("--json", "Export to JSON.");
        var dryOpt = new Option<bool>("--dry-run", "Dry run.");
        command.AddOption(outputOpt); command.AddOption(threadsOpt); command.AddOption(filterOpt);
        command.AddOption(langsOpt); command.AddOption(jsonOpt); command.AddOption(dryOpt);
        command.AddArgument(manifestArg);
        command.SetHandler(async (string path, string output, int threads, string? filter, string[]? langs, bool exportJson, bool dryRun) =>
        {
            try {
                byte[] data = path.StartsWith("http") ? await _httpClient.GetByteArrayAsync(path) : await File.ReadAllBytesAsync(path);
                var manifest = _rmanService.Parse(data);
                if (exportJson) { ExportManifestToJson(manifest, path); return; }
                ShowManifestSummary(manifest, filter, langs);
                if (dryRun) return;
                await _downloadService.DownloadManifestAsync(manifest, output, threads, filter, langs, path.StartsWith("http") ? path : null);
            } catch (Exception ex) { AnsiConsole.WriteException(ex); }
        }, manifestArg, outputOpt, threadsOpt, filterOpt, langsOpt, jsonOpt, dryOpt);
        return command;
    }

    private static Command CreateFetchCommand()
    {
        var command = new Command("fetch", "Fetch and discover latest manifest URLs.");
        command.AddAlias("get");
        var saveOpt = new Option<string>(new[] { "--save", "-s" }, "Directory to store .txt files.");
        command.AddOption(saveOpt);

        command.SetHandler(async (string? savePath) =>
        {
            var productStatuses = new ConcurrentDictionary<string, string>();
            productStatuses["League of Legends"] = "[grey]Waiting...[/]";
            productStatuses["Valorant"] = "[grey]Waiting...[/]";

            var versions = new List<RiotVersionInfo>();

            await AnsiConsole.Live(CreateStatusGrid(productStatuses))
                .StartAsync(async ctx =>
                {
                    // 1. Obtener todas las URLs
                    versions = await _apiService.FetchVersionsAsync((product, status) => {
                        productStatuses[product] = status;
                        ctx.UpdateTarget(CreateStatusGrid(productStatuses));
                    });

                    // 2. Extraer Versiones Reales (Deduplicadas por URL)
                    var pendingManifests = versions
                        .Where(x => x.Version == "latest")
                        .GroupBy(x => x.ManifestUrl)
                        .Select(g => g.First())
                        .ToList();

                    if (pendingManifests.Any())
                    {
                        var urlToVersionMap = new ConcurrentDictionary<string, string>();
                        string baseTemp = Path.Combine(Path.GetTempPath(), "RmanCore_" + Guid.NewGuid().ToString("N"));

                        foreach (var v in pendingManifests)
                        {
                            var config = RmanEndpoints.Products.FirstOrDefault(p => (p.ParentName == v.Product || p.ClientName == v.Product) && p.Abbreviation == v.Abbreviation);
                            if (config == null || string.IsNullOrEmpty(config.VersionFileFilter)) continue;

                            productStatuses[v.Product] = $"[yellow]Syncing {v.Region} version...[/]";
                            ctx.UpdateTarget(CreateStatusGrid(productStatuses));

                            try {
                                string currentTemp = Path.Combine(baseTemp, Guid.NewGuid().ToString("N"));
                                Directory.CreateDirectory(currentTemp);

                                var mBytes = await _httpClient.GetByteArrayAsync(v.ManifestUrl);
                                var manifest = _rmanService.Parse(mBytes);

                                // Descarga quirúrgica
                                await _downloadService.DownloadManifestAsync(manifest, currentTemp, 4, config.VersionFileFilter, null, v.ManifestUrl, true);

                                string? exePath = Directory.EnumerateFiles(currentTemp, config.VersionFileFilter, SearchOption.AllDirectories).FirstOrDefault();
                                if (exePath != null)
                                {
                                    string realVersion = "";
                                    if (string.IsNullOrEmpty(config.VersionPattern)) 
                                        realVersion = FileVersionInfo.GetVersionInfo(exePath).FileVersion ?? "latest";
                                    else 
                                        realVersion = GetValorantVersionFromExe(exePath, config.VersionPattern);

                                    if (!string.IsNullOrEmpty(realVersion) && realVersion != "latest") 
                                        urlToVersionMap.TryAdd(v.ManifestUrl, realVersion);
                                }
                                Directory.Delete(currentTemp, true);
                            } catch { }
                        }

                        // APLICACIÓN CRÍTICA: Actualizamos la lista original de versiones
                        foreach (var v in versions)
                        {
                            if (v.Version == "latest" && urlToVersionMap.TryGetValue(v.ManifestUrl, out var real))
                            {
                                v.Version = real;
                            }
                        }

                        if (Directory.Exists(baseTemp)) try { Directory.Delete(baseTemp, true); } catch { }
                    }

                    // Reporte Final
                    productStatuses["League of Legends"] = $"[green]Success ({versions.Count(x => x.Abbreviation == "lol")} manifests)[/]";
                    productStatuses["Valorant"] = $"[green]Success ({versions.Count(x => x.Abbreviation == "valorant")} manifests)[/]";
                    ctx.UpdateTarget(CreateStatusGrid(productStatuses));

                    if (!string.IsNullOrEmpty(savePath))
                    {
                        foreach (var v in versions)
                        {
                            var dir = Path.Combine(savePath, v.Abbreviation, v.Region, "windows", v.Category);
                            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                            // Saneamos la versión para el nombre de archivo
                            string safeVersion = string.Concat(v.Version.Split(Path.GetInvalidFileNameChars())).Trim();
                            await File.WriteAllTextAsync(Path.Combine(dir, $"{safeVersion}.txt"), v.ManifestUrl);
                        }
                    }
                });

            AnsiConsole.WriteLine();
            if (!string.IsNullOrEmpty(savePath)) 
                AnsiConsole.MarkupLine($"[green]✔[/] [white]Catalog successfully saved to:[/] [blue]{Path.GetFullPath(savePath)}[/]");
        }, saveOpt);

        return command;
    }

    private static string GetValorantVersionFromExe(string path, string pattern)
    {
        try {
            var data = File.ReadAllBytes(path);
            var patternBytes = Encoding.Unicode.GetBytes(pattern);
            int index = -1;
            for (int i = 0; i <= data.Length - patternBytes.Length; i++) {
                bool match = true;
                for (int j = 0; j < patternBytes.Length; j++) { if (data[i + j] != patternBytes[j]) { match = false; break; } }
                if (match) { index = i; break; }
            }

            if (index != -1) {
                int pos = index + patternBytes.Length + 10;
                string version = "\0";
                while (version.Contains("\0") || string.IsNullOrWhiteSpace(version)) {
                    pos += 2;
                    if (pos + 64 > data.Length) break;
                    byte[] buffer = new byte[64];
                    Array.Copy(data, pos, buffer, 0, 64);
                    version = Encoding.Unicode.GetString(buffer).Split('\0')[0].Trim();
                }
                return version;
            }
        } catch { }
        return "latest";
    }

    private static Grid CreateStatusGrid(IDictionary<string, string> statuses)
    {
        var grid = new Grid().AddColumn(new GridColumn().NoWrap()).AddColumn();
        foreach (var entry in statuses.OrderBy(x => x.Key)) {
            string icon = entry.Value.Contains("Success") ? "[green]✔[/]" : (entry.Value.Contains("Searching") || entry.Value.Contains("Syncing") ? "[yellow]●[/]" : "[red]✘[/]");
            grid.AddRow($"{icon} [bold white]{entry.Key}:[/]", entry.Value);
        }
        return grid;
    }

    private static void ExportManifestToJson(RmanManifest manifest, string path)
    {
        var exportData = new {
            ManifestID = manifest.ManifestId.ToString("X16"),
            Languages = manifest.Languages.Select(l => new { l.LanguageId, l.Name }),
            Files = manifest.Files.Select(f => new { path = f.Name, size = f.FileSize })
        };
        var jsonPath = Path.ChangeExtension(path.Split('/').Last().Split('?').First(), ".json");
        File.WriteAllText(jsonPath, Newtonsoft.Json.JsonConvert.SerializeObject(exportData, Newtonsoft.Json.Formatting.Indented));
    }

    private static void ShowManifestSummary(RmanManifest manifest, string? filter, string[]? langs)
    {
        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("Metric"); table.AddColumn("Value");
        int fCount = 0; ulong fSize = 0;
        var regex = filter != null ? new Regex(filter, RegexOptions.IgnoreCase) : null;
        foreach (var file in manifest.Files) { if (regex != null && !regex.IsMatch(file.Name)) continue; fCount++; fSize += file.FileSize; }
        table.AddRow("Filtered Files", fCount.ToString("N0"));
        table.AddRow("Total Size", FormatSize(fSize));
        AnsiConsole.Write(table);
    }

    private static string FormatSize(ulong bytes)
    {
        string[] S = { "B", "KB", "MB", "GB", "TB" };
        int i = 0; decimal n = bytes;
        while (n >= 1024 && i < S.Length - 1) { n /= 1024; i++; }
        return $"{n:n2} {S[i]}";
    }
}

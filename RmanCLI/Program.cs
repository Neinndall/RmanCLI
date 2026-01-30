using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using RmanCLI.Models;

namespace RmanCLI
{
    class Program
    {
        static async Task Main(string[] args)
        {
            if (args.Length < 1 || args.Contains("-h") || args.Contains("--help")) {
                PrintHelp();
                return;
            }

            string manifestUrl = args[0];
            string outputDir = "output";
            string? filter = null;
            List<string> locales = new();
            bool includeNeutral = false;
            int threads = 4;

            // Parse arguments
            for (int i = 1; i < args.Length; i++) {
                switch (args[i]) {
                    case "-o": case "--output": outputDir = args[++i]; break;
                    case "-f": case "--filter": filter = args[++i]; break;
                    case "-l": case "--langs": 
                        while (i + 1 < args.Length && !args[i + 1].StartsWith("-")) locales.Add(args[++i]);
                        break;
                    case "-n": case "--neutral": includeNeutral = true; break;
                    case "-t": case "--threads": int.TryParse(args[++i], out threads); break;
                }
            }

            using var httpClient = new HttpClient();
            var parser = new RmanParser(httpClient);

            try {
                Console.WriteLine($"[INFO] Loading manifest: {manifestUrl}");
                var manifest = await parser.LoadManifestAsync(manifestUrl);

                Console.WriteLine($"[INFO] Verifying local files in: {outputDir}");
                Regex? regex = !string.IsNullOrEmpty(filter) ? new Regex(filter!, RegexOptions.IgnoreCase) : null;
                
                // Filtering Logic
                var candidates = manifest.Files.Where(f => {
                    if (regex != null && !regex.IsMatch(f.Name)) return false;
                    bool fileHasLangs = f.Languages.Any();
                    if (locales.Any()) {
                        if (fileHasLangs) return f.Languages.Any(l => locales.Contains(l, StringComparer.OrdinalIgnoreCase));
                        return includeNeutral;
                    }
                    return !fileHasLangs || includeNeutral;
                }).ToList();

                var toUpdate = candidates.Where(f => {
                    string path = Path.Combine(outputDir, f.Name);
                    return !File.Exists(path) || (ulong)new FileInfo(path).Length != f.Size;
                }).ToList();

                if (!toUpdate.Any()) {
                    Console.WriteLine("[SUCCESS] Everything is already up to date.");
                    return;
                }

                Console.WriteLine($"[INFO] Found {toUpdate.Count} files to update using {threads} threads.");
                
                var cts = new CancellationTokenSource();
                Console.CancelKeyPress += (s, e) => { e.Cancel = true; cts.Cancel(); Console.WriteLine("\n[WARN] Cancellation requested..."); };

                await parser.DownloadAssetsAsync(toUpdate, outputDir, threads, cts.Token, (file, current, total) => {
                    float percent = (float)current / total * 100;
                    Console.Write($"\r[PROGRESS] {percent:F1}% | {current}/{total} | {Path.GetFileName(file)}".PadRight(Console.WindowWidth - 1));
                });

                Console.WriteLine("\n[SUCCESS] Operation completed successfully.");
            }
            catch (Exception ex) {
                Console.WriteLine($"\n[FATAL ERROR] {ex.Message}");
            }
        }

        static void PrintHelp()
        {
            Console.WriteLine("RmanCLI - Native Riot Manifest Downloader (C# Edition)");
            Console.WriteLine("\nUsage: RmanCLI <manifest_url> [options]");
            Console.WriteLine("\nOptions:");
            Console.WriteLine("  -o, --output <path>    Output directory (default: 'output')");
            Console.WriteLine("  -f, --filter <regex>   Filter files by name");
            Console.WriteLine("  -l, --langs <list>     Specific languages to download (e.g., es_ES en_US)");
            Console.WriteLine("  -n, --neutral          Include language-neutral files");
            Console.WriteLine("  -t, --threads <num>    Number of concurrent downloads (default: 4)");
            Console.WriteLine("  -h, --help             Show this help message");
        }
    }
}
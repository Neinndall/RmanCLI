using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using RiotManifestCore.Models;
using Spectre.Console;
using ZstdSharp;
using System.Net.Http.Headers;
using System.Text;

namespace RiotManifestCore.Services;

public class DownloadService
{
    private readonly HttpClient _httpClient;
    private string _bundleBaseUrl = "https://lol.dyn.riotcdn.net/channels/public/bundles";
    private readonly HashService _hashService = new();

    public DownloadService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    private void DetermineBundleBaseUrl(string manifestUrl)
    {
        _bundleBaseUrl = RmanEndpoints.GetBundleUrl(manifestUrl);
    }

    private class ChunkDownloadTask
    {
        public RmanChunk Chunk { get; set; } = null!;
        public ulong FileOffset { get; set; }
        public RmanFile FileInfo { get; set; } = null!;
        public string FullPath { get; set; } = string.Empty;
    }

    private class FilePatchTask
    {
        public RmanFile FileInfo { get; set; } = null!;
        public string FullPath { get; set; } = string.Empty;
        public Dictionary<ulong, List<ChunkDownloadTask>> ChunksByBundle { get; set; } = new();
    }

    public async Task DownloadManifestAsync(RmanManifest manifest, string outputPath, int maxThreads, string? filter = null, string[]? langs = null, string? manifestUrl = null, bool silent = false)
    {
        if (!string.IsNullOrEmpty(manifestUrl)) DetermineBundleBaseUrl(manifestUrl);

        var regex = !string.IsNullOrEmpty(filter) ? new Regex(filter, RegexOptions.IgnoreCase) : null;
        var selectedLangIds = new HashSet<byte>();
        
        if (langs != null && langs.Any())
        {
            foreach (var langName in langs)
            {
                var lang = manifest.Languages.FirstOrDefault(l => l.Name.Equals(langName, StringComparison.OrdinalIgnoreCase));
                if (lang != null) selectedLangIds.Add(lang.LanguageId);
            }
        }

        var filteredFiles = manifest.Files.Where(file => 
        {
            if (regex != null && !regex.IsMatch(file.Name)) return false;
            if (selectedLangIds.Count > 0)
            {
                bool isNeutral = file.LanguageIds.Count == 0;
                bool matchesLang = file.LanguageIds.Any(id => selectedLangIds.Contains(id));
                if (!isNeutral && !matchesLang) return false;
            }
            return true;
        }).ToList();

        if (!Directory.Exists(outputPath)) Directory.CreateDirectory(outputPath);

        var filesToPatch = new ConcurrentBag<FilePatchTask>();

        await Task.Run(async () =>
        {
            var scanSemaphore = new SemaphoreSlim(maxThreads);
            var scanTasks = filteredFiles.Select(async file =>
            {
                await scanSemaphore.WaitAsync();
                try 
                {
                    var fullPath = Path.Combine(outputPath, file.Name.Replace("/", Path.DirectorySeparatorChar.ToString()));
                    bool fileExists = File.Exists(fullPath);
                    bool wrongSize = fileExists && (ulong)new FileInfo(fullPath).Length != file.FileSize;
                    
                    var chunksByBundle = new Dictionary<ulong, List<ChunkDownloadTask>>();
                    ulong currentFileOffset = 0;

                    if (!fileExists || wrongSize)
                    {
                        foreach (var chunkId in file.ChunkIds)
                        {
                            var chunk = manifest.GetChunk(chunkId);
                            if (chunk == null) continue;
                            if (!chunksByBundle.ContainsKey(chunk.BundleId)) chunksByBundle[chunk.BundleId] = new List<ChunkDownloadTask>();
                            chunksByBundle[chunk.BundleId].Add(new ChunkDownloadTask { Chunk = chunk, FileOffset = currentFileOffset, FileInfo = file, FullPath = fullPath });
                            currentFileOffset += chunk.UncompressedSize;
                        }
                    }
                    else
                    {
                        using (var fs = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                        {
                            foreach (var chunkId in file.ChunkIds)
                            {
                                var chunk = manifest.GetChunk(chunkId);
                                if (chunk == null) continue;

                                bool needsUpdate = true;
                                if ((ulong)fs.Length >= currentFileOffset + chunk.UncompressedSize)
                                {
                                    var localData = new byte[chunk.UncompressedSize];
                                    fs.Position = (long)currentFileOffset;
                                    await fs.ReadExactlyAsync(localData, 0, (int)chunk.UncompressedSize);
                                    if (_hashService.VerifyChunk(localData, chunk.ChunkId, file.HashType)) needsUpdate = false;
                                }

                                if (needsUpdate)
                                {
                                    if (!chunksByBundle.ContainsKey(chunk.BundleId)) chunksByBundle[chunk.BundleId] = new List<ChunkDownloadTask>();
                                    chunksByBundle[chunk.BundleId].Add(new ChunkDownloadTask { Chunk = chunk, FileOffset = currentFileOffset, FileInfo = file, FullPath = fullPath });
                                }
                                currentFileOffset += chunk.UncompressedSize;
                            }
                        }
                    }

                    if (chunksByBundle.Any())
                    {
                        filesToPatch.Add(new FilePatchTask { FileInfo = file, FullPath = fullPath, ChunksByBundle = chunksByBundle });
                    }
                }
                finally { scanSemaphore.Release(); }
            });

            await Task.WhenAll(scanTasks);
        });

        if (!filesToPatch.Any()) return;

        var sortedFilesToPatch = filesToPatch.OrderBy(f => f.FileInfo.Name).ToList();

        foreach (var task in sortedFilesToPatch)
        {
            var dirName = Path.GetDirectoryName(task.FullPath);
            if (!string.IsNullOrEmpty(dirName) && !Directory.Exists(dirName)) Directory.CreateDirectory(dirName);

            // FIX: Corregido task.FileInfo.FullPath por task.FullPath
            if (!File.Exists(task.FullPath) || (ulong)new FileInfo(task.FullPath).Length != task.FileInfo.FileSize)
            {
                using (var fs = new FileStream(task.FullPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
                {
                    fs.SetLength((long)task.FileInfo.FileSize);
                }
            }

            var semaphore = new SemaphoreSlim(maxThreads);
            var downloadTasks = task.ChunksByBundle.Select(async pair =>
            {
                await semaphore.WaitAsync();
                try
                {
                    using (var decompressor = new Decompressor())
                    {
                        await DownloadAndPatchChunksAsync(pair.Key, pair.Value, decompressor);
                    }
                }
                finally { semaphore.Release(); }
            });

            await Task.WhenAll(downloadTasks);
        }
    }

    private async Task DownloadAndPatchChunksAsync(ulong bundleId, List<ChunkDownloadTask> tasks, Decompressor decompressor)
    {
        string bundleUrl = $"{_bundleBaseUrl}/{bundleId:X16}.bundle";
        var sortedTasks = tasks.OrderBy(t => t.Chunk.BundleOffset).ToList();

        int currentIndex = 0;
        while (currentIndex < sortedTasks.Count)
        {
            var group = new List<ChunkDownloadTask> { sortedTasks[currentIndex] };
            int nextIndex = currentIndex + 1;
            while (nextIndex < sortedTasks.Count)
            {
                var prev = sortedTasks[nextIndex - 1].Chunk;
                var curr = sortedTasks[nextIndex].Chunk;
                if (prev.BundleOffset + prev.CompressedSize == curr.BundleOffset)
                {
                    group.Add(sortedTasks[nextIndex]);
                    nextIndex++;
                }
                else break;
            }

            long start = (long)group.First().Chunk.BundleOffset;
            long end = (long)(group.Last().Chunk.BundleOffset + group.Last().Chunk.CompressedSize - 1);

            try 
            {
                var request = new HttpRequestMessage(HttpMethod.Get, bundleUrl);
                request.Headers.Range = new RangeHeaderValue(start, end);

                using (var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead))
                {
                    response.EnsureSuccessStatusCode();
                    var rangeData = await response.Content.ReadAsByteArrayAsync();

                    int internalOffset = 0;
                    foreach (var chunkTask in group)
                    {
                        var compressed = new byte[chunkTask.Chunk.CompressedSize];
                        Array.Copy(rangeData, internalOffset, compressed, 0, (int)chunkTask.Chunk.CompressedSize);
                        var uncompressed = decompressor.Unwrap(compressed).ToArray();

                        if (!_hashService.VerifyChunk(uncompressed, chunkTask.Chunk.ChunkId, chunkTask.FileInfo.HashType))
                            throw new Exception("Integrity failure");

                        await WriteChunkToFileAsync(chunkTask.FullPath, (long)chunkTask.FileOffset, uncompressed);
                        internalOffset += (int)chunkTask.Chunk.CompressedSize;
                    }
                }
            }
            catch { }
            currentIndex = nextIndex;
        }
    }

    private async Task WriteChunkToFileAsync(string path, long offset, byte[] data)
    {
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite))
        {
            fs.Position = offset;
            await fs.WriteAsync(data, 0, data.Length);
        }
    }
}

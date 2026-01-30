using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Buffers.Binary;
using ZstdSharp;
using RmanCLI.Models;

namespace RmanCLI
{
    public class RmanParser
    {
        private readonly HttpClient _httpClient;
        private const string DefaultBundleUrl = "https://lol.dyn.riotcdn.net/channels/public/bundles";

        public RmanParser(HttpClient httpClient) => _httpClient = httpClient;

        public async Task<RmanManifest> LoadManifestAsync(string urlOrPath)
        {
            byte[] data;
            if (Uri.IsWellFormedUriString(urlOrPath, UriKind.Absolute))
                data = await _httpClient.GetByteArrayAsync(urlOrPath);
            else
                data = await File.ReadAllBytesAsync(urlOrPath);

            return ParseManifest(data);
        }

        private RmanManifest ParseManifest(byte[] data)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(data) != 0x4E414D52)
                throw new Exception("Invalid RMAN magic");

            var manifest = new RmanManifest();
            uint offset = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(8));
            uint compressedSize = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(12));
            manifest.ManifestId = BinaryPrimitives.ReadUInt64LittleEndian(data.AsSpan(16));
            uint uncompressedSize = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(24));

            byte[] bodyCompressed = new byte[compressedSize];
            Array.Copy(data, (int)offset, bodyCompressed, 0, (int)compressedSize);

            using var decompressor = new Decompressor();
            byte[] body = decompressor.Unwrap(bodyCompressed).ToArray();

            ParseBody(manifest, body);
            return manifest;
        }

        private void ParseBody(RmanManifest manifest, byte[] body)
        {
            int rootOffset = BinaryPrimitives.ReadInt32LittleEndian(body);
            int vtableOffset = rootOffset - BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(rootOffset));
            ushort vtableSize = BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(vtableOffset));

            int GetFieldOffset(int index) {
                int entryOffset = 4 + (index * 2);
                if (entryOffset >= vtableSize) return 0;
                return BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(vtableOffset + entryOffset));
            }

            void ReadVector(int fieldIndex, Action<int> readElement) {
                int offset = GetFieldOffset(fieldIndex);
                if (offset == 0) return;
                int vectorPos = rootOffset + offset + BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(rootOffset + offset));
                int length = BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(vectorPos));
                for (int i = 0; i < length; i++)
                    readElement(vectorPos + 4 + (i * 4));
            }

            // 1. Bundles
            ReadVector(0, pos => {
                int bundlePos = pos + BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(pos));
                var bundle = new RmanBundle { BundleId = ReadUInt64(body, bundlePos, 0) };
                int chunksVectorPos = GetVectorPos(body, bundlePos, 1);
                int chunksCount = BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(chunksVectorPos));
                uint currentOffset = 0;
                for (int i = 0; i < chunksCount; i++) {
                    int chunkPos = chunksVectorPos + 4 + (i * 4);
                    chunkPos += BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(chunkPos));
                    var chunk = new RmanChunk {
                        ChunkId = ReadUInt64(body, chunkPos, 0),
                        CompressedSize = ReadUInt32(body, chunkPos, 1),
                        UncompressedSize = ReadUInt32(body, chunkPos, 2),
                        BundleId = bundle.BundleId,
                        BundleOffset = currentOffset
                    };
                    bundle.Chunks.Add(chunk);
                    manifest.Chunks[chunk.ChunkId] = chunk;
                    currentOffset += chunk.CompressedSize;
                }
                manifest.Bundles[bundle.BundleId] = bundle;
            });

            // 2. Languages
            var languages = new Dictionary<byte, string>();
            ReadVector(1, pos => {
                int langPos = pos + BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(pos));
                languages[ReadByte(body, langPos, 0)] = ReadString(body, langPos, 1);
            });

            // 3. Directories
            var dirs = new Dictionary<ulong, (ulong parentId, string name)>();
            ReadVector(3, pos => {
                int dirPos = pos + BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(pos));
                dirs[ReadUInt64(body, dirPos, 0)] = (ReadUInt64(body, dirPos, 1), ReadString(body, dirPos, 2));
            });

            // 4. Files
            ReadVector(2, pos => {
                int filePos = pos + BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(pos));
                var file = new RmanFile {
                    Size = ReadUInt64(body, filePos, 2),
                    Name = ReadString(body, filePos, 3)
                };
                ulong langMask = ReadUInt64(body, filePos, 4);
                for (int i = 0; i < 64; i++)
                    if ((langMask & (1UL << i)) != 0 && languages.TryGetValue((byte)(i + 1), out var langName))
                        file.Languages.Add(langName);

                int chunksIdsVectorPos = GetVectorPos(body, filePos, 7);
                int chunksIdsCount = BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(chunksIdsVectorPos));
                ulong fileOffset = 0;
                for (int i = 0; i < chunksIdsCount; i++) {
                    ulong chunkId = BinaryPrimitives.ReadUInt64LittleEndian(body.AsSpan(chunksIdsVectorPos + 4 + (i * 8)));
                    if (manifest.Chunks.TryGetValue(chunkId, out var chunk)) {
                        file.Chunks.Add(new RmanChunk {
                            ChunkId = chunk.ChunkId, BundleId = chunk.BundleId,
                            CompressedSize = chunk.CompressedSize, UncompressedSize = chunk.UncompressedSize,
                            BundleOffset = chunk.BundleOffset, FileOffset = fileOffset
                        });
                        fileOffset += chunk.UncompressedSize;
                    }
                }

                ulong dirId = ReadUInt64(body, filePos, 1);
                while (dirId != 0 && dirs.TryGetValue(dirId, out var dirInfo)) {
                    file.Name = $"{dirInfo.name}/{file.Name}";
                    dirId = dirInfo.parentId;
                }
                manifest.Files.Add(file);
            });
        }

        #region Binary Helpers
        private static int GetFieldOffsetLocal(byte[] body, int tablePos, int index) {
            int vtablePosLocal = tablePos - BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(tablePos));
            ushort vSize = BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(vtablePosLocal));
            int fieldOff = 4 + (index * 2);
            return fieldOff >= vSize ? 0 : BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(vtablePosLocal + fieldOff));
        }
        private static ulong ReadUInt64(byte[] body, int tablePos, int index) { int off = GetFieldOffsetLocal(body, tablePos, index); return off == 0 ? 0 : BinaryPrimitives.ReadUInt64LittleEndian(body.AsSpan(tablePos + off)); }
        private static uint ReadUInt32(byte[] body, int tablePos, int index) { int off = GetFieldOffsetLocal(body, tablePos, index); return off == 0 ? 0 : BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(tablePos + off)); }
        private static byte ReadByte(byte[] body, int tablePos, int index) { int off = GetFieldOffsetLocal(body, tablePos, index); return off == 0 ? (byte)0 : body[tablePos + off]; }
        private static string ReadString(byte[] body, int tablePos, int index) {
            int off = GetFieldOffsetLocal(body, tablePos, index);
            if (off == 0) return "";
            int stringPos = tablePos + off + BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(tablePos + off));
            int length = BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(stringPos));
            return System.Text.Encoding.UTF8.GetString(body.AsSpan(stringPos + 4, length));
        }
        private static int GetVectorPos(byte[] body, int tablePos, int index) {
            int off = GetFieldOffsetLocal(body, tablePos, index);
            return off == 0 ? 0 : tablePos + off + BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(tablePos + off));
        }
        #endregion

        public async Task DownloadAssetsAsync(List<RmanFile> files, string outputDir, int maxThreads, CancellationToken ct, Action<string, int, int>? progress)
        {
            int completed = 0;
            using var semaphore = new SemaphoreSlim(maxThreads);
            var tasks = files.Select(async file => {
                await semaphore.WaitAsync(ct);
                try { await DownloadSingleFileAsync(file, outputDir, ct); }
                catch (Exception ex) { Console.WriteLine($"\n[ERROR] {file.Name}: {ex.Message}"); }
                finally {
                    int current = Interlocked.Increment(ref completed);
                    progress?.Invoke(file.Name, current, files.Count);
                    semaphore.Release();
                }
            });
            await Task.WhenAll(tasks);
        }

        private async Task DownloadSingleFileAsync(RmanFile file, string outputDir, CancellationToken ct)
        {
            string fullPath = Path.Combine(outputDir, file.Name);
            string? directoryPath = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directoryPath)) Directory.CreateDirectory(directoryPath);
            
            if (File.Exists(fullPath) && (ulong)new FileInfo(fullPath).Length == file.Size) return;

            using var fs = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None, 256 * 1024, true);
            fs.SetLength((long)file.Size);
            using var decompressor = new Decompressor();

            foreach (var bundleGroup in file.Chunks.GroupBy(c => c.BundleId)) {
                string url = $"{DefaultBundleUrl}/{bundleGroup.Key:X16}.bundle";
                var chunks = bundleGroup.OrderBy(c => c.BundleOffset).ToList();
                int i = 0;
                while (i < chunks.Count) {
                    ct.ThrowIfCancellationRequested();
                    int startIdx = i;
                    ulong totalLen = chunks[startIdx].CompressedSize;
                    while (i + 1 < chunks.Count && chunks[i + 1].BundleOffset == (chunks[i].BundleOffset + chunks[i].CompressedSize)) {
                        totalLen += chunks[i + 1].CompressedSize;
                        i++;
                    }

                    byte[] compressedBlock = await DownloadRangeAsync(url, chunks[startIdx].BundleOffset, totalLen, ct);
                    int currentBlockOffset = 0;
                    for (int j = startIdx; j <= i; j++) {
                        var decompressed = decompressor.Unwrap(compressedBlock.AsSpan(currentBlockOffset, (int)chunks[j].CompressedSize)).ToArray();
                        fs.Seek((long)chunks[j].FileOffset, SeekOrigin.Begin);
                        await fs.WriteAsync(decompressed, ct);
                        currentBlockOffset += (int)chunks[j].CompressedSize;
                    }
                    i++;
                }
            }
        }

        private async Task<byte[]> DownloadRangeAsync(string url, ulong offset, ulong length, CancellationToken ct)
        {
            int retries = 3;
            while (true) {
                try {
                    var req = new HttpRequestMessage(HttpMethod.Get, url);
                    req.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue((long)offset, (long)(offset + length - 1));
                    using var res = await _httpClient.SendAsync(req, HttpCompletionOption.ResponseContentRead, ct);
                    res.EnsureSuccessStatusCode();
                    return await res.Content.ReadAsByteArrayAsync(ct);
                }
                catch when (retries-- > 0) { await Task.Delay(1000, ct); }
                catch { throw; }
            }
        }
    }
}

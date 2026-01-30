using System.Collections.Generic;

namespace RmanCLI.Models
{
    public class RmanManifest
    {
        public ulong ManifestId { get; set; }
        public List<RmanFile> Files { get; set; } = new();
        public Dictionary<ulong, RmanChunk> Chunks { get; set; } = new();
        public Dictionary<ulong, RmanBundle> Bundles { get; set; } = new();
    }
}

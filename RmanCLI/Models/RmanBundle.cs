using System.Collections.Generic;

namespace RmanCLI.Models
{
    public class RmanBundle
    {
        public ulong BundleId { get; set; }
        public List<RmanChunk> Chunks { get; set; } = new();
    }
}

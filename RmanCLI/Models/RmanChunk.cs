using System.Collections.Generic;

namespace RmanCLI.Models
{
    public class RmanChunk
    {
        public ulong ChunkId { get; set; }
        public ulong BundleId { get; set; }
        public uint CompressedSize { get; set; }
        public uint UncompressedSize { get; set; }
        public ulong BundleOffset { get; set; }
        public ulong FileOffset { get; set; }
    }
}

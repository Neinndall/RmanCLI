using System.Collections.Generic;

namespace RmanCLI.Models
{
    public class RmanFile
    {
        public string Name { get; set; } = string.Empty;
        public ulong Size { get; set; }
        public List<string> Languages { get; set; } = new();
        public List<RmanChunk> Chunks { get; set; } = new();
    }
}

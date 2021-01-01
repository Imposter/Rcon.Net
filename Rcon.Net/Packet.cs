using System.Collections.Generic;

namespace Rcon.Net
{
    public enum PacketType
    {
        Request,
        Response
    }

    public class Packet
    {
        public PacketType Type { get; set; }
        public bool FromServer { get; set; }
        public uint Sequence { get; set; }
        public uint Size { get; set; }
        public IList<string> Words { get; set; }
    }
}

namespace ZL.ConnectionGuard.Models
{
    public readonly struct CanFrame
    {
        public CanFrame(uint id, byte[] data, bool isExtended = false, bool isRemote = false)
        {
            Id = id;
            Data = data ?? System.Array.Empty<byte>();
            IsExtended = isExtended;
            IsRemote = isRemote;
        }

        public uint Id { get; }
        public byte[] Data { get; }
        public bool IsExtended { get; }
        public bool IsRemote { get; }
    }
}

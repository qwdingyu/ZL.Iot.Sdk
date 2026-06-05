namespace ZL.ConnectionGuard.Models
{
    public readonly struct LinFrame
    {
        public LinFrame(byte id, byte[] data)
        {
            Id = id;
            Data = data ?? System.Array.Empty<byte>();
        }

        public byte Id { get; }
        public byte[] Data { get; }
    }
}

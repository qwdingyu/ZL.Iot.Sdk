namespace ZL.Framing
{
    public interface IFrameDecoder
    {
        DecodeResult TryDecode(IByteBuffer buffer, out byte[] frame);
    }
}

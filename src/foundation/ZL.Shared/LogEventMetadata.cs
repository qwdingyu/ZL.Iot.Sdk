namespace ZL.Shared
{
    public sealed class LogEventMetadata
    {
        public string Instance { get; set; } = string.Empty;
        public string Direction { get; set; } = "SYS";
        public string Payload { get; set; } = string.Empty;
        public string SessionId { get; set; } = string.Empty;
    }
}

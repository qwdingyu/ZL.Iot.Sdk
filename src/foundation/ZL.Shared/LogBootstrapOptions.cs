namespace ZL.Shared
{
    public sealed class LogBootstrapOptions
    {
        public static LogBootstrapOptions Default { get; } = new LogBootstrapOptions();

        public string? BaseDirectory { get; set; }
        public string? AppNameOverride { get; set; }
        public bool EnableConsole { get; set; }
    }
}

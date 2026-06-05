namespace ZL.ConnectionGuard.Models
{
    public enum GuardState
    {
        Disconnected,
        Connecting,
        Connected,
        Reconnecting,
        Faulted
    }
}

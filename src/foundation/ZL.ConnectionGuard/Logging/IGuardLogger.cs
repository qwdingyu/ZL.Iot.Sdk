namespace ZL.ConnectionGuard.Logging
{
    public interface IGuardLogger
    {
        void Debug(string message);
        void Info(string message);
        void Warn(string message);
        void Error(string message, System.Exception? ex = null);
    }
}

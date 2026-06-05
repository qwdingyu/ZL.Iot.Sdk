namespace ZL.ConnectionGuard.Logging
{
    public sealed class NullLogger : IGuardLogger
    {
        public static readonly NullLogger Instance = new NullLogger();

        private NullLogger()
        {
        }

        public void Debug(string message)
        {
        }

        public void Info(string message)
        {
        }

        public void Warn(string message)
        {
        }

        public void Error(string message, System.Exception? ex = null)
        {
        }
    }
}

using System;

namespace ZL.ProtocolGateway
{
    /// <summary>
    /// 死信消息
    /// </summary>
    public class DeadLetterMessage
    {
        public Message Message { get; set; }
        public Exception Exception { get; set; }
        public DateTimeOffset FailedAt { get; set; }

        /// <summary>
        /// 死信重试次数 — P0-4 修复：防止 RetryDeadLetters 无限循环。
        /// 每次进入死信队列时递增，超过 MaxDeadLetterRetries 的消息将被永久丢弃。
        /// </summary>
        public int RetryCount { get; set; }
    }
}

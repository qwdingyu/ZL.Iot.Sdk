using System;
using System.Collections.Generic;
using System.Threading;

namespace ZL.Framing
{
    public sealed class FrameAssembler : IDisposable
    {
        private readonly StickyWindowBuffer _buffer;
        private readonly IFrameDecoder _decoder;
        private readonly Action<byte[], FrameAssembleMode> _onFrame;
        private readonly Timer _idleTimer;
        private readonly int _timeoutMs;
        private readonly TimeoutMode _timeoutMode;
        private readonly object _sync = new object();
        private DateTime _lastReceivedUtc;
        private int _disposed;

        public FrameAssembler(ByteFramingOptions options, int timeoutMs, Action<byte[], FrameAssembleMode> onFrame)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            _onFrame = onFrame ?? throw new ArgumentNullException(nameof(onFrame));
            options.Normalize();

            _timeoutMs = Math.Max(0, timeoutMs);
            var strategy = options.GetStrategy();
            _timeoutMode = options.GetTimeoutMode();
            if (strategy == ByteFramingStrategy.Timeout && string.IsNullOrWhiteSpace(options.TimeoutAction))
            {
                _timeoutMode = TimeoutMode.Emit;
            }
            _buffer = new StickyWindowBuffer(options.BufferInitialCapacity, options.BufferMaxCapacity);

            _decoder = strategy switch
            {
                ByteFramingStrategy.FixedLength => new FixedLengthFrameDecoder(
                    options.FixedLength,
                    options.GetSyncBytes(),
                    options.GetResyncMode(),
                    options.MaxResyncSkip),
                ByteFramingStrategy.LengthField => new LengthFieldFrameDecoder(options),
                ByteFramingStrategy.LengthFieldWithChecksum => new LengthFieldFrameDecoder(options),
                _ => null
            };

            _idleTimer = new Timer(_ => FlushOnIdle(), null, Timeout.Infinite, Timeout.Infinite);
        }

        public void Append(byte[] data)
        {
            if (data == null || data.Length == 0) return;
            if (IsDisposed()) return;

            if (_decoder == null)
            {
                if (_timeoutMs <= 0)
                {
                    _onFrame(data, FrameAssembleMode.Chunk);
                    return;
                }
            }

            List<(byte[] Frame, FrameAssembleMode Mode)> frames = null;
            lock (_sync)
            {
                if (IsDisposed()) return;
                _buffer.Write(data);
                _lastReceivedUtc = DateTime.UtcNow;
                if (_timeoutMs > 0)
                {
                    TryChangeTimer(_timeoutMs, Timeout.Infinite);
                }

                if (_decoder != null)
                {
                    frames = DecodeFramesLocked();
                }
            }

            if (frames == null) return;
            foreach (var item in frames)
            {
                _onFrame(item.Frame, item.Mode);
            }
        }

        public void Stop()
        {
            if (IsDisposed()) return;
            lock (_sync)
            {
                if (IsDisposed()) return;
                _buffer.Clear();
                TryChangeTimer(Timeout.Infinite, Timeout.Infinite);
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1)
            {
                return;
            }

            lock (_sync)
            {
                _buffer.Clear();
            }

            try
            {
                _idleTimer.Dispose();
            }
            catch (ObjectDisposedException)
            {
                // Keep dispose idempotent for concurrent shutdown paths.
            }
        }

        private List<(byte[] Frame, FrameAssembleMode Mode)> DecodeFramesLocked()
        {
            var frames = new List<(byte[] Frame, FrameAssembleMode Mode)>();
            int safety = 0;
            while (_buffer.ReadableBytes > 0 && safety++ < 1024)
            {
                var result = _decoder.TryDecode(_buffer, out var frame);
                if (result == DecodeResult.FrameAvailable && frame != null)
                {
                    frames.Add((frame, FrameAssembleMode.Decoded));
                    continue;
                }
                if (result == DecodeResult.Recovery)
                {
                    frames.Add((Array.Empty<byte>(), FrameAssembleMode.Recovery));
                    _buffer.DiscardReadBytes();
                    continue;
                }
                break;
            }
            _buffer.DiscardReadBytes();
            return frames;
        }

        private void FlushOnIdle()
        {
            if (_timeoutMs <= 0) return;
            if (IsDisposed()) return;

            byte[] frame = null;
            TimeoutMode mode;
            lock (_sync)
            {
                if (IsDisposed()) return;
                if (_buffer.ReadableBytes == 0) return;
                if ((DateTime.UtcNow - _lastReceivedUtc).TotalMilliseconds < _timeoutMs) return;

                mode = _timeoutMode;
                if (mode == TimeoutMode.Emit)
                {
                    frame = _buffer.ReadBytes(_buffer.ReadableBytes);
                }
                else if (mode == TimeoutMode.Clear)
                {
                    _buffer.Clear();
                }
            }

            if (mode == TimeoutMode.Emit && frame != null && frame.Length > 0)
            {
                _onFrame(frame, FrameAssembleMode.Timeout);
            }
        }

        private bool IsDisposed()
        {
            return Volatile.Read(ref _disposed) == 1;
        }

        private void TryChangeTimer(int dueTime, int period)
        {
            try
            {
                _idleTimer.Change(dueTime, period);
            }
            catch (ObjectDisposedException)
            {
                // Ignore racing timer changes during shutdown.
            }
        }
    }

    public enum FrameAssembleMode
    {
        Decoded,
        Timeout,
        Recovery,
        Chunk
    }
}

#if SLOW_TESTS
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using PlcSimulator.Core.Memory;
using PlcSimulator.Protocols.Siemens;
using ZL.ProtocolGateway.Plugins;
using Xunit;

namespace ZL.ProtocolGateway.Tests
{
    /// <summary>
    /// S7 仿真器集成测试夹具。
    /// 每个测试类共享一个 S7Server + PlcMemory 实例，测试方法通过写入不同地址实现隔离。
    /// </summary>
    public class S7ServerFixture : IDisposable
    {
        public PlcMemory Memory { get; }
        public S7Server Server { get; }
        public int Port { get; }

        public S7ServerFixture()
        {
            Port = FindFreePort(19900, 19999);
            Memory = new PlcMemory();
            FillDefaultTestData(Memory);
            Server = new S7Server(Memory, Port, S7CpuModel.S7_1200, station: 0);

            var started = Server.Start();
            if (!started)
                throw new InvalidOperationException($"S7Server 启动失败，端口 {Port}");
        }

        /// <summary>清理整个 DB 区域，恢复预设测试数据</summary>
        public void ResetMemory()
        {
            Memory.ClearAllDbAreas();
            FillDefaultTestData(Memory);
        }

        /// <summary>读取 DB1 中指定偏移的原始字节</summary>
        public byte[] ReadDb(int dbNumber, int byteOffset, int length)
        {
            return Memory.Read(0x84, dbNumber, byteOffset, length);
        }

        /// <summary>读取 M 区域原始字节</summary>
        public byte[] ReadM(int byteOffset, int length)
        {
            return Memory.Read(0x83, 0, byteOffset, length);
        }

        /// <summary>读取 I 区域原始字节</summary>
        public byte[] ReadI(int byteOffset, int length)
        {
            return Memory.Read(0x81, 0, byteOffset, length);
        }

        /// <summary>读取 Q 区域原始字节</summary>
        public byte[] ReadQ(int byteOffset, int length)
        {
            return Memory.Read(0x82, 0, byteOffset, length);
        }

        public void Dispose()
        {
            Server?.Stop();
        }

        /// <summary>获取一个快速测试用的 S7OutputConfig（连接到本夹具的 S7Server）</summary>
        public static S7OutputConfig CreateFastConfig(int port)
        {
            return new S7OutputConfig
            {
                ServerIp = "127.0.0.1",
                Port = port,
                Rack = 0,
                Slot = 1,
                ConnectTimeoutMs = 1000,
                SendTimeoutMs = 2000,
                ReconnectIntervalMs = 500,
                ErrorThreshold = 5,
                DefaultWriteAddress = "DB1.DBW0",
                Name = "S7-Integration-Test"
            };
        }

        /// <summary>在指定范围内查找可用 TCP 端口</summary>
        public static int FindFreePort(int start = 19900, int end = 19999)
        {
            for (int port = start; port <= end; port++)
            {
                try
                {
                    var listener = new TcpListener(IPAddress.Loopback, port);
                    listener.Start();
                    listener.Stop();
                    return port;
                }
                catch
                {
                    // 端口被占用，继续尝试下一个
                }
            }
            throw new InvalidOperationException($"在范围 [{start}, {end}] 内未找到可用端口");
        }

        /// <summary>
        /// 填充默认测试数据（与 S7Simulator.Standalone Program.cs 保持一致）
        /// </summary>
        private static void FillDefaultTestData(PlcMemory memory)
        {
            // DB1 区域预设值
            memory.WriteByte(0, 0x01);       // DB1.0  =  true
            memory.Write(2, new byte[] { 0x7F, 0xFF });   // DB1.2  =  INT16 max
            memory.Write(4, new byte[] { 0x48, 0x49, 0x0F, 0x40 }); // DB1.4  =  FLOAT ~3.14
            memory.Write(8, new byte[] { 0xFF, 0xFF });   // DB1.8  =  UINT16 max

            // M 区域
            memory.Write(0x83, 0, 0, new byte[] { 0xAA });
            memory.Write(0x83, 0, 100, new byte[] { 0xD2, 0x04 });

            // I 区域
            memory.Write(0x81, 0, 0, new byte[] { 0x01, 0x02, 0x03, 0x04 });

            // Q 区域
            memory.Write(0x82, 0, 0, new byte[] { 0x05, 0x06, 0x07, 0x08 });
        }
    }

    [CollectionDefinition("S7ServerIntegration")]
    public class S7ServerIntegrationCollection : ICollectionFixture<S7ServerFixture>
    {
    }
}

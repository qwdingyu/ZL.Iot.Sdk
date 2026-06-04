using HslCommunication;
using HslCommunication.MQTT;
using System;
using System.Text;

namespace ZL.EdgeService.Utils
{
    /// <summary>
    /// MQTT Tool
    /// </summary>
    public class MqttTool
    {
        string ServerIp;
        int ServerPort;
        string ClientId;
        string UserName;
        string Password;
        bool CleanSession;

        public MqttClient mqtt_client;
        public MqttTool(string serverIp, int serverPort, string clientId, string userName, string password, bool cleanSession)
        {
            ServerIp = serverIp;
            ServerPort = serverPort;
            ClientId = clientId;
            UserName = userName;
            Password = password;
            CleanSession = cleanSession;
        }

        public MqttClient Connect()
        {
            mqtt_client = new MqttClient(new MqttConnectionOptions
            {
                CleanSession = CleanSession,
                ClientId = ClientId,
                ConnectTimeout = 10,
                Credentials = new MqttCredential
                {
                    UserName = UserName,
                    Password = Password
                },
                IpAddress = ServerIp,
                Port = ServerPort,
                KeepAlivePeriod = TimeSpan.FromSeconds(60),
                KeepAliveSendInterval = TimeSpan.FromSeconds(30)
            });
            if (mqtt_client == null) return null;
            OperateResult result = mqtt_client.ConnectServer();
            if (!result.IsSuccess)
                throw new Exception($"Connect Fail：ErrorCode：{result.ErrorCode}，Message：{result.Message}");
            Console.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss}：MQTT DotNet Client Connected");
            return mqtt_client;
        }
        /// <summary>
        /// 连接MQTT Server
        /// </summary>
        /// <param name="serverIp">MQTT Server IP</param>
        /// <param name="serverPort">MQTT Server端口</param>
        /// <param name="clientId">客户端标识</param>
        /// <param name="userName">用户名</param>
        /// <param name="password">密码</param>
        /// <param name="cleanSession">是否清除会话</param>
        /// <returns>MqttClient连接对象</returns>
        public static MqttClient Connect(string serverIp, int serverPort, string clientId, string userName, string password, bool cleanSession)
        {
            MqttClient client = new MqttClient(new MqttConnectionOptions
            {
                CleanSession = cleanSession,
                ClientId = clientId,
                ConnectTimeout = 10,
                Credentials = new MqttCredential
                {
                    UserName = userName,
                    Password = password
                },
                IpAddress = serverIp,
                Port = serverPort,
                KeepAlivePeriod = TimeSpan.FromSeconds(60),
                KeepAliveSendInterval = TimeSpan.FromSeconds(30)
            });
            if (client == null) return null;
            OperateResult result = client.ConnectServer();
            if (!result.IsSuccess)
                throw new Exception($"Connect Fail：ErrorCode：{result.ErrorCode}，Message：{result.Message}");
            Console.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss}：MQTT DotNet Client Connected");
            return client;
        }

        /// <summary>
        /// 断开连接MQTT Server
        /// </summary>
        /// <param name="client">MqttClient连接对象</param>
        /// <returns></returns>
        public static void DisConnect(MqttClient client)
        {
            client?.ConnectClose();
            Console.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss}：MQTT DotNet Client DisConnected");
        }

        /// <summary>
        /// 订阅Topic
        /// </summary>
        /// <param name="client">MqttClient连接对象</param>
        /// <param name="topic">Topic</param>        
        /// <returns></returns>
        public static void Subscribe(MqttClient client, string topic)
        {
            if (client == null) return;
            OperateResult result = client.SubscribeMessage(topic);
            if (!result.IsSuccess)
                throw new Exception($"Subscribe Fail：ErrorCode：{result.ErrorCode}，Message：{result.Message}");
            Console.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss}：MQTT DotNet Client Subscribe Topic： {topic}");
        }

        /// <summary>
        /// 取消订阅Topic
        /// </summary>
        /// <param name="client">MqttClient连接对象</param>
        /// <param name="topic">Topic</param>
        /// <returns></returns>
        public static void UnSubscribe(MqttClient client, string topic)
        {
            OperateResult result = client.UnSubscribeMessage(topic);
            if (!result.IsSuccess)
                throw new Exception($"UnSubscribe Fail：ErrorCode：{result.ErrorCode}，Message：{result.Message}");
            Console.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss}：MQTT DotNet Client UnSubscribe Topic： {topic}");
        }

        /// <summary>
        /// 从订阅的Topic接收消息
        /// </summary>
        /// <param name="client">MqttClient连接对象</param>
        /// <param name="topic">Topic</param>
        /// <returns></returns>
        public static void ReceivedMessage(MqttClient client)
        {
            if (client == null) return;
            client.OnMqttMessageReceived += (MqttClient _client, string topic, byte[] payload) =>
            {
                Console.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss}：MQTT DotNet Client Recieved Message From Topic：{topic}\n+{Encoding.UTF8.GetString(payload)}");
            };
        }

        /// <summary>
        /// 发送消息
        /// </summary>
        /// <param name="client">MqttClient连接对象</param>
        /// <param name="level">消息级别</param>
        /// <param name="topic">Topic</param>
        /// <param name="messageContent">消息内容</param>
        /// <returns></returns>
        public static void SendMessage(MqttClient client, MqttQualityOfServiceLevel level, string topic, string messageContent)
        {
            //如果参数不合法，则不发送消息
            if (client == null || string.IsNullOrEmpty(topic) || string.IsNullOrEmpty(messageContent)) return;
            OperateResult result = client.PublishMessage(
            new MqttApplicationMessage()
            {
                Topic = topic,
                QualityOfServiceLevel = level,
                Payload = Encoding.UTF8.GetBytes(messageContent)
            });
            if (!result.IsSuccess)
                throw new Exception($"Send Message Fail：ErrorCode：{result.ErrorCode}，Message：{result.Message}");
            Console.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss}：MQTT DotNet Client Send Message To Topic：{topic}\n+{messageContent}");
        }
        /// <summary>
        /// 发送消息
        /// </summary>
        /// <param name="client">MqttClient连接对象</param>
        /// <param name="level">消息级别</param>
        /// <param name="topic">Topic</param>
        /// <param name="messageContent">消息内容</param>
        /// <returns></returns>
        public static void SendMessage(MqttClient client, string topic, string messageContent, MqttQualityOfServiceLevel level = MqttQualityOfServiceLevel.AtLeastOnce)
        {
            //如果参数不合法，则不发送消息
            if (client == null || string.IsNullOrEmpty(topic) || string.IsNullOrEmpty(messageContent)) return;
            OperateResult result = client.PublishMessage(
            new MqttApplicationMessage()
            {
                Topic = topic,
                QualityOfServiceLevel = level,
                Payload = Encoding.UTF8.GetBytes(messageContent)
            });
            if (!result.IsSuccess)
                throw new Exception($"Send Message Fail：ErrorCode：{result.ErrorCode}，Message：{result.Message}");
            Console.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss}：MQTT DotNet Client Send Message To Topic：{topic}\n+{messageContent}");
        }
    }
}

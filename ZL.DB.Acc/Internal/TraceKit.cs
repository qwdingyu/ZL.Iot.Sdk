using System;
using System.Net;
using System.Net.Sockets;

namespace ZL.DB.Acc.Utils
{
    public static class TraceKit
    {
        private static IPEndPoint REMOTE_EP;
        static TraceKit()
        {
            try
            {
                System.Collections.Specialized.NameValueCollection appSetting = System.Configuration.ConfigurationManager.AppSettings;

                REMOTE_EP = new System.Net.IPEndPoint(System.Net.Dns.GetHostAddresses(appSetting["TraceIp"])[0],
                    int.Parse(appSetting["TracePort"]));
            }
            catch
            {
                REMOTE_EP = new System.Net.IPEndPoint(System.Net.Dns.GetHostAddresses("127.0.0.1")[0], 2012); ;
            }
        }

        public static void SendText(string format, params object[] args)
        {
            string s = string.Format(format, args);
            SendText(s);
        }
        public static void SendText(string text)
        {
            SendText(REMOTE_EP, text);
        }

        public static void SendText(IPEndPoint remoteIP, string text)
        {
            if (remoteIP == null || string.IsNullOrEmpty(text))
                return;

            string msg = text;
            byte[] buffer = System.Text.Encoding.Default.GetBytes(msg);

            Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            SendState sendState = new SendState();
            sendState.Socket = socket;
            socket.BeginSendTo(buffer, 0, buffer.Length, SocketFlags.None, remoteIP, new AsyncCallback(UdpSendCallback), sendState);
        }

        private static void UdpSendCallback(IAsyncResult ar)
        {
            SendState sendState = (SendState)ar.AsyncState;
            Socket socket = sendState.Socket;
            try
            {
                socket.EndSendTo(ar);
                socket.Close();
            }
            catch (SocketException)
            {
                socket.Close();
            }
            catch (Exception e)
            {
                throw e;
            }
        }

        internal class SendState
        {
            private Socket _Socket;

            public SendState()
            {
            }
            public Socket Socket { get { return _Socket; } set { _Socket = value; } }
        }
    }
}
using System;
using ZL.PFLite.Auth;
using ZL.PFLite.Common;

namespace ZL.Iot.Interface
{
    public class AuthKit
    {

        private static SoftAuthorize softAuthorize = new SoftAuthorize();


        public static bool CheckAppAuth()
        {
            bool authOK = false;
            System.Collections.Specialized.NameValueCollection appSetting = System.Configuration.ConfigurationManager.AppSettings;
            string APP_SN = "";
            try
            {
                APP_SN = appSetting["APP_SN"];
                //authOK = softAuthorize.CheckAuthorize(APP_SN, SoftSecurity.AuthorizeEncrypted);
                authOK = softAuthorize.CheckAuthorize(APP_SN);
                if (!authOK)
                {
                    authOK = false;
                    LogKit.WriteAndTrace("授权检测失败，请联系系统管理员!机器码为：" + softAuthorize.GetMachineCodeString());
                }
            }
            catch (Exception ex)
            {
                // 记录异常信息以便排查，而不是简单忽略
                LogKit.WriteAndTrace($"授权检测异常: {ex.Message}");
                authOK = false;
            }
            return authOK;
        }
    }
}

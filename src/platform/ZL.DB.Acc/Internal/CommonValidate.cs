using System;

namespace ZL.DB.Acc.Utils
{
    /// <summary>
    /// Validate 的摘要说明。
    /// </summary>
    public class CommonValidate
    {
        public CommonValidate()
        {
            //
            // TODO: 在此处添加构造函数逻辑
            //
        }

        ///返回字符串的长度
        ///用来判断用户输入是否超出边界
        ///
        public static int GetStringLen(string str)
        {
            int len = 0;
            byte[] sarr = System.Text.Encoding.Default.GetBytes(str);
            len = sarr.Length;
            return len;
        }

        ///返回字符串的长度
        ///用来统计用户输入的个数
        ///

        public static int WordStatistical(string CString)
        {
            int digit = 0;
            for (int i = 0; i < CString.Length; i++)
            {
                if (Convert.ToInt32(Convert.ToChar(CString.Substring(i, 1))) < Convert.ToInt32(Convert.ToChar(128)))
                {
                    digit += 1;
                }
                else
                {
                    digit += 2;
                }
            }
            return digit;
        }

        public static string SqlValidateString(string CString)
        {

            string message = "";
            int index = -1;
            index = CString.IndexOf("\'");
            if (index > -1)
            {
                message = "字符串" + CString + "在第" + index.ToString() + "处存在非法字符“'”,请注意修改！";
                return message;
            }
            return message;

        }

        public static bool SqlValidateStringBool(string CString)
        {
            if (SqlValidateString(CString) == "")
                return true;
            else
                return false;
        }

        public static bool VINValidate(string sVIN, int len)
        {
            string[] sInvalid = { "I", "O", "Q" };
            if (len < 0)
                sVIN = sVIN.ToUpper();
            else
                sVIN = sVIN.Substring(0, len).ToUpper();
            bool bResult = false;
            for (int i = 0; i < sInvalid.GetLength(0); i++)
            {
                if (sVIN.IndexOf(sInvalid[i]) > 0)
                {
                    bResult = false;
                    break;
                }
                else
                {
                    bResult = true;
                }
            }
            return bResult;
        }

        public static bool VINValidate(string sVIN)
        {
            return VINValidate(sVIN, sVIN.Length);
        }



        /// <summary>
        /// 验证字符串str是否合法
        /// </summary>
        /// <param name="str">要验证的字符串</param>
        /// <param name="valType">验证类型</param>
        /// <returns>合法：true,不合法：false</returns>
        public static bool UserValidateBool(string str, ValidateType valType)
        {
            if (UserValidate(str, valType) == string.Empty)
                return true;
            else
                return false;
        }

        /// <summary>
        ///  验证字符串str是否合法
        /// </summary>
        /// <param name="str">要验证的字符串</param>
        /// <param name="valType">验证类型</param>
        /// <param name="stratPos">验证起始位</param>
        /// <param name="len">验证长度</param>
        /// <returns>合法：true,不合法：false</returns>
        public static bool UserValidateBool(string str, ValidateType valType, int stratPos, int len)
        {
            if (UserValidate(str, valType, stratPos, len) == string.Empty)
                return true;
            else
                return false;
        }

        /// <summary>
        /// 验证字符串str长度是否合法
        /// </summary>
        /// <param name="str">要验证的字符串</param>
        /// <param name="valtype">验证类型</param>
        /// <param name="defaultLength">默认长度</param>
        /// <returns>合法：true,不合法：false</returns>
        public static bool UserValidateBool(string str, ValidateType valtype, int defaultLength)
        {
            if (UserValidate(str, valtype, defaultLength) == string.Empty)
                return true;
            else
                return false;
        }

        /// <summary>
        /// 验证字符串str长度是否合法
        /// </summary>
        /// <param name="str">要验证的字符串</param>
        /// <param name="valtype">验证类型</param>
        /// <param name="defaultLength">默认长度</param>
        /// <param name="stratPos">验证起始位</param>
        /// <param name="len">验证长度</param>
        /// <returns>合法：true,不合法：false</returns>
        public static bool UserValidateBool(string str, ValidateType valType, int defaultLength, int startPos, int len)
        {
            if (UserValidate(str, valType, defaultLength, startPos, len) == string.Empty)
                return true;
            else
                return false;
        }

        //验证种类
        /// <summary>
        /// 验证字符串str是否合法
        /// </summary>
        /// <param name="str">要验证的字符串</param>
        /// <param name="valType">验证类型</param>
        /// <returns>合法：string.empty,不合法：提示字符串</returns>
        public static string UserValidate(string str, ValidateType valtype)
        {
            return UserValidate(str, valtype, int.MinValue, 0, str.Length);
        }

        /// <summary>
        ///  验证字符串str是否合法
        /// </summary>
        /// <param name="str">要验证的字符串</param>
        /// <param name="valType">验证类型</param>
        /// <param name="stratPos">验证起始位</param>
        /// <param name="len">验证长度</param>
        /// <returns>合法：string.empty,不合法：提示字符串</returns>
        public static string UserValidate(string str, ValidateType valtype, int startPos, int len)
        {
            return UserValidate(str, valtype, int.MinValue, startPos, len);
        }

        /// <summary>
        /// 验证字符串str长度是否合法
        /// </summary>
        /// <param name="str">要验证的字符串</param>
        /// <param name="valtype">验证类型</param>
        /// <param name="defaultLength">默认长度</param>
        /// <returns>合法：string.empty,不合法：提示字符串</returns>
        public static string UserValidate(string str, ValidateType valtype, int defaultLength)
        {
            return UserValidate(str, valtype, defaultLength, int.MinValue, int.MinValue);
        }

        /// <summary>
        /// 验证字符串str长度是否合法
        /// </summary>
        /// <param name="str">要验证的字符串</param>
        /// <param name="valtype">验证类型</param>
        /// <param name="defaultLength">默认长度</param>
        /// <param name="stratPos">验证起始位</param>
        /// <param name="len">验证长度</param>
        /// <returns>合法：string.empty,不合法：提示字符串</returns>
        public static string UserValidate(string str, ValidateType valtype, int defaultLength, int startPos, int len)
        {
            string result = string.Empty;
            switch (valtype)
            {
                case ValidateType.Common:
                    if (startPos != int.MinValue)
                        result = ValidateString(str.Substring(startPos, len), CommonValChars);
                    else
                        result = ValidateString(str, CommonValChars);
                    break;
                case ValidateType.VIN:
                    if (startPos != int.MinValue)
                        result = ValidateString(str.Substring(startPos, len), VinValChars);
                    else
                        result = ValidateString(str, VinValChars);
                    break;
                case ValidateType.LENGTH:
                    if (ValStrintLen(str, defaultLength) > 0)
                        result = "超长";
                    else
                        result = string.Empty;
                    break;
                default:
                    result = string.Empty;
                    break;
            }
            return result;
        }

        /// <summary>
        /// 验证字符串长度是否合法
        /// </summary>
        /// <param name="str">要验证的字符串</param>
        /// <param name="defaultLen">默认长度</param>
        /// <returns>字符串长度大于默认长度：1，字符串长度等于默认长度：0，字符串长度小于默认长度：-1</returns>
        public static int ValStrintLen(string str, int defaultLen)
        {
            if (GetStringLen(str) > defaultLen)
                return 1;
            if (GetStringLen(str) == defaultLen)
                return 0;
            if (GetStringLen(str) < defaultLen)
                return -1;
            return -1;
        }

        /// <summary>
        /// 验证字符串是否含有非法字符
        /// </summary>
        /// <param name="str">要验证的字符串</param>
        /// <param name="valStr">非法字符数组</param>
        /// <returns>合法：string.empty,不合法：提示字符串</returns>
        public static string ValidateString(string str, string[] valStr)
        {
            string result = str + "包含非法字符'";
            for (int i = 0; i < valStr.Length; i++)
            {
                if (str.IndexOf(valStr[i]) >= 0)
                    return result + valStr[i] + "'";
            }
            return string.Empty;
        }

        /// <summary>
        /// 通用非法字符数组
        /// </summary>
        public static string[] CommonValChars = { "\'" };
        /// <summary>
        /// VIN验证中的非法字符数组
        /// </summary>
        public static string[] VinValChars = { "I", "O", "Q" };

    }

    /// <summary>
    /// 验证类型枚举
    /// </summary>
    public enum ValidateType
    {
        /// <summary>
        /// 验证通用字符串
        /// </summary>
        Common = 1,
        /// <summary>
        /// 验证VIN字符串
        /// </summary>
        VIN = 2,
        /// <summary>
        /// 验证字符串长度
        /// </summary>
        LENGTH = 3
    }
}


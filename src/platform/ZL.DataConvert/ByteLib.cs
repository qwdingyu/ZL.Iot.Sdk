using System;


//注释说明


namespace ZL.DataConvert
{
    /// <summary>
    /// 单个字节转换库
    /// </summary>
    public class ByteLib
    {

        #region 截取某个字节
        /// <summary>
        /// 从字节数组中截取某个字节
        /// </summary>
        /// <param name="source"></param>
        /// <param name="start"></param>
        /// <returns></returns>
        public static byte GetByteFromByteArray(byte[] source, int start)
        {
            byte[] b = ByteArrayLib.GetByteArray(source, start, 1);

            return b == null ? (byte)0 : b[0];
        }
        #endregion

        #region 将字节中某个位赋值
        /// <summary>
        /// 将字节中的某个位赋值
        /// </summary>
        /// <param name="value">原始字节</param>
        /// <param name="bit">位</param>
        /// <param name="val">写入数值</param>
        /// <returns>返回字节</returns>
        public static byte SetbitValue(byte value, int bit, bool val)
        {
            return val ? (byte)(value | (byte)Math.Pow(2, bit)) : (byte)(value & (byte)~(byte)Math.Pow(2, bit));
        }
        #endregion
        /// <summary>
        /// 将8位的字符串 必须是Byte类型的有效表现形式
        /// </summary>
        /// <param name="txt">将8位的字符串</param>
        /// <returns></returns>
        //仅限于8位数的字符串转换
        //12345678 --> 128
        public static byte? BinStringToByte(string txt)
        {
            int cnt = 0;
            int ret = 0;

            if (txt.Length == 8)
            {
                for (cnt = 7; cnt >= 0; cnt += -1)
                {
                    if (int.Parse(txt.Substring(cnt, 1)) == 1)
                    {
                        ret += (int)(Math.Pow(2, (txt.Length - 1 - cnt)));
                    }
                }
                return (byte)ret;
            }
            return null;
        }
        ////参数是数值类型的字符串
        ////12345 --> 16
        //public static int BinStringToInt(string txt)
        //{
        //    int cnt = 0;
        //    int ret = 0;

        //    for (cnt = txt.Length - 1; cnt >= 0; cnt += -1)
        //    {
        //        if (int.Parse(txt.Substring(cnt, 1)) == 1)
        //        {
        //            ret += (int)(Math.Pow(2, (txt.Length - 1 - cnt)));
        //        }
        //    }
        //    return ret;
        //}
    }
}

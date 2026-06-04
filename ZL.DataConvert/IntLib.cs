using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;


//注释说明


namespace ZL.DataConvert
{
    /// <summary>
    /// Int类型转换库
    /// </summary>
    public class IntLib
    {
        #region 字节数组中截取转成32位整型
        /// <summary>
        /// 字节数组中截取转成32位整型
        /// </summary>
        /// <param name="source"></param>
        /// <param name="start"></param>
        /// <param name="type"></param>
        /// <returns></returns>
        public static int GetIntFromByteArray(byte[] source, int start = 0, DataFormat type = DataFormat.ABCD)
        {
            byte[] b = ByteArrayLib.Get4ByteArray(source, start, type);
            return b == null ? 0 : BitConverter.ToInt32(b, 0);
        }

        #endregion

        #region 将字节数组中截取转成32位整型数组
        /// <summary>
        /// 将字节数组中截取转成32位整型数组
        /// </summary>
        /// <param name="source"></param>
        /// <param name="type"></param>
        /// <returns></returns>
        public static int[] GetIntArrayFromByteArray(byte[] source, DataFormat type = DataFormat.ABCD)
        {
            int[] values = new int[source.Length / 4];

            for (int i = 0; i < source.Length / 4; i++)
            {
                values[i] = GetIntFromByteArray(source, 4 * i, type);
            }

            return values;
        }
        #endregion

        #region 将字符串转转成32位整型数组
        /// <summary>
        /// 将字符串转转成32位整型数组
        /// </summary>
        /// <param name="val"></param>
        /// <param name="spilt"></param>
        /// <returns></returns>
        public static int[] GetIntArrayFromString(string val, char spilt = ' ')
        {
            val = val.Trim();
            List<int> Result = new List<int>();
            if (val.Contains(spilt))
            {
                string[] str = val.Split(new char[] { spilt }, StringSplitOptions.RemoveEmptyEntries);

                foreach (var item in str)
                {
                    Result.Add(Convert.ToInt32(item.Trim()));
                }
            }
            else
            {
                Result.Add(Convert.ToInt32(val.Trim()));
            }

            return Result.ToArray();
        }
        #endregion
        /// <summary>
        /// 适用于一个字的字符串。转换成Int类型例如：T-->84(十进制) 同上   字符串转换成其对应的 Ascll码值
        /// </summary>
        /// <param name="character"></param>
        /// <returns>int 类型</returns>

        public static int Asc(string character)
        {
            if (character.Length == 1)
            {
                var asciiEncoding = new ASCIIEncoding();
                var intAsciiCode = (int)asciiEncoding.GetBytes(character)[0];
                return (intAsciiCode);
            }
            else
            {
                throw new Exception("Character is not valid.");
            }
        }

    }
}

using System;
using System.Collections.Generic;
using System.Linq;


//注释说明


namespace ZL.DataConvert
{
    /// <summary>
    /// Float类型转换库
    /// </summary>
    public class FloatLib
    {
        #region 字节数组中截取转成浮点型
        /// <summary>
        /// 将字节数组中某4个字节转换成Float类型
        /// </summary>
        /// <param name="source"></param>
        /// <param name="start"></param>
        /// <param name="type"></param>
        /// <returns></returns>
        public static float GetFloatFromByteArray(byte[] source, int start = 0, DataFormat type = DataFormat.ABCD)
        {
            byte[] b = ByteArrayLib.Get4ByteArray(source, start, type);
            return b == null ? 0.0f : BitConverter.ToSingle(b, 0);
        }
        #endregion

        #region 将字节数组中截取转成浮点型数组
        /// <summary>
        /// 将字节数组转换成Float数组
        /// </summary>
        /// <param name="source"></param>
        /// <param name="type"></param>
        /// <returns></returns>
        public static float[] GetFloatArrayFromByteArray(byte[] source, DataFormat type = DataFormat.ABCD)
        {
            float[] values = new float[source.Length / 4];

            for (int i = 0; i < source.Length / 4; i++)
            {
                values[i] = GetFloatFromByteArray(source, 4 * i, type);
            }

            return values;
        }
        #endregion

        #region 将字符串转换成浮点型数组
        /// <summary>
        /// 将Float字符串转换成单精度浮点型数组
        /// </summary>
        /// <param name="val">Float字符串</param>
        /// <param name="spilt">分隔符</param>
        /// <returns>单精度浮点型数组</returns>
        public static float[] GetFloatArrayFromString(string val, char spilt = ' ')
        {
            val = val.Trim();
            List<float> Result = new List<float>();
            if (val.Contains(spilt))
            {
                string[] str = val.Split(new char[] { spilt }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var item in str)
                {
                    Result.Add(Convert.ToSingle(item.Trim()));
                }
            }
            else
            {
                Result.Add(Convert.ToSingle(val.Trim()));
            }
            return Result.ToArray();
        }
        #endregion
        /// <summary>
        /// 16进制字符串转换成float类型
        /// </summary>
        /// <param name="str">十六进制的字符串</param>
        /// <returns></returns>
        //参数必须是16进制数值，转换为float类型，具体转换过程如下：
        //31-->49(十进制)--[49,0,0,0]-->6866362E-44
        public static float GetFloatFromHexString(string str)
        {
            var num = uint.Parse(str, System.Globalization.NumberStyles.AllowHexSpecifier);
            var floatVals = BitConverter.GetBytes(num);
            var f = BitConverter.ToSingle(floatVals, 0);
            return f;
        }
        /// <summary>
        /// 将uint数值转换成float类型
        /// </summary>
        /// <param name="number">uint数值</param>
        /// <returns></returns>
        //
        //99 --> [99,0,0,0] -->1.387285E-43
        public static float GetFloatFromUint(uint number)
        {
            var floatVals = BitConverter.GetBytes(number);
            var flo = BitConverter.ToSingle(floatVals, 0);
            return flo;
        }
        /// <summary>
        /// 将uint[]数值转换成List<float>类型
        /// </summary>
        /// <param name="number"></param>
        /// <returns></returns>
        //
        public static List<float> GetFloatArrayFromUintArrary(uint[] number)
        {
            var listByts = new List<byte[]>();
            var listFloats = new List<float>();
            for (var i = 0; i < number.Length; i++)
            {
                listByts.Add(BitConverter.GetBytes(number[i]));
                listFloats.Add(BitConverter.ToSingle(listByts[i], 0));
            }
            return listFloats;
        }
        //double[]类型转List<float>类型
        public static List<float> GetFloatFromDoubleArray(double[] number)
        {
            //var listByts = new List<byte[]>();
            var listFloats = new List<float>();
            for (var i = 0; i < number.Length; i++)
            {
                //listByts.Add(BitConverter.GetBytes((uint)number[i]));
                //listFloats.Add(BitConverter.ToSingle(listByts[i], 0));
                listFloats.Add(BitConverter.ToSingle(BitConverter.GetBytes((uint)number[i]), 0));
            }
            return listFloats;
        }

    }
}

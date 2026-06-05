using System;
using System.Collections.Generic;
using System.Text;


//注释说明


namespace ZL.DataConvert
{
    /// <summary>
    /// 字符串转换类
    /// </summary>
    public class StringLib
    {
        #region 将字节数组转换成字符串
        /// <summary>
        /// 将字节数组转等效16进制
        /// </summary>
        /// <param name="source"></param>
        /// <param name="start"></param>
        /// <param name="count"></param>
        /// <returns></returns>
        public static string GetStringFromByteArrayByBitConvert(byte[] source, int start, int count)
        {
            return BitConverter.ToString(source, start, count);
        }
        #endregion

        #region 将字节数组转换成带编码格式字符串
        /// <summary>
        /// 将字节数组转换成带编码格式字符串
        /// </summary>
        /// <param name="source"></param>
        /// <param name="start"></param>
        /// <param name="count"></param>
        /// <param name="encoding"></param>
        /// <returns></returns>
        public static string GetStringFromByteArray(byte[] source, int start, int count, Encoding encoding)
        {
            return encoding.GetString(ByteArrayLib.GetByteArray(source, start, count));
        }
        /// <summary>
        /// 将字节数组转换成带编码格式字符串
        /// </summary>
        /// <param name="source"></param>
        /// <param name="start"></param>
        /// <param name="count"></param>
        /// <returns></returns>
        public static string GetStringFromByteArray(byte[] source, int start, int count)
        {
            return Encoding.Default.GetString(ByteArrayLib.GetByteArray(source, start, count));
        }

        #endregion

        #region 将字节数组转换成带16进制字符串
        /// <summary>
        /// 将字节数组转换成带16进制字符串
        /// </summary>
        /// <param name="source"></param>
        /// <param name="start"></param>
        /// <param name="count"></param>
        /// <param name="segment"></param>
        /// <returns></returns>
        public static string GetHexStringFromByteArray(byte[] source, int start, int count, char segment = ' ')
        {
            byte[] b = ByteArrayLib.GetByteArray(source, start, count);

            StringBuilder sb = new StringBuilder();

            if (b.Length > 0)
            {
                foreach (var item in b)
                {
                    if (segment == 0) sb.Append(string.Format("{0:X2}", item));
                    else sb.Append(string.Format("{0:X2}{1}", item, segment));
                }
            }
            if (segment != 0 && sb.Length > 1 && sb[sb.Length - 1] == segment)
            {
                sb.Remove(sb.Length - 1, 1);
            }
            return sb.ToString();
        }

        /// <summary>
        /// 将字节数组转换成带16进制字符串
        /// </summary>
        /// <param name="source"></param>
        /// <param name="segment"></param>
        /// <returns></returns>
        public static string GetHexStringFromByteArray(byte[] source, char segment = ' ')
        {
            return GetHexStringFromByteArray(source, 0, source.Length, segment);
        }


        #endregion

        #region  将字节数组转换成西门子字符串
        /// <summary>
        /// 将字节数组转换成西门子字符串
        /// </summary>
        /// <param name="source"></param>
        /// <param name="start"></param>
        /// <param name="length"></param>
        /// <returns></returns>
        public static string GetSiemensStringFromByteArray(byte[] source, int start, int length)
        {
            byte[] b = ByteArrayLib.GetByteArray(source, start, length + 2);

            int valid = b[1];
            if (valid > 0)
            {
                return Encoding.GetEncoding("GBK").GetString(ByteArrayLib.GetByteArray(b, 2, valid));
            }
            else
            {
                return "empty";
            }
        }
        #endregion

        /// <summary>
        /// 布尔数组转换成字符串
        /// </summary>
        /// <param name="source"></param>
        /// <param name="IsTrueFormat"></param>
        /// <param name="segment"></param>
        /// <returns></returns>
        public static string GetStringFromBitArray(bool[] source, bool IsTrueFormat = true, char segment = ' ')
        {
            return GetStringFromBitArray(source, 0, source.Length, IsTrueFormat, segment);
        }


        /// <summary>
        /// 布尔数组转换成字符串
        /// </summary>
        /// <param name="source"></param>
        /// <param name="start"></param>
        /// <param name="count"></param>
        /// <param name="IsTrueFormat"></param>
        /// <param name="segment"></param>
        /// <returns></returns>
        public static string GetStringFromBitArray(bool[] source, int start, int count, bool IsTrueFormat = true, char segment = ' ')
        {
            bool[] b = BitLib.GetBitArray(source, start, count);
            StringBuilder sb = new StringBuilder();
            if (b.Length > 0)
            {
                foreach (bool item in b)
                {
                    if (segment == '\0')
                    {
                        if (IsTrueFormat)
                        {
                            sb.Append(item.ToString());
                        }
                        else
                        {
                            sb.Append(item ? "1" : "0");
                        }
                    }
                    else if (IsTrueFormat)
                    {
                        sb.Append(item.ToString() + segment.ToString());
                    }
                    else
                    {
                        sb.Append(item ? "1" : ("0" + segment.ToString()));
                    }
                }
            }
            if (((segment != '\0') && (sb.Length > 1)) && (sb[sb.Length - 1] == segment))
            {
                sb.Remove(sb.Length - 1, 1);
            }
            return sb.ToString();
        }

        /// <summary>
        /// 各种类型数组转换成字符串
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="source"></param>
        /// <param name="segment"></param>
        /// <returns></returns>
        public static string GetStringFromValueArray<T>(T[] source, char segment = ' ')
        {
            StringBuilder sb = new StringBuilder();
            if (source.Length > 0)
            {
                foreach (T item in source)
                {
                    if (segment == '\0')
                    {
                        sb.Append(item.ToString());
                    }
                    else
                    {
                        sb.Append(item.ToString() + segment.ToString());
                    }
                }
            }
            if (((segment != '\0') && (sb.Length > 1)) && (sb[sb.Length - 1] == segment))
            {
                sb.Remove(sb.Length - 1, 1);
            }
            return sb.ToString();
        }
        /// <summary>
        /// 字符串反转为char[]数组
        /// </summary>
        /// <param name="str"></param>
        /// <returns></returns>
        public static char[] StrReverse(string str)
        {
            var tmp = str.ToCharArray();
            Array.Reverse(tmp);
            return tmp;
        }
        /// <summary>
        /// 字符串截取，digits = 保留小数位数+1 1.23456789 --> 1.23456
        /// </summary>
        /// <param name="valStr"></param>
        /// <param name="digits">除去小数点后截取的数据长度</param>
        /// <returns></returns>
        //
        //主要用于截取浮点型的数据，第2个参数指除去小数点后截取的数据长度。
        //
        public static string StringRound(string valStr, int digits)
        {
            var res = string.Empty;
            var idx = valStr.IndexOf('.');
            var len = valStr.Length;
            if (idx > 0)
            {
                var roundLen = idx + (digits + 1);
                //要截取的长度大于总长度时，进行处理
                if (roundLen > len) digits = len - (idx);
                res = valStr.Substring(0, idx) + valStr.Substring(idx, digits);
            }
            else
            {
                res = valStr;
            }
            return res;
        }
        /// <summary>
        /// ASCII码转字符：
        /// </summary>
        /// <param name="asciiCode">ASCII码转字符</param>
        /// <returns></returns>
        //
        //将0到255之间的十进制数值转换成对于的字符。0 <= str1 <= 255
        //99 -- > C
        public static string GetStringFromASCII(int asciiCode)
        {
            if (asciiCode >= 0 && asciiCode <= 255)
            {
                var asciiEncoding = new ASCIIEncoding();
                var byteArray = new[] { (byte)asciiCode };
                var strCharacter = asciiEncoding.GetString(byteArray);
                return (strCharacter);
            }
            else
            {
                throw new Exception("ASCII Code is not valid.");
            }
        }
        /// <summary>
        /// 将多个Ascll码值 转换成字符串输出
        /// </summary>
        /// <param name="items">AscllZ值的数组 65, 66, 67, 68, 69--ABCDE</param>
        /// <param name="len"></param>
        /// <returns></returns>

        public static string GetStringFromASCII(Int16[] items, int len = 0)
        {
            var str = "";
            Int16[] tmp;
            if (items.Length > len && len != 0)
            {
                tmp = new Int16[len];
                Array.Copy(items, 0, tmp, 0, len);
            }
            else
                tmp = items;
            foreach (int item in tmp)
            {
                if (item < 0)
                {
                    str += "-";
                    str += Convert.ToChar(Math.Abs(item));
                }
                else
                {
                    str += Convert.ToChar(item);
                }

            }
            return str.Trim();
        }
        /// <summary>
        /// 将多个Ascll码值 转换成字符串输出
        /// </summary>
        /// <param name="items">Ascll码值的数组</param>
        /// <param name="begin">开始截取的索引</param>
        /// <param name="number">需要的字符的个数</param>
        /// <returns></returns>
        public static string GetStringFromASCII(Int16[] items, int begin, int number)
        {
            int n = begin;
            string str = "";
            for (int m = n; m < number + n; m++)
            {
                if (items[m] != 0)
                    str += Convert.ToChar(items[m]);
            }
            return str;
        }

        /// <summary>
        /// 质量数据转换
        /// </summary>
        /// <param name="data">Byte数组</param>
        /// <param name="DataType">数据类型(R或者I)，目前只兼容这两种数据类型转换</param>
        /// <param name="start">起始地址</param>
        /// <returns></returns>
        public static string GetStringByteToQty(byte[] data, string DataType, int start)
        {
            string res = string.Empty;
            int len = 0;
            switch (DataType)
            {
                case "R": len = start + 4; break;
                case "I": len = start + 2; break;
            }
            if (data.Length < len)
            {
                return res;
                throw new Exception("XML定义读取的字节数同数据转换的字节数不一致！");
            }
            if (DataType == "R")//一个REAL占用4个字节
            {
                //var cod = new byte[4] { data[idx], data[idx + 1], data[idx + 2], data[idx + 3] };
                //Reverse()函数需要使用.NetFrameWork 4.0框架，故将次注释
                //var v = cod.Reverse().ToArray();  //反序
                var v = new byte[4] { data[start + 3], data[start + 2], data[start + 1], data[start + 0] };
                res = BitConverter.ToSingle(v, 0).ToString("F6");
            }
            else if (DataType == "I")//一个INT占用2个字节
            {
                //byte[] bInt = { data[idx], data[idx + 1] };
                Int16 i16 = (short)((data[start] << 8) + data[start + 1]);
                res = i16.ToString();
            }
            return res;
        }
        /// <summary>
        /// Uint转换成浮点型字符串（保留3位小数）
        /// </summary>
        /// <param name="number"></param>
        /// <returns></returns>
        //double[]类型转List<string>类型
        public static List<string> GetListStringFromDoubleArray(double[] number)
        {
            var listFloats = new List<string>();
            foreach (var num in number)
            {
                var by = BitConverter.GetBytes((uint)num);
                var val = BitConverter.ToSingle(by, 0);
                var valStr = StringRound(val.ToString(), 4);
                listFloats.Add(valStr);
            }
            return listFloats;
        }

    }
}

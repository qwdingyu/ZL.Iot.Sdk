using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using ZL.DataConvert;

namespace ZL.Dao.Edge
{
    public class QtyUtil
    {
        public static string IsValid(string val)
        {
            if (string.IsNullOrEmpty(val) || val == "0" || val == "0.0")
            {
                return "*";
            }
            return "";
        }
        public static string DataConvert(string[] data, int index)
        {
            string res = string.Empty;
            res = data[index];
            return res;
        }

        public static Dictionary<string, int> TagDefaultDataSizeDic = new Dictionary<string, int>
            {
                {"1", 1},
                {"bool", 1},
                {"2", 0},
                {"bytes", 0},
                {"byte[]", 0},
                {"3", 1},
                {"byte", 1},
                {"4", 2},
                {"short", 2},
                {"I", 2}, // I 表达有歧义，但是为了兼容iot_qty_def之前的配置所以这么写，后期统一修改为 bool字面意思
                {"5", 2},
                {"word", 2},
                {"6", 4},
                {"dword", 4},
                {"7", 4},
                {"int", 4},
                {"8", 4},
                {"float", 4},
                {"R", 4},
                {"double", 4},
                {"11", 0},
                {"str", 0},
                {"string", 0}
            };
        /// <summary>
        /// 根据 数据类型获取  枚举类型
        /// </summary>
        /// <param name="data_type"></param>
        /// <returns></returns>
        public static int getDataSize(string data_type)
        {
            int size = 0;
            if (string.IsNullOrEmpty(data_type)) return size;
            if (!TagDefaultDataSizeDic.TryGetValue(data_type, out size))
            {
                size = 0;
            }
            return size;
        }
        public static string ByteToQty(string field, byte[] byteArray, string DataType, int start, int size = 0, int bit_index = 0)
        {
            string res = string.Empty;
            int len = 0;
            if (DataType == "1" || DataType == "bool")
            {
                size = 0;
                len = 1;
            }
            else
            {
                int defaultSize = getDataSize(DataType);
                // 针对 string， byte数组是必须要传递size的；其他类型如果不传则使用缺省长度
                if (defaultSize > 0)
                    size = defaultSize;
                len = start + size;
            }
            if (byteArray.Length < len)
            {
                return res;
                throw new Exception($"字段=【{field}】定义读取的字节数同数据转换的字节数不一致！");
            }
            char[] specialChars = { '\0', '\\', ' ' }; // 指定要去除的特殊字符
            switch (DataType)
            {
                case "1":
                case "bool":
                    res = BitLib.GetBitFromByteArray(byteArray, start, bit_index).ToString();
                    break;
                case "2":
                case "bytes":
                    res = ByteArrayLib.GetByteArray(byteArray, start, size).ToString();
                    break;
                case "3":
                case "byte":
                    res = byteArray[start].ToString();
                    break;
                case "4":
                case "short":
                case "I":
                    res = ShortLib.GetShortFromByteArray(byteArray, start).ToString();
                    break;
                case "ushort":
                    res = UShortLib.GetUShortFromByteArray(byteArray, start).ToString();
                    break;
                case "5":
                case "word":
                    //DB1.INT20 在kepware配置的类型为word
                    res = UShortLib.GetUShortFromByteArray(byteArray, start).ToString();
                    break;
                //case "6":
                //case "dword":
                //    res = DWor.GetShortFromByteArray(byteArray, start).ToString();
                //    break;
                case "7":
                case "int":
                    res = IntLib.GetIntFromByteArray(byteArray, start).ToString();
                    break;
                case "uint":
                    res = UIntLib.GetUIntFromByteArray(byteArray, start).ToString();
                    break;
                case "8":
                case "float":
                case "R":
                    res = FloatLib.GetFloatFromByteArray(byteArray, start).ToString();
                    break;
                case "double":
                    res = DoubleLib.GetDoubleFromByteArray(byteArray, start).ToString();
                    break;
                case "long":
                    res = LongLib.GetLongFromByteArray(byteArray, start).ToString();
                    break;
                case "ulong":
                    res = ULongLib.GetULongFromByteArray(byteArray, start).ToString();
                    break;
                case "11":
                case "str":
                case "string":
                    res = StringLib.GetStringFromByteArray(byteArray, start, size, Encoding.ASCII).Trim(specialChars);
                    break;
            }
            //if (DataType == "R")//一个REAL占用4个字节
            //{
            //    var cod = new byte[4] { data[idx], data[idx + 1], data[idx + 2], data[idx + 3] };
            //    var v = cod.Reverse().ToArray();  //反序
            //    //1.064987E-43 这种数据类型保留3位小数
            //    res = BitConverter.ToSingle(v, 0).ToString("F6");
            //}
            //else if (DataType == "I")//一个INT占用2个字节
            //{
            //    //byte[] bInt = { data[idx], data[idx + 1] };
            //    short v = (short)((data[idx] << 8) + data[idx + 1]);
            //    res = v.ToString();
            //}
            //else if (DataType == "B")
            //{
            //    res = data[idx].ToString();
            //}
            return res;
        }

        public static string Ascii2String(short[] items)
        {
            var str = "";
            foreach (int item in items)
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
            return str;
        }

        //string转byte类型
        public static byte int2Asc(string strInt)
        {
            byte tmp = 0; var asciiEncoding = new ASCIIEncoding();
            if (strInt == null)
                return tmp;
            if (strInt.Trim() != string.Empty && IsNumeric(strInt) && int.Parse(strInt) < int.MaxValue)
                tmp = byte.Parse(strInt);//单字节 值范围（0~255）
            else
                tmp = asciiEncoding.GetBytes(strInt.PadRight(1, char.MinValue))[0]; //单字符 值范围（ASCII）
            return tmp;
        }


        public static bool IsNumeric(string value)
        {
            return Regex.IsMatch(value, @"^[+-]?\d*[.]?d*$");
        }
    }
}

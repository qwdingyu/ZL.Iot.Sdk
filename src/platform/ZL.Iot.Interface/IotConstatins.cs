using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using ZL.PFLite.Common;

namespace ZL.Iot.Interface
{
    public class IotConstatins
    {
        // P1: 移除对 System.Configuration.ConfigurationManager.AppSettings 的依赖
        // 建议使用 Microsoft.Extensions.Configuration 或环境变量替代
        private static readonly System.Collections.Specialized.NameValueCollection _appSettings 
            = System.Configuration.ConfigurationManager.AppSettings;

        public static Dictionary<object, DataType> DataTypeMapping = new Dictionary<object, DataType>
            {
                // hsl demo中定义的方式 bool, short, ushort, int, uint, long, ulong, float, double, string, byte 
                // iot有，hsl没有 bytes
                //hsl有，iot没有 ushort uint long, ulong,
                {"none", DataType.NONE},
                {0, DataType.NONE},
                {"0", DataType.NONE},

                {"bool", DataType.BOOL},
                {1, DataType.BOOL},
                {"1", DataType.BOOL},

                {"bytes", DataType.BYTES},
                {"byte[]", DataType.BYTES},
                {2, DataType.BYTES},
                {"2", DataType.BYTES},

                {"byte", DataType.BYTE},
                {3, DataType.BYTE},
                {"3", DataType.BYTE},

                {"short", DataType.SHORT},
                {4, DataType.SHORT},
                {"4", DataType.SHORT},

                {"word", DataType.WORD},
                {5, DataType.WORD},
                {"5", DataType.WORD},

                {"dword", DataType.DWORD},
                {6, DataType.DWORD},
                {"6", DataType.DWORD},

                {"int", DataType.INT},
                {7, DataType.INT},
                {"7", DataType.INT},

                {"float", DataType.FLOAT},
                {8, DataType.FLOAT},
                {"8", DataType.FLOAT},

                {"double", DataType.DOUBLE},
                {9, DataType.DOUBLE},
                {"9", DataType.DOUBLE},

                {"str", DataType.STR},
                {"string", DataType.STR},
                {11, DataType.STR},
                {"11", DataType.STR},

                {"sys", DataType.SYS},
                {99, DataType.SYS},
                {"99", DataType.SYS }
            };

        /// <summary>
        /// 数据类型同数据长度的对应关系
        /// </summary>
        public static DataTable TagDataTypeSizeTab = new DataTable();
        /// <summary>
        /// 不再支持数字代表的类型，这样写不直观
        /// </summary>
        public static string[] NotSupportDataType = new string[] { "1", "2", "bytes", "3", "4", "5", "6", "7", "8", "9", "11", "str" };
        /// <summary>
        /// 不支持数据类型，转换为支持的类型
        /// </summary>
        public static Dictionary<string, string> DataTypeChgDic = new Dictionary<string, string> {
            { "1","bool" }
            , {"2","byte[]" }
            , {"bytes","byte[]" }
            ,{ "3","byte" }
            ,{ "4","short" }
            ,{ "5","word" }
            ,{ "6","dword" }
            , {"7","int" }
            , {"8","float" }
            , {"9","double" }
            , {"11","string" }
            , {"str","string" }
        };
        /// <summary>
        /// 数据类型缺省长度，长度为0表示“按需配置”
        /// </summary>
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

                {"5", 2},
                {"word", 2},

                {"6", 4},
                {"dword", 4},

                {"7", 4},
                {"int", 4},

                {"8", 4},
                {"float", 4},

                {"9", 4},
                {"double", 4},

                {"11", 0},
                {"str", 0},
                {"string", 0}
            };

        static IotConstatins()
        {
            TagDataTypeSizeTab = GetTagDataTypeSizeTab();
        }
        /// <summary>
        /// 根据 数据类型获取  枚举类型
        /// </summary>
        /// <param name="data_type"></param>
        /// <returns></returns>
        public static DataType getDataType(string tagId, string data_type)
        {
            DataType datatype = DataType.NONE;
            if (string.IsNullOrEmpty(data_type)) return datatype;
            if (NotSupportDataType.Contains(data_type))
                LogKit.WriteLogs($"标签id【{tagId}】数据类型【{data_type}】不再被支持！请使用【{getDataTypeChgDic(data_type)}】");
            //if (byte.TryParse(data_type, out byte byte_type))
            //{
            //    datatype = (DataType)byte_type;
            //}
            //else
            if (!DataTypeMapping.TryGetValue(data_type, out datatype))
            {
                datatype = DataType.NONE; // 这是一个可选步骤，确保在映射中找不到时，数据类型设置为NONE
            }
            if(datatype == DataType.NONE)
                LogKit.WriteLogs($"标签id【{tagId}】数据类型【{data_type}】非法，不是系统支持的数据类型！】");
            return datatype;
        }
        /// <summary>
        /// 不支持数据类型，转换为支持的类型
        /// </summary>
        /// <param name="data_type"></param>
        /// <returns></returns>
        public static string getDataTypeChgDic(string data_type)
        {
            if (DataTypeChgDic.TryGetValue(data_type, out string data_Type))
            {
                return data_Type;
            }
            return string.Empty;
        }
        /// <summary>
        /// 根据数据类型代码获取数据类型名称，用于标签显示
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        public static string GetDataType(string data_type)
        {
            if (NotSupportDataType.Contains(data_type))
                LogKit.WriteLogs($"数据类型【{data_type}】不再被支持！请使用【{getDataTypeChgDic(data_type)}】");
            if (DataTypeMapping.TryGetValue(data_type, out DataType data_Type))
            {
                //两种写法 返回的内容不一样，需要注意
                //((int)data_Type).ToString();// 5
                return data_Type.ToString();// DWORD
            }
            return string.Empty;
        }
        /// <summary>
        /// 获取固定长度的数据类型
        /// </summary>
        /// <returns></returns>
        public static string[] getFixDataSize()
        {
            var fixList = TagDefaultDataSizeDic.Where(it => it.Value > 0).Select(i => i.Key).ToArray();
            return fixList;
        }
        /// <summary>
        /// 专门为Frm_DeviceTag使用，用于SimulatorLite维护标签时进行参照使用，并无实际意义；
        /// </summary>
        /// <returns></returns>
        public static DataTable GetTagDataTypeSizeTab()
        {
            DataTable dt = new DataTable();
            dt.Columns.Add("data_type", System.Type.GetType("System.String"));//数据类型
            dt.Columns.Add("data_type_name", System.Type.GetType("System.String"));//类型名称
            dt.Columns.Add("data_lenght", System.Type.GetType("System.String"));//数据长度
            dt.Columns.Add("remark", System.Type.GetType("System.String"));//备注

            //dt.Rows.Add(new object[] { "0", "NONE", "0", "未知" });
            dt.Rows.Add(new object[] { "1", "BOOL", "1", "布尔" });
            dt.Rows.Add(new object[] { "2", "BYTES", "按需", "数组" });
            dt.Rows.Add(new object[] { "3", "BYTE", "1", "Byte" });
            dt.Rows.Add(new object[] { "4", "SHORT", "2", "Short" });
            dt.Rows.Add(new object[] { "5", "WORD", "2", "字" });
            dt.Rows.Add(new object[] { "6", "DWORD", "4", "双字" });
            dt.Rows.Add(new object[] { "7", "INT", "4", "整型" });
            dt.Rows.Add(new object[] { "8", "FLOAT", "4", "浮点型" });
            //dt.Rows.Add(new object[] { "9", "SYS", "1","系统" });
            dt.Rows.Add(new object[] { "11", "STR", "按需", "字符串" });
            return dt;
        }

        /// <summary>
        /// 获取缺省的数据类型
        /// </summary>
        /// <param name="data_type"></param>
        /// <returns></returns>
        public static int GetDefaultDataSizeByDataType(string data_type)
        {
            int _size = 0;
            if (TagDefaultDataSizeDic.ContainsKey(data_type))
                _size = TagDefaultDataSizeDic[data_type];
            return _size;
        }
        public static ushort GetDefaultDataSizeByDataType(DataType data_type, ushort cfgSize)
        {
            ushort _size = 0;
            //Type type = typeof(object);
            switch (data_type)
            {
                //整型   数据库配置 data_type：4	date_size：2	address：DB201,INT4
                case DataType.INT:
                    _size = 2;
                    //type = typeof(int);
                    break;
                //浮点型 数据库配置 data_type：8	date_size：4	address：DB201,14
                case DataType.FLOAT:
                case DataType.DOUBLE:
                    _size = 4;
                    //type = typeof(float);
                    break;
                default:
                    _size = cfgSize;
                    //type = typeof(object);
                    break;
            }
            return _size;
        }
    }
}

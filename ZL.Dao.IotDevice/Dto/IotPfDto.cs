namespace ZL.Dao.IotDevice
{
    public class IotPfDto
    {
        public string company_id { get; set; }
        public string plant_id { get; set; }
        public string line { get; set; }
        public string tag_id { get; set; }
        public string tag_name { get; set; }
        public string address { get; set; }        
        public string data_type { get; set; }
        public int list_order { get; set; }
        public int exe_order { get; set; }
        public string tag_type { get; set; }
        public string set_type { get; set; }
        public string preset { get; set; }
        public string info_type { get; set; }
        public object val { get; set; }
        /// <summary>
        /// 获取默认值
        /// </summary>
        /// <returns></returns>
        public object getDefalutValByDataType()
        {
            //默认值，清空下发地址中的值
            object keyVal = null;
            switch (data_type)
            {
                // 0 - 未知 - NONE
                //1 - 布尔 - BOOL
                //2 - 数组 - BYTES
                //3 - Byte - BYTE
                //4 - Short - SHORT
                //5 - 字 - WORD
                //6 - 双字 - DWORD
                //7 - 整型 - INT
                //8 - 浮点型 - FLOAT
                //9 - 系统 - SYS
                //11 - 字符串 - STR
                case "1":
                case "bool":
                    keyVal = false; break;
                case "2":
                case "bytes":
                case "byte[]":
                    keyVal = new byte[] { }; break;
                case "3":
                case "byte":
                    keyVal = 0; break;
                case "4":
                case "short":
                    keyVal = 0; break;
                case "5":
                case "word":
                    keyVal = 0; break;
                case "6":
                case "dword":
                    keyVal = ""; break;
                case "7":
                case "int":
                    keyVal = 0; break;
                case "8":
                case "float":
                    keyVal = 0; break;
                case "11":
                case "str":
                case "string":
                    keyVal = ""; break;
            }
            return keyVal;  
        }
    }
}

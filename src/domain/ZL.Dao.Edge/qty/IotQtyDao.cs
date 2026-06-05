using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using ZL.DB.Acc;
using ZL.PFLite;
using ZL.PFLite.Common;

namespace ZL.Dao.Edge
{
    public class IotQtyDao : DaoBase
    {
        static string[] NeedDataSize = new string[] { "2", "bytes", "byte[]", "11", "str", "string" };
        private static DataTable Dt_Iot_Qty = new DataTable("Pms_Qty");
        private static int Dt_Iot_Qty_Col_Count = 0;
        IotQtyDal iotQtyDal = new IotQtyDal();

        static IotQtyDao()
        {
            try
            {
                init();
            }
            catch (Exception ex)
            {
                LogKit.WriteLogs("数据库连接串提取错误，请检查配置文件！异常：" + ex.Message);
            }
        }

        /// <summary>
        /// 初始化质量数据DataTable
        /// </summary>
        public static void init()
        {
            Dt_Iot_Qty = new DataTable("Pms_Qty");
            //简写
            //Dt_Iot_Qty.Columns.Add("Xx", typeof(Int16));
            //Dt_Iot_Qty.Columns.Add("Xx", typeof(string));
            //Dt_Iot_Qty.Columns.Add("Xx", typeof(Boolean));
            Dt_Iot_Qty.Columns.Add("company_id", Type.GetType("System.String"));//公司
            Dt_Iot_Qty.Columns.Add("plant_id", Type.GetType("System.String"));//工厂
            Dt_Iot_Qty.Columns.Add("line", Type.GetType("System.String"));//线号
            Dt_Iot_Qty.Columns.Add("region_no", Type.GetType("System.String"));//区域
            Dt_Iot_Qty.Columns.Add("station_no", Type.GetType("System.String"));//工位
            //未考虑一个工位有多个设备的情况
            Dt_Iot_Qty.Columns.Add("sn", Type.GetType("System.String"));//产品流水号
            Dt_Iot_Qty.Columns.Add("psn", Type.GetType("System.String"));//父件号
            Dt_Iot_Qty.Columns.Add("data_type", Type.GetType("System.String"));//数据类型(R-浮点型(4个字节)、I-整型(2个字节))
            Dt_Iot_Qty.Columns.Add("qty_type", Type.GetType("System.String"));//质量数据类型：N拧紧 Y压装 C测量 S试漏 T图片
            Dt_Iot_Qty.Columns.Add("val", Type.GetType("System.String"));//采集值
            Dt_Iot_Qty.Columns.Add("val_field", Type.GetType("System.String"));//质量对应的字段
            Dt_Iot_Qty.Columns.Add("val_des", Type.GetType("System.String"));//数据描述
            Dt_Iot_Qty.Columns.Add("created_at", Type.GetType("System.DateTime"));
            Dt_Iot_Qty.Columns.Add("updated_at", Type.GetType("System.DateTime"));
            Dt_Iot_Qty.Columns.Add("remark", Type.GetType("System.String"));
            Dt_Iot_Qty_Col_Count = Dt_Iot_Qty.Columns.Count;
        }

        /// <summary>
        /// 复制一个新的DataTable
        /// </summary>
        /// <returns></returns>
        public DataTable NewPmsQty()
        {
            //清空数据，并复制
            Dt_Iot_Qty.Clear();
            return Dt_Iot_Qty.Copy();
        }
        /// <summary>
        /// 利用数据库的批量存储功能
        /// </summary>
        /// <param name="dataTable"></param>
        /// <returns></returns>
        public bool Save(DataTable dataTable)
        {
            bool ok = false;
            try
            {
                //利用数据库的批量存储功能
                string TableName = string.IsNullOrEmpty(dataTable.TableName) ? "pms_qty" : dataTable.TableName;
                //是否对所有的数据库都支持
                db.Fastest<DataTable>().AS(TableName).BulkCopy(dataTable);
                ok = true;
            }
            catch (Exception)
            {
                throw;
            }
            return ok;
        }

        public bool Save(pms_station_no ps, string tag_id, string sn, string psn, byte[] PlcByteData, int start_idx, string LogFile)
        {
            List<string> ListQty = new List<string>();
            bool ok = false;
            try
            {
                LogFile = string.IsNullOrEmpty(LogFile) ? ps.station_no : LogFile;
                if (string.IsNullOrEmpty(sn))
                {
                    LogKit.WriteLogs("产品条码为空！不能进行插入操作", LogFile);
                    return false;
                }
                if (PlcByteData.Length == 0)
                {
                    LogKit.WriteLogs("采集PLC质量数据长度为0！", LogFile);
                    return false;
                }
                pms_region_station p = new pms_region_station
                {
                    company_id = ps.company_id,
                    plant_id = ps.plant_id,
                    line = ps.line,
                    station_no = ps.station_no,
                    region_no = ""
                };
                var dt = GetQtyData(tag_id, p, sn, psn, PlcByteData, start_idx, LogFile, out ListQty);
                ok = Save(dt);
            }
            catch (Exception)
            {
                throw;
            }
            return ok;
        }
        public (int size, int bit_index) getSize(DataRow it)
        {
            string data_type = it["data_type"].ToString();
            int size = 1;
            int bit_index = 0;
            // 针对string类型为读取长度
            if (NeedDataSize.Contains(data_type))
                size = int.Parse(it["size"].ToString());
            else if (data_type == "1" || data_type == "bool")
                //针对bool类型为 byte的第几位
                bit_index = int.Parse(it["bit_index"].ToString());
            return (size, bit_index);
        }
        public DataTable GetQtyData(string tag_id, pms_region_station p, string sn, string psn, byte[] PlcByteData, int start_idx, string LogFile, out List<string> qtyList, bool debug = false)
        {
            DataTable qtyDt = NewPmsQty();
            qtyList = new List<string>();
            DataTable Dt_QtyDef = iotQtyDal.GetQtyDef(tag_id, start_idx);
            if (Dt_QtyDef == null || Dt_QtyDef.Rows.Count == 0)
                return qtyDt;
            try
            {
                string station_no = p.station_no;
                string RegionNo = p.region_no;
                var query = from t in Dt_QtyDef.Rows.Cast<DataRow>()
                            where t["station_no"]?.ToString() == p.station_no
                            select t;
                foreach (var it in query)
                {
                    string data_type = it["data_type"].ToString();
                    string qty_type = it["qty_type"].ToString();
                    string val_field = it["val_field"].ToString();
                    string val_des = it["val_des"].ToString();
                    string null_check = it["null_check"].ToString().ToLower();
                    int PlcAdr_Start = int.Parse(it["idx_add"].ToString());
                    (int size, int bit_index) = getSize(it);
                    string Val = QtyUtil.ByteToQty(val_field, PlcByteData, data_type, PlcAdr_Start, size, bit_index);
                    if (debug)
                    {
                        string msg = $"工位:【{station_no}】产品编号:【{sn}】 字段：【{val_field}】 数据名称:【{val_des}】 值:【{Val}】！";
                        LogKit.WriteLogs(msg, LogFile);
                    }
                    //创建object 存储数据
                    var drQty = new object[] { p.company_id, p.plant_id, p.line, p.region_no, p.station_no, sn, psn, data_type, qty_type, Val, val_field, val_des, DateTime.Now, DateTime.Now, "" };
                    string null_flag = "";
                    if (null_check == "true")
                    {
                        //val值为空的把remark标记为*
                        null_flag = QtyUtil.IsValid(Val);
                        drQty[Dt_Iot_Qty_Col_Count - 1] = null_flag;
                    }
                    qtyDt.Rows.Add(drQty);//将drQty数据塞到dt里面
                    qtyList.Add(Val);
                }
            }
            catch (Exception ex)
            {
                throw;
            }
            finally
            {
            }
            return qtyDt;
        }

        public ConcurrentDictionary<string, object> GetQtyDic(string tag_id, byte[] PlcByteData, string LogFile, bool debug = false)
        {
            ConcurrentDictionary<string, object> qtyDic = new ConcurrentDictionary<string, object>();
            int start_idx = iotQtyDal.GetQtyDefMinAdr(tag_id);
            DataTable Dt_QtyDef = iotQtyDal.GetQtyDef(tag_id, start_idx);
            if (Dt_QtyDef == null || Dt_QtyDef.Rows.Count == 0)
                return qtyDic;
            try
            {
                var query = from t in Dt_QtyDef.Rows.Cast<DataRow>() select t;
                foreach (var it in query)
                {
                    string station_no = it["station_no"].ToString();
                    string data_type = it["data_type"].ToString();
                    string qty_type = it["qty_type"].ToString();
                    string val_field = it["val_field"].ToString();
                    string val_des = it["val_des"].ToString();
                    string null_check = it["null_check"].ToString().ToLower();
                    int PlcAdr_Start = int.Parse(it["idx_add"].ToString());
                    (int size, int bit_index) = getSize(it);
                    string Val = QtyUtil.ByteToQty(val_field, PlcByteData, data_type, PlcAdr_Start, size, bit_index);
                    if (debug)
                        LogKit.WriteLogs($"工位:【{station_no}】 字段：【{val_field}】数据名称:【{val_des}】 值:【{Val}】！", LogFile);
                    //创建object 存储数据
                    if (!qtyDic.ContainsKey(val_field))
                        qtyDic.TryAdd(val_field, Val);
                }
            }
            catch (Exception ex)
            {
                throw;
            }
            finally
            {
            }
            return qtyDic;
        }
        /// <summary>
        /// 根据tag_id查找iot_qty_def中的配置，并解析byte数组，返回字典
        /// </summary>
        /// <param name="tag_id"></param>
        /// <param name="PlcByteData"></param>
        /// <param name="LogFile"></param>
        /// <param name="debug"></param>
        /// <returns></returns>
        public ConcurrentDictionary<string, SpValMisc> GetQtyDefDic(string tag_id, int addr_start, byte[] PlcByteData, string LogFile, bool debug = false)
        {
            ConcurrentDictionary<string, SpValMisc> qtyDic = new ConcurrentDictionary<string, SpValMisc>();
            if (addr_start == 0)
                addr_start = iotQtyDal.GetQtyDefMinAdr(tag_id);
            DataTable Dt_QtyDef = iotQtyDal.GetQtyDef(tag_id, addr_start);
            if (Dt_QtyDef == null || Dt_QtyDef.Rows.Count == 0)
                return qtyDic;
            try
            {
                var query = from t in Dt_QtyDef.Rows.Cast<DataRow>() select t;
                foreach (var it in query)
                {
                    string station_no = it["station_no"].ToString();
                    string data_type = it["data_type"].ToString();
                    string qty_type = it["qty_type"].ToString();
                    string val_field = it["val_field"].ToString();
                    string val_des = it["val_des"].ToString();
                    string null_check = it["null_check"].ToString().ToLower();
                    int PlcAdr_Start = int.Parse(it["idx_add"].ToString());
                    (int size, int bit_index) = getSize(it);
                    string Val = QtyUtil.ByteToQty(val_field, PlcByteData, data_type, PlcAdr_Start, size, bit_index);
                    if (debug)
                        LogKit.WriteLogs($"工位:【{station_no}】， 字段：【{val_field}】，数据类型【{data_type}】，size=【{size}】，bit_index=【{bit_index}】，数据名称:【{val_des}】 值:【{Val}】！", LogFile);
                    SpValMisc spVal = new SpValMisc { station_no = station_no, field = val_field, name = "", val = Val, desc = val_des };
                    //创建object 存储数据
                    if (!qtyDic.ContainsKey(val_field))
                        qtyDic.TryAdd(val_field, spVal);
                }
            }
            catch (Exception ex)
            {
                throw;
            }
            finally
            {
            }
            return qtyDic;
        }

        public DataTable ParseDataByTagId(string tag_id, byte[] PlcByteData, bool debug = false, string LogFile = "")
        {
            DataTable qtyDt = new DataTable("Pms_Qty");
            qtyDt.Columns.Add("station_no", Type.GetType("System.String"));//工位
            qtyDt.Columns.Add("data_type", Type.GetType("System.String"));//数据类型(R-浮点型(4个字节)、I-整型(2个字节))
            qtyDt.Columns.Add("qty_type", Type.GetType("System.String"));//质量数据类型：N拧紧 Y压装 C测量 S试漏 T图片
            qtyDt.Columns.Add("val_field", Type.GetType("System.String"));//质量对应的字段
            qtyDt.Columns.Add("val", Type.GetType("System.String"));//采集值
            qtyDt.Columns.Add("val_des", Type.GetType("System.String"));//数据描述
            qtyDt.Columns.Add("created_at", Type.GetType("System.DateTime"));
            int start_idx = iotQtyDal.GetQtyDefMinAdr(tag_id);
            DataTable Dt_QtyDef = iotQtyDal.GetQtyDef(tag_id, start_idx);
            if (Dt_QtyDef == null || Dt_QtyDef.Rows.Count == 0)
                return qtyDt;
            try
            {
                var query = from t in Dt_QtyDef.Rows.Cast<DataRow>() select t;
                foreach (var it in query)
                {
                    string station_no = it["station_no"].ToString();
                    string data_type = it["data_type"].ToString();
                    string qty_type = it["qty_type"].ToString();
                    string val_field = it["val_field"].ToString();
                    string val_des = it["val_des"].ToString();
                    string null_check = it["null_check"].ToString().ToLower();
                    int PlcAdr_Start = int.Parse(it["idx_add"].ToString());
                    (int size, int bit_index) = getSize(it);
                    string Val = QtyUtil.ByteToQty(val_field, PlcByteData, data_type, PlcAdr_Start, size, bit_index);
                    if (debug)
                        LogKit.WriteLogs($"工位:【{station_no}】 字段：【{val_field}】数据名称:【{val_des}】 值:【{Val}】！", LogFile);
                    //创建object 存储数据
                    var drQty = new object[] { station_no, data_type, qty_type, val_field, Val, val_des, DateTime.Now };

                    qtyDt.Rows.Add(drQty);//将drQty数据塞到dt里面
                }
            }
            catch (Exception ex)
            {
                throw;
            }
            finally
            {
            }
            return qtyDt;
        }

        /// <summary>
        /// 单质量数据存储
        /// </summary>
        /// <param name="company_id"></param>
        /// <param name="plant_id"></param>
        /// <param name="line"></param>
        /// <param name="region_no"></param>
        /// <param name="station_no"></param>
        /// <param name="sn"></param>
        /// <param name="valField"></param>
        /// <param name="valDes"></param>
        /// <param name="DataType"></param>
        /// <param name="qty_Type"></param>
        /// <param name="Val"></param>
        /// <param name="flag"></param>
        public void Add_Single_QtyData(pms_public p, string region_no, string station_no, string sn, string valField, string valDes, string DataType, string qty_Type, object Val, string flag)
        {
            string sql = string.Empty;
            sql = string.Format(@"INSERT INTO Pms_Qty (company_id, plant_id, line, region_no, station_no, psn, sn, val_field, val_des, data_type, qty_type, val, remark, created_at, created_by, updated_at, updated_by) VALUES 
('{0}', '{1}', '{2}', '{3}', '{4}', '{5}', '{6}', '{7}', '{8}', '{9}', '{10}', '{11}', '{12}', '{13}', 'admin', '{13}', 'admin')",
p.company_id, p.plant_id, p.line, region_no, station_no, sn, sn, valField, valDes, DataType, qty_Type, Val, flag, GetDateStr);
            Execute(sql);
        }

    }
}
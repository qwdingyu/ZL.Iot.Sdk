using System;
using System.Collections.Generic;
using System.Data;

namespace ZL.Dao.IotDevice
{
    public class DemoDataKit
    {
        /// <summary>
        /// 驱动信息
        /// </summary>
        /// <returns></returns>
        public static List<iot_driver> GenIotDriver()
        {
            List<iot_driver> _list = new List<iot_driver>();
            _list.Add(new iot_driver() { id = "1", assembly_name = "ZL.Plc.X.dll", class_name = "SiemensDriver", class_full_name = "ZL.Plc.X.SiemensDriver", description = "Siemens" });
            _list.Add(new iot_driver() { id = "2", assembly_name = "ZL.Plc.X.dll", class_name = "MitsubishiMcUdpDriver", class_full_name = "ZL.Plc.X.MitsubishiMcUdpDriver", description = "三菱" });
            _list.Add(new iot_driver() { id = "3", assembly_name = "ZL.Plc.X.dll", class_name = "OmronFinsTcpDriver", class_full_name = "ZL.Plc.X.OmronFinsTcpDriver", description = "OmronTcp" });
            _list.Add(new iot_driver() { id = "4", assembly_name = "ZL.Plc.X.dll", class_name = "OmronFinsUdpDriver", class_full_name = "ZL.Plc.X.OmronFinsUdpDriver", description = "OmronUdp" });
            _list.Add(new iot_driver() { id = "5", assembly_name = "ZL.Plc.X.dll", class_name = "ModbusTcpDriver", class_full_name = "ZL.Plc.X.ModbusTcpDriver", description = "ModbusTcp连接" });
            _list.Add(new iot_driver() { id = "6", assembly_name = "ZL.Plc.X.dll", class_name = "ModbusRtuDriver", class_full_name = "ZL.Plc.X.ModbusRtuDriver", description = "ModbusRtu连接" });
            return _list;
        }
        /// <summary>
        /// 设备类型信息
        /// </summary>
        /// <returns></returns>
        public static List<iot_device_type> GenIotDeviceType()
        {
            List<iot_device_type> _list = new List<iot_device_type>();
            _list.Add(new iot_device_type() { id = 1, type = "S7200", brand = "Siemens" });
            _list.Add(new iot_device_type() { id = 2, type = "S7200Smart", brand = "Siemens" });
            _list.Add(new iot_device_type() { id = 3, type = "S7300", brand = "Siemens" });
            _list.Add(new iot_device_type() { id = 4, type = "S7400", brand = "Siemens" });
            _list.Add(new iot_device_type() { id = 5, type = "S71200 ", brand = "Siemens" });
            _list.Add(new iot_device_type() { id = 6, type = "S71500", brand = "Siemens" });
            _list.Add(new iot_device_type() { id = 7, type = "Qna3E", brand = "Mitsubishi" });
            _list.Add(new iot_device_type() { id = 8, type = "Omron", brand = "Omron" });
            _list.Add(new iot_device_type() { id = 9, type = "S7200S", brand = "Siemens" });
            _list.Add(new iot_device_type() { id = 10, type = "ModBus", brand = "ModBus" });
            return _list;
        }

        /// <summary>
        /// 设备参数
        /// </summary>
        /// <returns></returns>
        public static List<iot_device_arg> GenIotDeviceArg()
        {
            List<iot_device_arg> _list = new List<iot_device_arg>();
            _list.Add(new iot_device_arg() { id = "49678955-29e5-49bd-bab9-1f8d209f0e7d", device_type_id = "1", pro_name = "Rack", pro_value = "0", description = "机架号" });
            _list.Add(new iot_device_arg() { id = "4eb0cd5b-9829-4f05-972a-5f088b0176b3", device_type_id = "1", pro_name = "Slot", pro_value = "1", description = "插槽号" });
            _list.Add(new iot_device_arg() { id = "65f595f8-137f-46fe-ab0e-2eac0eb74a87", device_type_id = "2", pro_name = "Rack", pro_value = "0", description = "机架号" });
            _list.Add(new iot_device_arg() { id = "1b44ffff-900c-4d1c-bf4e-b7f9729938d8", device_type_id = "2", pro_name = "Slot", pro_value = "0", description = "插槽号" });
            _list.Add(new iot_device_arg() { id = "7e85c091-bd14-479e-820b-33c9485e301a", device_type_id = "3", pro_name = "Rack", pro_value = "0", description = "机架号" });
            _list.Add(new iot_device_arg() { id = "0e7c6f21-f2fa-4329-a996-f248c417b929", device_type_id = "3", pro_name = "Slot", pro_value = "2", description = "插槽号" });
            _list.Add(new iot_device_arg() { id = "531d62c3-aaf1-40e0-96c9-e8566246eec7", device_type_id = "4", pro_name = "Rack", pro_value = "0", description = "机架号" });
            _list.Add(new iot_device_arg() { id = "877e8560-8030-4de8-a8eb-1a978ee9475f", device_type_id = "4", pro_name = "Slot", pro_value = "0", description = "插槽号" });
            _list.Add(new iot_device_arg() { id = "89f6841a-58df-4f04-9248-8a53aeb41df1", device_type_id = "5", pro_name = "Rack", pro_value = "0", description = "机架号" });
            _list.Add(new iot_device_arg() { id = "5ceb8147-7c18-4f72-856a-18715196a2a6", device_type_id = "5", pro_name = "Slot", pro_value = "0", description = "插槽号" });
            _list.Add(new iot_device_arg() { id = "4bf0aac8-63e6-4253-8733-4f1ad2a1a72d", device_type_id = "6", pro_name = "Rack", pro_value = "0", description = "机架号" });
            _list.Add(new iot_device_arg() { id = "60b7bec5-f31b-4189-8041-9d3dc6f5bee0", device_type_id = "6", pro_name = "Slot", pro_value = "0", description = "插槽号" });
            _list.Add(new iot_device_arg() { id = "fe7ae58a-9210-4fc6-856b-19927c9a4bad", device_type_id = "7", pro_name = "Rack", pro_value = "0", description = "机架号" });
            _list.Add(new iot_device_arg() { id = "d6ab6c74-da05-45f3-bdab-f0ce7b89e708", device_type_id = "7", pro_name = "Slot", pro_value = "0", description = "插槽号" });
            _list.Add(new iot_device_arg() { id = "8d2ad762-4cc6-4cf2-8d64-933757f119c6", device_type_id = "8", pro_name = "Port", pro_value = "9600", description = "端口号" });
            _list.Add(new iot_device_arg() { id = "097fc05d-7a4b-47f8-b585-355d0c2629a8", device_type_id = "8", pro_name = "DA2", pro_value = "0", description = "PLC的单元号" });
            _list.Add(new iot_device_arg() { id = "6c57eba5-ec78-4f3b-8ee0-6d2077e916b7", device_type_id = "8", pro_name = "DataFormat", pro_value = "CDAB", description = "数据格式" });
            _list.Add(new iot_device_arg() { id = "663fb532-595c-4e97-ae20-5fc9227694f7", device_type_id = "10", pro_name = "ServerName", pro_value = "COM3", description = "端口号" });
            _list.Add(new iot_device_arg() { id = "caf7745e-57e3-4496-aac1-08768943c99e", device_type_id = "10", pro_name = "BaudRate", pro_value = "9600", description = "波特率" });
            _list.Add(new iot_device_arg() { id = "52f917f8-5d03-4800-b073-73c664a3f384", device_type_id = "10", pro_name = "ParityBit", pro_value = "N", description = "校验位" });
            _list.Add(new iot_device_arg() { id = "4a58b964-ecf4-42a7-bd3f-7d846daab386", device_type_id = "10", pro_name = "DataBits", pro_value = "8", description = "数据位" });
            _list.Add(new iot_device_arg() { id = "bd392d01-96cd-4c8d-8b9c-eb5dcc84db37", device_type_id = "10", pro_name = "StopBits", pro_value = "1", description = "停止位" });
            return _list;
        }

        /// <summary>
        /// 设备品牌
        /// </summary>
        /// <returns></returns>
        public static List<iot_brand> GenIotBrand()
        {
            List<iot_brand> _list = new List<iot_brand>();
            _list.Add(new iot_brand() { id = 1, brand = "Modbus", brand_name = "Modbus", remark = "" });
            _list.Add(new iot_brand() { id = 2, brand = "Siemens", brand_name = "西门子", remark = "" });
            _list.Add(new iot_brand() { id = 3, brand = "Melsec", brand_name = "三菱", remark = "" });
            _list.Add(new iot_brand() { id = 4, brand = "Inovance", brand_name = "汇川", remark = "" });
            _list.Add(new iot_brand() { id = 5, brand = "Omron", brand_name = "欧姆龙", remark = "" });
            _list.Add(new iot_brand() { id = 6, brand = "LSis", brand_name = "LSis", remark = "" });
            _list.Add(new iot_brand() { id = 7, brand = "Keyence", brand_name = "基恩士", remark = "" });
            _list.Add(new iot_brand() { id = 8, brand = "Panasonic", brand_name = "松下", remark = "" });
            _list.Add(new iot_brand() { id = 9, brand = "AllenBrandly", brand_name = "罗克韦尔", remark = "" });
            _list.Add(new iot_brand() { id = 10, brand = "Beckhoff", brand_name = "倍福", remark = "" });
            _list.Add(new iot_brand() { id = 11, brand = "GE", brand_name = "通用", remark = "" });
            _list.Add(new iot_brand() { id = 12, brand = "Fatek", brand_name = "永宏", remark = "" });
            _list.Add(new iot_brand() { id = 13, brand = "Fuji", brand_name = "富士", remark = "" });
            _list.Add(new iot_brand() { id = 14, brand = "XinJE", brand_name = "信捷", remark = "" });
            _list.Add(new iot_brand() { id = 15, brand = "Yokogawa", brand_name = "横河", remark = "" });
            _list.Add(new iot_brand() { id = 16, brand = "Delta", brand_name = "台达", remark = "" });
            _list.Add(new iot_brand() { id = 17, brand = "KEPServerEX", brand_name = "KEPServerEX", remark = "" });
            _list.Add(new iot_brand() { id = 18, brand = "Efort", brand_name = "Efort", remark = "机器人" });
            _list.Add(new iot_brand() { id = 19, brand = "Kuka", brand_name = "库卡", remark = "机器人" });
            _list.Add(new iot_brand() { id = 20, brand = "YRC1000", brand_name = "安川", remark = "机器人" });
            _list.Add(new iot_brand() { id = 21, brand = "ABB", brand_name = "ABB", remark = "机器人" });
            _list.Add(new iot_brand() { id = 22, brand = "Fanuc", brand_name = "发那科", remark = "机器人" });
            _list.Add(new iot_brand() { id = 23, brand = "Hyundai", brand_name = "现代", remark = "机器人" });
            _list.Add(new iot_brand() { id = 24, brand = "YamahaRCX", brand_name = "雅马哈", remark = "机器人" });
            _list.Add(new iot_brand() { id = 25, brand = "Fanuc", brand_name = "Fanuc", remark = "数控机床" });
            _list.Add(new iot_brand() { id = 26, brand = "Vibration", brand_name = "捷克振动", remark = "传感器" });
            _list.Add(new iot_brand() { id = 27, brand = "Freedom", brand_name = "自由协议", remark = "自由协议" });
            _list.Add(new iot_brand() { id = 28, brand = "DAM3601", brand_name = "阿尔泰科技", remark = "仪器仪表" });
            _list.Add(new iot_brand() { id = 29, brand = "DLT645", brand_name = "电力规约", remark = "仪器仪表" });
            _list.Add(new iot_brand() { id = 30, brand = "GYKZQ", brand_name = "光源控制器", remark = "仪器仪表" });
            _list.Add(new iot_brand() { id = 31, brand = "DTSU6606", brand_name = "德力西电气", remark = "仪器仪表" });
            _list.Add(new iot_brand() { id = 32, brand = "Toledo", brand_name = "托利多", remark = "托利多" });
            _list.Add(new iot_brand() { id = 33, brand = "Special", brand_name = "特殊协议", remark = "Special特殊协议" });
            _list.Add(new iot_brand() { id = 34, brand = "OpcUa", brand_name = "OpcUa", remark = "OpcUa" });
            return _list;
        }

        /// <summary>
        /// 设备信息
        /// </summary>
        /// <returns></returns>
        public static List<iot_device> GenIotDevice()
        {
            List<iot_device> _list = new List<iot_device>();
            _list.Add(new iot_device() { id = "9", company_id = "e75094e6-ba7f-11ec-9557-000c299b8769", plant_id = "23", line = "123", region_no = "1", station_no = "OP90", driver_id = "1", device_name = "变壳压装机", is_active = 0, class_name = "", address = "127.0.0.1", device_type_id = 5, time_out = 3000, purpose = 3, remark = "", assembly_name = null });
            _list.Add(new iot_device() { id = "903", company_id = "e75094e6-ba7f-11ec-9557-000c299b8769", plant_id = "23", line = "123", region_no = "1", station_no = "OP20", driver_id = "1", device_name = "压装机", is_active = 0, class_name = "", address = "127.0.0.1", device_type_id = 5, time_out = 3000, purpose = 3, remark = "", assembly_name = null });

            return _list;
        }
        /// <summary>
        /// 设备和终端的对应关系信息
        /// </summary>
        /// <returns></returns>
        public static List<iot_edge_relation> GenIotDdgeRelation()
        {
            List<iot_edge_relation> _list = new List<iot_edge_relation>();
            _list.Add(new iot_edge_relation() { id = "cebbad66-ba80-11ec-9557-000c299b8769", edge_id = "cabbea14-ba7f-11ec-9557-000c299b8769", device_id = "9", created_at = DateTime.Now, created_by = "admin", updated_at = DateTime.Now, updated_by = "admin" });
            _list.Add(new iot_edge_relation() { id = "cebbae14-ba80-11ec-9557-000c299b8769", edge_id = "cabbea14-ba7f-11ec-9557-000c299b8769", device_id = "903", created_at = DateTime.Now, created_by = "admin", updated_at = DateTime.Now, updated_by = "admin" });

            return _list;
        }

        /// <summary>
        /// 设备标签
        /// </summary>
        /// <returns></returns>
        public static List<iot_tag> GenIotTag()
        {
            List<iot_tag> _list = new List<iot_tag>();
            //_list.Add(new iot_tag() { id = "1", device_id = "9", group_id = null, tag_name = "MES读数据为空", data_type = "1", data_size = 1, address = "DB99,X2.5", data_source = "MES", pid = "4", list_order = 0, tag_type = "W", set_type = "W", preset = "", info_type = "ReadNull", step = 1, pstep = 1, is_active = 1, archive = 0, cycle = 0, default_value = null, description = "MES读数据为空", su = 0, sl = 0, sv = 0, unit = null, value = "", updated_at = DateTime.Now, remark = "", tag_sub = null });
            //_list.Add(new iot_tag() { id = "2", device_id = "9", group_id = null, tag_name = "MES读完成", data_type = "1", data_size = 1, address = "DB99,X2.6", data_source = "MES", pid = "4", list_order = 999, tag_type = "W", set_type = "W", preset = "", info_type = "ReadOk", step = 1, pstep = 1, is_active = 1, archive = 0, cycle = 0, default_value = null, description = "MES读完成", su = 0, sl = 0, sv = 0, unit = null, value = "", updated_at = DateTime.Now, remark = "", tag_sub = null });
            //_list.Add(new iot_tag() { id = "3", device_id = "9", group_id = null, tag_name = "读产品流水号", data_type = "11", data_size = 20, address = "DB99,B72", data_source = "PLC", pid = "4", list_order = 1, tag_type = "R", set_type = "R", preset = "", info_type = "SN", step = 1, pstep = 1, is_active = 1, archive = 0, cycle = 0, default_value = null, description = "读产品流水号", su = 0, sl = 0, sv = 0, unit = null, value = "", updated_at = DateTime.Now, remark = "", tag_sub = null });
            //_list.Add(new iot_tag() { id = "4", device_id = "9", group_id = null, tag_name = "PLC请求上传质量数据", data_type = "1", data_size = 1, address = "DB99,X0.4", data_source = "PLC", pid = null, list_order = 0, tag_type = "M", set_type = "", preset = "", info_type = "Get", step = 1, pstep = 1, is_active = 1, archive = 0, cycle = 0, default_value = null, description = "PLC请求上传质量数据", su = 0, sl = 0, sv = 0, unit = null, value = "", updated_at = DateTime.Now, remark = "", tag_sub = null });
            //_list.Add(new iot_tag() { id = "5", device_id = "9", group_id = null, tag_name = "拧紧1力矩", data_type = "8", data_size = 4, address = "DB99,REAL102", data_source = "PLC", pid = "4", list_order = 3, tag_type = "R", set_type = "R", preset = "", info_type = "LJ1", step = 1, pstep = 1, is_active = 1, archive = 0, cycle = 0, default_value = null, description = "拧紧1力矩", su = 0, sl = 0, sv = 0, unit = null, value = "", updated_at = DateTime.Now, remark = "", tag_sub = null });
            //_list.Add(new iot_tag() { id = "6", device_id = "9", group_id = null, tag_name = "拧紧1角度", data_type = "8", data_size = 4, address = "DB99,REAL106", data_source = "PLC", pid = "4", list_order = 4, tag_type = "R", set_type = "R", preset = "", info_type = "JD1", step = 1, pstep = 1, is_active = 1, archive = 0, cycle = 0, default_value = null, description = "拧紧1角度", su = 0, sl = 0, sv = 0, unit = null, value = "", updated_at = DateTime.Now, remark = "", tag_sub = null });
            //_list.Add(new iot_tag() { id = "7", device_id = "9", group_id = null, tag_name = "拧紧2力矩", data_type = "8", data_size = 4, address = "DB99,REAL110", data_source = "PLC", pid = "4", list_order = 5, tag_type = "R", set_type = "R", preset = "", info_type = "LJ2", step = 1, pstep = 1, is_active = 1, archive = 0, cycle = 0, default_value = null, description = "拧紧2力矩", su = 0, sl = 0, sv = 0, unit = null, value = "", updated_at = DateTime.Now, remark = "", tag_sub = null });
            //_list.Add(new iot_tag() { id = "8", device_id = "9", group_id = null, tag_name = "拧紧2角度", data_type = "8", data_size = 4, address = "DB99,REAL114", data_source = "PLC", pid = "4", list_order = 6, tag_type = "R", set_type = "R", preset = "", info_type = "JD2", step = 1, pstep = 1, is_active = 1, archive = 0, cycle = 0, default_value = null, description = "拧紧2角度", su = 0, sl = 0, sv = 0, unit = null, value = "", updated_at = DateTime.Now, remark = "", tag_sub = null });
            //_list.Add(new iot_tag() { id = "9", device_id = "9", group_id = null, tag_name = "拧紧3力矩", data_type = "8", data_size = 4, address = "DB99,REAL118", data_source = "PLC", pid = "4", list_order = 7, tag_type = "R", set_type = "R", preset = "", info_type = "LJ3", step = 1, pstep = 1, is_active = 1, archive = 0, cycle = 0, default_value = null, description = "拧紧3力矩", su = 0, sl = 0, sv = 0, unit = null, value = "", updated_at = DateTime.Now, remark = "", tag_sub = null });
            //_list.Add(new iot_tag() { id = "10", device_id = "9", group_id = null, tag_name = "拧紧3角度", data_type = "8", data_size = 4, address = "DB99,REAL122", data_source = "PLC", pid = "4", list_order = 8, tag_type = "R", set_type = "R", preset = "", info_type = "JD3", step = 1, pstep = 1, is_active = 1, archive = 0, cycle = 0, default_value = null, description = "拧紧3角度", su = 0, sl = 0, sv = 0, unit = null, value = "", updated_at = DateTime.Now, remark = "", tag_sub = null });
            //_list.Add(new iot_tag() { id = "11", device_id = "9", group_id = null, tag_name = "轴高度", data_type = "4", data_size = 2, address = "DB99,INT100", data_source = "PLC", pid = "4", list_order = 2, tag_type = "R", set_type = "R", preset = "", info_type = "GD", step = 1, pstep = 1, is_active = 1, archive = 0, cycle = 0, default_value = null, description = "轴高度", su = 0, sl = 0, sv = 0, unit = null, value = "", updated_at = DateTime.Now, remark = "", tag_sub = null });
            //_list.Add(new iot_tag() { id = "12", device_id = "9", group_id = null, tag_name = "PLC请求采集批次", data_type = "1", data_size = 1, address = "DB99,X0.2", data_source = "PLC", pid = null, list_order = 0, tag_type = "M", set_type = "", preset = "", info_type = "Get", step = 1, pstep = 1, is_active = 1, archive = 0, cycle = 0, default_value = null, description = "PLC请求采集批次", su = 0, sl = 0, sv = 0, unit = null, value = "", updated_at = DateTime.Now, remark = "", tag_sub = null });
            //_list.Add(new iot_tag() { id = "13", device_id = "9", group_id = null, tag_name = "产品SN", data_type = "11", data_size = 30, address = "DB99,B800", data_source = "MES", pid = "12", list_order = 1, tag_type = "R", set_type = "", preset = "", info_type = "SN", step = 1, pstep = 1, is_active = 1, archive = 0, cycle = 0, default_value = null, description = "产品SN", su = 0, sl = 0, sv = 0, unit = null, value = "", updated_at = DateTime.Now, remark = "", tag_sub = null });
            //_list.Add(new iot_tag() { id = "14", device_id = "9", group_id = null, tag_name = "放油螺栓垫片批次码", data_type = "11", data_size = 20, address = "DB99,B830", data_source = "MES", pid = "12", list_order = 2, tag_type = "R", set_type = "", preset = "", info_type = "放油螺栓垫片", step = 1, pstep = 1, is_active = 1, archive = 0, cycle = 0, default_value = null, description = "放油螺栓垫片批次码", su = 0, sl = 0, sv = 0, unit = null, value = "", updated_at = DateTime.Now, remark = "", tag_sub = null });
            //_list.Add(new iot_tag() { id = "15", device_id = "9", group_id = null, tag_name = "放油螺栓批次码", data_type = "11", data_size = 20, address = "DB99,B860", data_source = "MES", pid = "12", list_order = 3, tag_type = "R", set_type = "", preset = "", info_type = "放油螺栓", step = 1, pstep = 1, is_active = 1, archive = 0, cycle = 0, default_value = null, description = "放油螺栓批次码", su = 0, sl = 0, sv = 0, unit = null, value = "", updated_at = DateTime.Now, remark = "", tag_sub = null });
            //_list.Add(new iot_tag() { id = "16", device_id = "9", group_id = null, tag_name = "输入输出油封批次码", data_type = "11", data_size = 20, address = "DB99,B890", data_source = "MES", pid = "12", list_order = 4, tag_type = "R", set_type = "", preset = "", info_type = "输入输出油封", step = 1, pstep = 1, is_active = 1, archive = 0, cycle = 0, default_value = null, description = "输入输出油封批次码", su = 0, sl = 0, sv = 0, unit = null, value = "", updated_at = DateTime.Now, remark = "", tag_sub = null });
            //_list.Add(new iot_tag() { id = "17", device_id = "9", group_id = null, tag_name = "MES读数据为空", data_type = "1", data_size = 1, address = "DB99,X3.5", data_source = "MES", pid = "12", list_order = 0, tag_type = "W", set_type = "", preset = "", info_type = "ReadNull", step = 1, pstep = 1, is_active = 1, archive = 0, cycle = 0, default_value = null, description = "MES读数据为空", su = 0, sl = 0, sv = 0, unit = null, value = "", updated_at = DateTime.Now, remark = "", tag_sub = null });
            //_list.Add(new iot_tag() { id = "18", device_id = "9", group_id = null, tag_name = "MES读完成", data_type = "1", data_size = 1, address = "DB99,X3.6", data_source = "MES", pid = "12", list_order = 999, tag_type = "W", set_type = "", preset = "", info_type = "ReadOk", step = 1, pstep = 1, is_active = 1, archive = 0, cycle = 0, default_value = null, description = "MES读完成", su = 0, sl = 0, sv = 0, unit = null, value = "", updated_at = DateTime.Now, remark = "", tag_sub = null });
            //_list.Add(new iot_tag() { id = "342", device_id = "9", group_id = null, tag_name = "BYTES数据", data_type = "2", data_size = 10, address = "DB99,1200", data_source = "MES", pid = "992", list_order = 8, tag_type = "W", set_type = "P", preset = "1234567890abc", info_type = "BYTE", step = 1, pstep = 1, is_active = 1, archive = 0, cycle = 0, default_value = null, description = "BYTES数据", su = 0, sl = 0, sv = 0, unit = null, value = "", updated_at = DateTime.Now, remark = "", tag_sub = null });
            //_list.Add(new iot_tag() { id = "879", device_id = "9", group_id = null, tag_name = "校准值", data_type = "8", data_size = 4, address = "DB99,1150", data_source = "MES", pid = "992", list_order = 7, tag_type = "W", set_type = "I", preset = "", info_type = "val", step = 1, pstep = 1, is_active = 1, archive = 0, cycle = 0, default_value = null, description = "校准值", su = 0, sl = 0, sv = 0, unit = null, value = "", updated_at = DateTime.Now, remark = "", tag_sub = null });
            //_list.Add(new iot_tag() { id = "991", device_id = "9", group_id = null, tag_name = "系列", data_type = "11", data_size = 20, address = "DB99,B1100", data_source = "MES", pid = "992", list_order = 5, tag_type = "W", set_type = "I", preset = "", info_type = "catena", step = 1, pstep = 1, is_active = 1, archive = 0, cycle = 0, default_value = null, description = "系列", su = 0, sl = 0, sv = 0, unit = null, value = "", updated_at = DateTime.Now, remark = "", tag_sub = null });
            //_list.Add(new iot_tag() { id = "992", device_id = "9", group_id = null, tag_name = "PLC请求下发配方", data_type = "1", data_size = 1, address = "DB99,X1000.2", data_source = "PLC", pid = null, list_order = 0, tag_type = "M", set_type = "", preset = "", info_type = "Set", step = 1, pstep = 1, is_active = 1, archive = 0, cycle = 0, default_value = null, description = "PLC请求下发配方", su = 0, sl = 0, sv = 0, unit = null, value = "", updated_at = DateTime.Now, remark = "", tag_sub = null });
            //_list.Add(new iot_tag() { id = "993", device_id = "9", group_id = null, tag_name = "产品SN", data_type = "11", data_size = 30, address = "DB99,B1010", data_source = "MES", pid = "992", list_order = 1, tag_type = "R", set_type = "", preset = "", info_type = "SN", step = 1, pstep = 1, is_active = 1, archive = 0, cycle = 0, default_value = null, description = "产品SN", su = 0, sl = 0, sv = 0, unit = null, value = "", updated_at = DateTime.Now, remark = "", tag_sub = null });
            //_list.Add(new iot_tag() { id = "994", device_id = "9", group_id = null, tag_name = "产品型号", data_type = "11", data_size = 20, address = "DB99,B1040", data_source = "MES", pid = "992", list_order = 2, tag_type = "W", set_type = "I", preset = "", info_type = "model", step = 1, pstep = 1, is_active = 1, archive = 0, cycle = 0, default_value = null, description = "产品型号", su = 0, sl = 0, sv = 0, unit = null, value = "", updated_at = DateTime.Now, remark = "", tag_sub = null });
            //_list.Add(new iot_tag() { id = "995", device_id = "9", group_id = null, tag_name = "短代码", data_type = "7", data_size = 2, address = "DB99,INT1060", data_source = "MES", pid = "992", list_order = 3, tag_type = "W", set_type = "I", preset = "", info_type = "short_code", step = 1, pstep = 1, is_active = 1, archive = 0, cycle = 0, default_value = null, description = "短代码", su = 0, sl = 0, sv = 0, unit = null, value = "", updated_at = DateTime.Now, remark = "", tag_sub = null });
            //_list.Add(new iot_tag() { id = "996", device_id = "9", group_id = null, tag_name = "订单号", data_type = "11", data_size = 20, address = "DB99,B1080", data_source = "MES", pid = "992", list_order = 4, tag_type = "W", set_type = "I", preset = "", info_type = "order_no", step = 1, pstep = 1, is_active = 1, archive = 0, cycle = 0, default_value = null, description = "订单号", su = 0, sl = 0, sv = 0, unit = null, value = "", updated_at = DateTime.Now, remark = "", tag_sub = null });
            //_list.Add(new iot_tag() { id = "997", device_id = "9", group_id = null, tag_name = "MES无数据", data_type = "1", data_size = 1, address = "DB99,X1003.5", data_source = "MES", pid = "992", list_order = 0, tag_type = "W", set_type = "", preset = "", info_type = "WriteNull", step = 1, pstep = 1, is_active = 1, archive = 0, cycle = 0, default_value = null, description = "MES无数据", su = 0, sl = 0, sv = 0, unit = null, value = "", updated_at = DateTime.Now, remark = "", tag_sub = null });
            //_list.Add(new iot_tag() { id = "998", device_id = "9", group_id = null, tag_name = "MES写完成", data_type = "1", data_size = 1, address = "DB99,X1003.6", data_source = "MES", pid = "992", list_order = 999, tag_type = "W", set_type = "", preset = "", info_type = "WriteOk", step = 1, pstep = 1, is_active = 1, archive = 0, cycle = 0, default_value = null, description = "MES写完成", su = 0, sl = 0, sv = 0, unit = null, value = "", updated_at = DateTime.Now, remark = "", tag_sub = null });
            //_list.Add(new iot_tag() { id = "999", device_id = "9", group_id = null, tag_name = "是否合格", data_type = "1", data_size = 1, address = "DB99,X1003.7", data_source = "MES", pid = "992", list_order = 6, tag_type = "W", set_type = "I", preset = "", info_type = "ok", step = 1, pstep = 1, is_active = 1, archive = 0, cycle = 0, default_value = null, description = "是否合格", su = 0, sl = 0, sv = 0, unit = null, value = "", updated_at = DateTime.Now, remark = "", tag_sub = null });
            //_list.Add(new iot_tag() { id = "1000", device_id = "9", group_id = null, tag_name = "PLC请求下发多工位配方", data_type = "1", data_size = 1, address = "DB99,X1300.2", data_source = "PLC", pid = null, list_order = 0, tag_type = "M", set_type = "", preset = "2", info_type = "Sets", step = 1, pstep = 1, is_active = 1, archive = 0, cycle = 0, default_value = null, description = "PLC请求下发多工位配方", su = 0, sl = 0, sv = 0, unit = null, value = null, updated_at = DateTime.Now, remark = null, tag_sub = null });
            //_list.Add(new iot_tag() { id = "1001", device_id = "9", group_id = null, tag_name = "产品SN", data_type = "11", data_size = 30, address = "DB99,B1310", data_source = "MES", pid = "1000", list_order = 0, tag_type = "R", set_type = null, preset = null, info_type = "SN", step = 1, pstep = 1, is_active = 1, archive = 0, cycle = 0, default_value = null, description = "产品SN", su = 0, sl = 0, sv = 0, unit = null, value = null, updated_at = DateTime.Now, remark = null, tag_sub = null });
            //_list.Add(new iot_tag() { id = "1002", device_id = "9", group_id = null, tag_name = "MES无数据", data_type = "1", data_size = 1, address = "DB99,X1303.5", data_source = "MES", pid = "1000", list_order = 0, tag_type = "W", set_type = null, preset = null, info_type = "WriteNull", step = 1, pstep = 1, is_active = 1, archive = 0, cycle = 0, default_value = null, description = "MES无数据", su = 0, sl = 0, sv = 0, unit = null, value = null, updated_at = DateTime.Now, remark = null, tag_sub = null });
            //_list.Add(new iot_tag() { id = "1003", device_id = "9", group_id = null, tag_name = "产品型号-OP10", data_type = "11", data_size = 20, address = "DB99,B1340", data_source = "MES", pid = "1000", list_order = 1, tag_type = "W", set_type = "I", preset = null, info_type = "model", step = 1, pstep = 1, is_active = 1, archive = 0, cycle = 0, default_value = null, description = "产品型号-OP10", su = 0, sl = 0, sv = 0, unit = null, value = null, updated_at = DateTime.Now, remark = null, tag_sub = null });
            //_list.Add(new iot_tag() { id = "1004", device_id = "9", group_id = null, tag_name = "短代码-OP10", data_type = "7", data_size = 2, address = "DB99,INT1360", data_source = "MES", pid = "1000", list_order = 2, tag_type = "W", set_type = "I", preset = null, info_type = "short_code", step = 1, pstep = 1, is_active = 1, archive = 0, cycle = 0, default_value = null, description = "短代码-OP10", su = 0, sl = 0, sv = 0, unit = null, value = null, updated_at = DateTime.Now, remark = null, tag_sub = null });
            //_list.Add(new iot_tag() { id = "1005", device_id = "9", group_id = null, tag_name = "产品型号-OP20", data_type = "11", data_size = 20, address = "DB99,B1370", data_source = "MES", pid = "1000", list_order = 3, tag_type = "W", set_type = "I", preset = null, info_type = "model", step = 1, pstep = 1, is_active = 1, archive = 0, cycle = 0, default_value = null, description = "产品型号-OP20", su = 0, sl = 0, sv = 0, unit = null, value = null, updated_at = DateTime.Now, remark = null, tag_sub = null });
            //_list.Add(new iot_tag() { id = "1006", device_id = "9", group_id = null, tag_name = "短代码-OP20", data_type = "7", data_size = 2, address = "DB99,INT1390", data_source = "MES", pid = "1000", list_order = 4, tag_type = "W", set_type = "I", preset = null, info_type = "short_code", step = 1, pstep = 1, is_active = 1, archive = 0, cycle = 0, default_value = null, description = "短代码-OP20", su = 0, sl = 0, sv = 0, unit = null, value = null, updated_at = DateTime.Now, remark = null, tag_sub = null });
            //_list.Add(new iot_tag() { id = "1007", device_id = "9", group_id = null, tag_name = "产品型号-OP30", data_type = "11", data_size = 20, address = "DB99,B1400", data_source = "MES", pid = "1000", list_order = 5, tag_type = "W", set_type = "I", preset = null, info_type = "model", step = 1, pstep = 1, is_active = 1, archive = 0, cycle = 0, default_value = null, description = "产品型号-OP30", su = 0, sl = 0, sv = 0, unit = null, value = null, updated_at = DateTime.Now, remark = null, tag_sub = null });
            //_list.Add(new iot_tag() { id = "1008", device_id = "9", group_id = null, tag_name = "短代码-OP30", data_type = "7", data_size = 2, address = "DB99,INT1420", data_source = "MES", pid = "1000", list_order = 6, tag_type = "W", set_type = "I", preset = null, info_type = "short_code", step = 1, pstep = 1, is_active = 1, archive = 0, cycle = 0, default_value = null, description = "短代码-OP30", su = 0, sl = 0, sv = 0, unit = null, value = null, updated_at = DateTime.Now, remark = null, tag_sub = null });
            //_list.Add(new iot_tag() { id = "1009", device_id = "9", group_id = null, tag_name = "产品型号-OP40", data_type = "11", data_size = 20, address = "DB99,B1430", data_source = "MES", pid = "1000", list_order = 7, tag_type = "W", set_type = "I", preset = null, info_type = "model", step = 1, pstep = 1, is_active = 1, archive = 0, cycle = 0, default_value = null, description = "产品型号-OP40", su = 0, sl = 0, sv = 0, unit = null, value = null, updated_at = DateTime.Now, remark = null, tag_sub = null });
            //_list.Add(new iot_tag() { id = "1010", device_id = "9", group_id = null, tag_name = "短代码-OP40", data_type = "7", data_size = 2, address = "DB99,INT1450", data_source = "MES", pid = "1000", list_order = 8, tag_type = "W", set_type = "I", preset = null, info_type = "short_code", step = 1, pstep = 1, is_active = 1, archive = 0, cycle = 0, default_value = null, description = "短代码-OP40", su = 0, sl = 0, sv = 0, unit = null, value = null, updated_at = DateTime.Now, remark = null, tag_sub = null });
            //_list.Add(new iot_tag() { id = "1011", device_id = "9", group_id = null, tag_name = "MES写完成", data_type = "1", data_size = 1, address = "DB99,X1303.6", data_source = "MES", pid = "1000", list_order = 999, tag_type = "W", set_type = null, preset = null, info_type = "WriteOk", step = 1, pstep = 1, is_active = 1, archive = 0, cycle = 0, default_value = null, description = "MES写完成", su = 0, sl = 0, sv = 0, unit = null, value = null, updated_at = DateTime.Now, remark = null, tag_sub = null });
            //_list.Add(new iot_tag() { id = "10530", device_id = "9", group_id = null, tag_name = "MES心跳信号", data_type = "1", data_size = 1, address = "DB9,X2.0", data_source = "MES", pid = null, list_order = 0, tag_type = "HB", set_type = "", preset = "", info_type = "", step = 1, pstep = 1, is_active = 1, archive = 0, cycle = 0, default_value = null, description = "MES心跳信号", su = 0, sl = 0, sv = 0, unit = null, value = "", updated_at = DateTime.Now, remark = "", tag_sub = null });
            //_list.Add(new iot_tag() { id = "10531", device_id = "9", group_id = null, tag_name = "MES准备好", data_type = "1", data_size = 1, address = "DB9,X2.1", data_source = "MES", pid = null, list_order = 0, tag_type = "RO", set_type = "", preset = "", info_type = "", step = 1, pstep = 1, is_active = 1, archive = 0, cycle = 0, default_value = null, description = "MES准备好", su = 0, sl = 0, sv = 0, unit = null, value = "", updated_at = DateTime.Now, remark = "", tag_sub = null });
            //_list.Add(new iot_tag() { id = "10560", device_id = "903", group_id = null, tag_name = "PLC请求上传质量数据", data_type = "1", data_size = 1, address = "DB903,X0.4", data_source = "PLC", pid = null, list_order = 0, tag_type = "M", set_type = "", preset = "", info_type = "Get", step = 1, pstep = 1, is_active = 1, archive = 0, cycle = 0, default_value = null, description = "PLC请求上传质量数据", su = 0, sl = 0, sv = 0, unit = null, value = "", updated_at = DateTime.Now, remark = "", tag_sub = null });
            //_list.Add(new iot_tag() { id = "10561", device_id = "903", group_id = null, tag_name = "PLC请求采集批次", data_type = "1", data_size = 1, address = "DB903,X0.2", data_source = "PLC", pid = null, list_order = 0, tag_type = "M", set_type = "", preset = "", info_type = "Get", step = 1, pstep = 1, is_active = 1, archive = 0, cycle = 0, default_value = null, description = "PLC请求采集批次", su = 0, sl = 0, sv = 0, unit = null, value = "", updated_at = DateTime.Now, remark = "", tag_sub = null });
            //_list.Add(new iot_tag() { id = "10562", device_id = "903", group_id = null, tag_name = "MES心跳信号", data_type = "1", data_size = 1, address = "DB903,X2.0", data_source = "MES", pid = null, list_order = 0, tag_type = "HB", set_type = "", preset = "", info_type = "", step = 1, pstep = 1, is_active = 1, archive = 0, cycle = 0, default_value = null, description = "MES心跳信号", su = 0, sl = 0, sv = 0, unit = null, value = "", updated_at = DateTime.Now, remark = "", tag_sub = null });
            //_list.Add(new iot_tag() { id = "10563", device_id = "903", group_id = null, tag_name = "MES准备好", data_type = "1", data_size = 1, address = "DB903,X2.1", data_source = "MES", pid = null, list_order = 0, tag_type = "RO", set_type = "", preset = "", info_type = "", step = 1, pstep = 1, is_active = 1, archive = 0, cycle = 0, default_value = null, description = "MES准备好", su = 0, sl = 0, sv = 0, unit = null, value = "", updated_at = DateTime.Now, remark = "", tag_sub = null });
            //_list.Add(new iot_tag() { id = "10564", device_id = "903", group_id = null, tag_name = "MES读数据为空", data_type = "1", data_size = 1, address = "DB903,X2.5", data_source = "MES", pid = "10560", list_order = 0, tag_type = "W", set_type = "W", preset = "", info_type = "ReadNull", step = 1, pstep = 1, is_active = 1, archive = 0, cycle = 0, default_value = null, description = "MES读数据为空", su = 0, sl = 0, sv = 0, unit = null, value = "", updated_at = DateTime.Now, remark = "", tag_sub = null });
            //_list.Add(new iot_tag() { id = "10565", device_id = "903", group_id = null, tag_name = "MES读数据为空", data_type = "1", data_size = 1, address = "DB903,X3.5", data_source = "MES", pid = "10561", list_order = 0, tag_type = "W", set_type = "", preset = "", info_type = "ReadNull", step = 1, pstep = 1, is_active = 1, archive = 0, cycle = 0, default_value = null, description = "MES读数据为空", su = 0, sl = 0, sv = 0, unit = null, value = "", updated_at = DateTime.Now, remark = "", tag_sub = null });
            //_list.Add(new iot_tag() { id = "10566", device_id = "903", group_id = null, tag_name = "读产品流水号", data_type = "11", data_size = 20, address = "DB903,B72", data_source = "PLC", pid = "10560", list_order = 1, tag_type = "R", set_type = "R", preset = "", info_type = "SN", step = 1, pstep = 1, is_active = 1, archive = 0, cycle = 0, default_value = null, description = "读产品流水号", su = 0, sl = 0, sv = 0, unit = null, value = "", updated_at = DateTime.Now, remark = "", tag_sub = null });
            //_list.Add(new iot_tag() { id = "10567", device_id = "903", group_id = null, tag_name = "产品SN", data_type = "11", data_size = 30, address = "DB903,B800", data_source = "MES", pid = "10561", list_order = 1, tag_type = "R", set_type = "", preset = "", info_type = "SN", step = 1, pstep = 1, is_active = 1, archive = 0, cycle = 0, default_value = null, description = "产品SN", su = 0, sl = 0, sv = 0, unit = null, value = "", updated_at = DateTime.Now, remark = "", tag_sub = null });
            //_list.Add(new iot_tag() { id = "10568", device_id = "903", group_id = null, tag_name = "产品合格状态", data_type = "4", data_size = 2, address = "DB903,INT100", data_source = "PLC", pid = "10560", list_order = 2, tag_type = "R", set_type = "R", preset = "", info_type = "产品合格状态", step = 1, pstep = 1, is_active = 1, archive = 0, cycle = 0, default_value = null, description = "轴高度", su = 0, sl = 0, sv = 0, unit = null, value = "", updated_at = DateTime.Now, remark = "", tag_sub = null });
            //_list.Add(new iot_tag() { id = "10569", device_id = "903", group_id = null, tag_name = "中间轴批次码", data_type = "11", data_size = 20, address = "DB903,B830", data_source = "MES", pid = "10561", list_order = 2, tag_type = "R", set_type = "", preset = "", info_type = "中间轴批次码", step = 1, pstep = 1, is_active = 1, archive = 0, cycle = 0, default_value = null, description = "放油螺栓垫片批次码", su = 0, sl = 0, sv = 0, unit = null, value = "", updated_at = DateTime.Now, remark = "", tag_sub = null });
            //_list.Add(new iot_tag() { id = "10570", device_id = "903", group_id = null, tag_name = "压中间轴位移值", data_type = "8", data_size = 4, address = "DB903,REAL102", data_source = "PLC", pid = "10560", list_order = 3, tag_type = "R", set_type = "R", preset = "", info_type = "压中间轴位移值", step = 1, pstep = 1, is_active = 1, archive = 0, cycle = 0, default_value = null, description = "拧紧1力矩", su = 0, sl = 0, sv = 0, unit = null, value = "", updated_at = DateTime.Now, remark = "", tag_sub = null });
            //_list.Add(new iot_tag() { id = "10571", device_id = "903", group_id = null, tag_name = "输入轴批次码", data_type = "11", data_size = 20, address = "DB903,B860", data_source = "MES", pid = "10561", list_order = 3, tag_type = "R", set_type = "", preset = "", info_type = "输入轴批次码", step = 1, pstep = 1, is_active = 1, archive = 0, cycle = 0, default_value = null, description = "放油螺栓批次码", su = 0, sl = 0, sv = 0, unit = null, value = "", updated_at = DateTime.Now, remark = "", tag_sub = null });
            //_list.Add(new iot_tag() { id = "10572", device_id = "903", group_id = null, tag_name = "压中间轴压力值", data_type = "8", data_size = 4, address = "DB903,REAL106", data_source = "PLC", pid = "10560", list_order = 4, tag_type = "R", set_type = "R", preset = "", info_type = "压中间轴压力值", step = 1, pstep = 1, is_active = 1, archive = 0, cycle = 0, default_value = null, description = "拧紧1角度", su = 0, sl = 0, sv = 0, unit = null, value = "", updated_at = DateTime.Now, remark = "", tag_sub = null });
            //_list.Add(new iot_tag() { id = "10573", device_id = "903", group_id = null, tag_name = "内油封批次码", data_type = "11", data_size = 20, address = "DB903,B890", data_source = "MES", pid = "10561", list_order = 4, tag_type = "R", set_type = "", preset = "", info_type = "内油封批次码", step = 1, pstep = 1, is_active = 1, archive = 0, cycle = 0, default_value = null, description = "输入输出油封批次码", su = 0, sl = 0, sv = 0, unit = null, value = "", updated_at = DateTime.Now, remark = "", tag_sub = null });
            //_list.Add(new iot_tag() { id = "10574", device_id = "903", group_id = null, tag_name = "压输入轴位移值", data_type = "8", data_size = 4, address = "DB903,REAL110", data_source = "PLC", pid = "10560", list_order = 5, tag_type = "R", set_type = "R", preset = "", info_type = "压输入轴位移值", step = 1, pstep = 1, is_active = 1, archive = 0, cycle = 0, default_value = null, description = "拧紧2力矩", su = 0, sl = 0, sv = 0, unit = null, value = "", updated_at = DateTime.Now, remark = "", tag_sub = null });
            //_list.Add(new iot_tag() { id = "10575", device_id = "903", group_id = null, tag_name = "压输入轴压力值", data_type = "8", data_size = 4, address = "DB903,REAL114", data_source = "PLC", pid = "10560", list_order = 6, tag_type = "R", set_type = "R", preset = "", info_type = "压输入轴压力值", step = 1, pstep = 1, is_active = 1, archive = 0, cycle = 0, default_value = null, description = "拧紧2角度", su = 0, sl = 0, sv = 0, unit = null, value = "", updated_at = DateTime.Now, remark = "", tag_sub = null });
            //_list.Add(new iot_tag() { id = "10576", device_id = "903", group_id = null, tag_name = "压内油封位移值", data_type = "8", data_size = 4, address = "DB903,REAL118", data_source = "PLC", pid = "10560", list_order = 7, tag_type = "R", set_type = "R", preset = "", info_type = "压内油封位移值", step = 1, pstep = 1, is_active = 1, archive = 0, cycle = 0, default_value = null, description = "拧紧3力矩", su = 0, sl = 0, sv = 0, unit = null, value = "", updated_at = DateTime.Now, remark = "", tag_sub = null });
            //_list.Add(new iot_tag() { id = "10577", device_id = "903", group_id = null, tag_name = "压内油封压力值", data_type = "8", data_size = 4, address = "DB903,REAL122", data_source = "PLC", pid = "10560", list_order = 8, tag_type = "R", set_type = "R", preset = "", info_type = "压内油封压力值", step = 1, pstep = 1, is_active = 1, archive = 0, cycle = 0, default_value = null, description = "拧紧3角度", su = 0, sl = 0, sv = 0, unit = null, value = "", updated_at = DateTime.Now, remark = "", tag_sub = null });
            //_list.Add(new iot_tag() { id = "10578", device_id = "903", group_id = null, tag_name = "MES读完成", data_type = "1", data_size = 1, address = "DB903,X2.6", data_source = "MES", pid = "10560", list_order = 999, tag_type = "W", set_type = "W", preset = "", info_type = "ReadOk", step = 1, pstep = 1, is_active = 1, archive = 0, cycle = 0, default_value = null, description = "MES读完成", su = 0, sl = 0, sv = 0, unit = null, value = "", updated_at = DateTime.Now, remark = "", tag_sub = null });
            //_list.Add(new iot_tag() { id = "10579", device_id = "903", group_id = null, tag_name = "MES读完成", data_type = "1", data_size = 1, address = "DB903,X3.6", data_source = "MES", pid = "10561", list_order = 999, tag_type = "W", set_type = "", preset = "", info_type = "ReadOk", step = 1, pstep = 1, is_active = 1, archive = 0, cycle = 0, default_value = null, description = "MES读完成", su = 0, sl = 0, sv = 0, unit = null, value = "", updated_at = DateTime.Now, remark = "", tag_sub = null });

            return _list;
        }


        ///// <summary>
        ///// 设备动作执行
        ///// </summary>
        ///// <returns></returns>
        //public static List<iot_biz_mode> GenIotBizMode()
        //{
        //    List<iot_biz_mode> _list = new List<iot_biz_mode>();
        //    _list.Add(new iot_biz_mode() { id = "06d6c482-6f8d-11ec-9b3c-54ee75e6a623", list_order = 3, judge_type = "Q", judge_exp = "", exe_type = "Q", script = "insert into pms_plan_position(company_id , plant_id ,  line , station_no , sn ,part_sn , ok_mark, start_time , created_at , created_by)\r\nVALUES(\'?CompanyId?\',\'?PlantId?\',\'?Line?\',\'?StationNo?\',\'?SN?\',\'\', 1, NOW(), NOW(),\'sys\')", remark = null, created_at = DateTime.Now, created_by = null, updated_at = DateTime.Now, updated_by = null });
        //    _list.Add(new iot_biz_mode() { id = "d7c490bb-62e8-45c0-8459-ea6727e44f10", list_order = 1, judge_type = "Q", judge_exp = null, exe_type = "S", script = "SELECT\r\n  a.model,\r\n  a.order_no,\r\n b.short_code,\r\n b.catena,\r\n \'True\' AS ok ,\r\n  \'3.456\' as val,\r\n \'123456\' AS BYTE\r\nFROM\r\n  (\r\n SELECT\r\n    company_id,\r\n   plant_id,\r\n   line,\r\n   model,\r\n    sn,\r\n   order_no \r\n FROM\r\n    pms_plan \r\n WHERE\r\n   company_id = \'?CompanyId?\' \r\n   AND plant_id = \'?PlantId?\' \r\n   AND line = \'?Line?\' \r\n    AND SN = \'?SN?\' \r\n  ) a\r\n LEFT JOIN bom_model b ON a.company_id = b.company_id \r\n AND a.plant_id = b.plant_id \r\n  AND a.line = b.line \r\n  AND a.model = b.model", remark = null, created_at = DateTime.Now, created_by = null, updated_at = DateTime.Now, updated_by = null });
        //    _list.Add(new iot_biz_mode() { id = "247d0906-2d43-433b-8404-b0e71051f5b1", list_order = 1, judge_type = "Q", judge_exp = null, exe_type = "S", script = "SELECT \'AAA\' as model, 1 as short_code union all\r\nSELECT \'BBB\' as model, 2 as short_code union all\r\nSELECT \'CCC\' as model, 3 as short_code union all\r\nSELECT \'DDD\' as model, 4 as short_code ", remark = null, created_at = DateTime.Now, created_by = null, updated_at = DateTime.Now, updated_by = null });
        //    _list.Add(new iot_biz_mode() { id = "45ed2c3e-6f8d-11ec-9b3c-54ee75e6a623", list_order = 1, judge_type = "Q", judge_exp = "", exe_type = "Q", script = "insert into pms_compqrcode ( company_id, plant_id, line, station_no, sn, type, qrcode, remark, created_at, created_by,updated_at, updated_by)\r\nVALUES(\'?CompanyId?\',\'?PlantId?\',\'?Line?\',\'?StationNo?\',\'?SN?\',\'SKT\',\'?中间轴批次码?\', \'@中间轴批次码@\',NOW(),\'sys\', NOW(),\'sys\');", remark = "", created_at = DateTime.Now, created_by = null, updated_at = DateTime.Now, updated_by = null });
        //    _list.Add(new iot_biz_mode() { id = "45ed2cec-6f8d-11ec-9b3c-54ee75e6a623", list_order = 2, judge_type = "Q", judge_exp = "", exe_type = "Q", script = "insert into pms_compqrcode ( company_id, plant_id, line, station_no, sn, type, qrcode, remark, created_at, created_by,updated_at, updated_by)\r\nVALUES(\'?CompanyId?\',\'?PlantId?\',\'?Line?\',\'?StationNo?\',\'?SN?\',\'SKT\',\'?输入轴批次码?\', \'@输入轴批次码@\', NOW(),\'sys\', NOW(),\'sys\');", remark = "", created_at = DateTime.Now, created_by = null, updated_at = DateTime.Now, updated_by = null });
        //    _list.Add(new iot_biz_mode() { id = "45ed2d21-6f8d-11ec-9b3c-54ee75e6a623", list_order = 3, judge_type = "Q", judge_exp = "", exe_type = "Q", script = "insert into pms_compqrcode ( company_id, plant_id, line, station_no, sn, type, qrcode, remark, created_at, created_by,updated_at, updated_by)\r\nVALUES(\'?CompanyId?\',\'?PlantId?\',\'?Line?\',\'?StationNo?\',\'?SN?\',\'SKT\',\'?内油封批次码?\', \'@内油封批次码@\', NOW(),\'sys\', NOW(),\'sys\');", remark = null, created_at = DateTime.Now, created_by = null, updated_at = DateTime.Now, updated_by = null });
        //    _list.Add(new iot_biz_mode() { id = "58c2127e-6f6a-11ec-a0a9-000c299b8769", list_order = 3, judge_type = "Q", judge_exp = "", exe_type = "Q", script = "insert into pms_plan_position(company_id , plant_id ,  line , station_no , sn ,part_sn , ok_mark, start_time , created_at , created_by)\r\nVALUES(\'?CompanyId?\',\'?PlantId?\',\'?Line?\',\'?StationNo?\',\'?SN?\',\'\', 1, NOW(), NOW(),\'sys\')", remark = null, created_at = DateTime.Now, created_by = null, updated_at = DateTime.Now, updated_by = null });
        //    _list.Add(new iot_biz_mode() { id = "8dca89fe-6f8a-11ec-9b3c-54ee75e6a623", list_order = 1, judge_type = "Q", judge_exp = "?RSP1? > 0", exe_type = "Q", script = "delete from pms_qtyjson WHERE company_id=\'?CompanyId?\' AND plant_id=\'?PlantId?\' AND line=\'?Line?\' AND station_no=\'?StationNo?\' AND SN=\'?SN?\';", remark = null, created_at = DateTime.Now, created_by = null, updated_at = DateTime.Now, updated_by = null });
        //    _list.Add(new iot_biz_mode() { id = "abf50964-6f8a-11ec-9b3c-54ee75e6a623", list_order = 2, judge_type = "Q", judge_exp = "?RSP1? = 0", exe_type = "Q", script = "INSERT INTO pms_qtyjson ( company_id, plant_id, line, region_no, station_no, sn, val_json, remark, created_at, created_by, updated_at, updated_by )\nVALUES\n ( \'?CompanyId?\', \'?PlantId?\', \'?Line?\', \'?RegionNo?\', \'?StationNo?\', \'?SN?\', \'\r\n   [{\"field\": \"#产品合格状态#\", \"name\":\"@产品合格状态@\", \"val\":\"?产品合格状态?\"}, \r\n   {\"field\": \"#压中间轴位移值#\", \"name\":\"@压中间轴位移值@\", \"val\":\"?压中间轴位移值?\"}, \r\n   {\"field\": \"#压中间轴压力值#\", \"name\":\"@压中间轴压力值@\", \"val\":\"?压中间轴压力值?\"}, \r\n   {\"field\": \"#压输入轴位移值#\", \"name\":\"@压输入轴位移值@\", \"val\":\"?压输入轴位移值?\"}, \r\n   {\"field\": \"#压输入轴压力值#\", \"name\":\"@压输入轴压力值@\", \"val\":\"?压输入轴压力值?\"}, \r\n   {\"field\": \"#压内油封位移值#\", \"name\":\"@压内油封位移值@\", \"val\":\"?压内油封位移值?\"}, \r\n   {\"field\": \"#压内油封压力值#\", \"name\":\"@压内油封压力值@\", \"val\":\"?压内油封压力值?\"}]\', \'\', NOW(), \'sys\', NOW(), \'sys\' );", remark = null, created_at = DateTime.Now, created_by = null, updated_at = DateTime.Now, updated_by = null });
        //    _list.Add(new iot_biz_mode() { id = "d0e15139-6a98-11ec-ae88-000c299b8769", list_order = 1, judge_type = "Q", judge_exp = "?RSP1? > 0", exe_type = "Q", script = "delete from pms_qtyjson WHERE company_id=\'?CompanyId?\' AND plant_id=\'?PlantId?\' AND line=\'?Line?\' AND station_no=\'?StationNo?\' AND SN=\'?SN?\';", remark = null, created_at = DateTime.Now, created_by = null, updated_at = DateTime.Now, updated_by = null });
        //    _list.Add(new iot_biz_mode() { id = "d0e1a91b-6a98-11ec-ae88-000c299b8769", list_order = 2, judge_type = "Q", judge_exp = "?RSP1? = 0", exe_type = "Q", script = "insert into pms_qtyjson ( company_id, plant_id, line, region_no, station_no, sn, val_json, remark, created_at, created_by, updated_at, updated_by)\r\nVALUES(\'?CompanyId?\',\'?PlantId?\',\'?Line?\',\'?RegionNo?\',\'?StationNo?\',\'?SN?\',\'[{\"field\": \"#GD#\", \"name\":\"@GD@\", \"val\":\"?GD?\"}, {\"field\": \"#LJ1#\", \"name\":\"@LJ1@\", \"val\":\"?LJ1?\"}, {\"field\": \"#JD1#\", \"name\":\"@JD1@\", \"val\":\"?JD1?\"}, {\"field\": \"#LJ2#\", \"name\":\"@LJ2@\", \"val\":\"?LJ2?\"}, {\"field\": \"#JD2#\", \"name\":\"@JD2@\", \"val\":\"?JD2?\"}, {\"field\": \"#LJ3#\", \"name\":\"@LJ3@\", \"val\":\"?LJ3?\"}, {\"field\": \"#JD3#\", \"name\":\"@JD3@\", \"val\":\"?JD3?\"}]\',\'\', NOW(),\'sys\', NOW(),\'sys\');", remark = null, created_at = DateTime.Now, created_by = null, updated_at = DateTime.Now, updated_by = null });
        //    _list.Add(new iot_biz_mode() { id = "d0e1aba3-6a98-11ec-ae88-000c299b8769", list_order = 1, judge_type = "Q", judge_exp = "", exe_type = "Q", script = "insert into pms_compqrcode ( company_id, plant_id, line, station_no, sn, type, qrcode, remark, created_at, created_by,updated_at, updated_by)\r\nVALUES(\'?CompanyId?\',\'?PlantId?\',\'?Line?\',\'?StationNo?\',\'?SN?\',\'?type?\',\'?放油螺栓垫片?\', \'@放油螺栓垫片@\',NOW(),\'sys\', NOW(),\'sys\');", remark = "", created_at = DateTime.Now, created_by = null, updated_at = DateTime.Now, updated_by = null });
        //    _list.Add(new iot_biz_mode() { id = "d0e1ace8-6a98-11ec-ae88-000c299b8769", list_order = 2, judge_type = "Q", judge_exp = "", exe_type = "Q", script = "insert into pms_compqrcode ( company_id, plant_id, line, station_no, sn, type, qrcode, remark, created_at, created_by,updated_at, updated_by)\r\nVALUES(\'?CompanyId?\',\'?PlantId?\',\'?Line?\',\'?StationNo?\',\'?SN?\',\'?type?\',\'?放油螺栓?\', \'@放油螺栓@\', NOW(),\'sys\', NOW(),\'sys\');", remark = "", created_at = DateTime.Now, created_by = null, updated_at = DateTime.Now, updated_by = null });
        //    _list.Add(new iot_biz_mode() { id = "d0e1ae05-6a98-11ec-ae88-000c299b8769", list_order = 3, judge_type = "Q", judge_exp = "", exe_type = "Q", script = "insert into pms_compqrcode ( company_id, plant_id, line, station_no, sn, type, qrcode, remark, created_at, created_by,updated_at, updated_by)\r\nVALUES(\'?CompanyId?\',\'?PlantId?\',\'?Line?\',\'?StationNo?\',\'?SN?\',\'?type?\',\'?输入输出油封?\', \'@输入输出油封@\', NOW(),\'sys\', NOW(),\'sys\');", remark = null, created_at = DateTime.Now, created_by = null, updated_at = DateTime.Now, updated_by = null });

        //    return _list;
        //}
        //public static List<iot_exe> GenIotExe()
        //{
        //    List<iot_exe> _list = new List<iot_exe>(); _list.Add(new iot_exe() { id = "06d6c482-6f8d-11ec-9b3c-54ee75e6a623", biz_id = "06d6c482-6f8d-11ec-9b3c-54ee75e6a623", remark = null, created_at = DateTime.Now, created_by = null, updated_at = DateTime.Now, updated_by = null });
        //    _list.Add(new iot_exe() { id = "d7c490bb-62e8-45c0-8459-ea6727e44f10", biz_id = "d7c490bb-62e8-45c0-8459-ea6727e44f10", remark = null, created_at = DateTime.Now, created_by = null, updated_at = DateTime.Now, updated_by = null });
        //    _list.Add(new iot_exe() { id = "247d0906-2d43-433b-8404-b0e71051f5b1", biz_id = "247d0906-2d43-433b-8404-b0e71051f5b1", remark = null, created_at = DateTime.Now, created_by = null, updated_at = DateTime.Now, updated_by = null });
        //    _list.Add(new iot_exe() { id = "45ed2c3e-6f8d-11ec-9b3c-54ee75e6a623", biz_id = "45ed2c3e-6f8d-11ec-9b3c-54ee75e6a623", remark = "", created_at = DateTime.Now, created_by = null, updated_at = DateTime.Now, updated_by = null });
        //    _list.Add(new iot_exe() { id = "45ed2cec-6f8d-11ec-9b3c-54ee75e6a623", biz_id = "45ed2cec-6f8d-11ec-9b3c-54ee75e6a623", remark = "", created_at = DateTime.Now, created_by = null, updated_at = DateTime.Now, updated_by = null });
        //    _list.Add(new iot_exe() { id = "45ed2d21-6f8d-11ec-9b3c-54ee75e6a623", biz_id = "45ed2d21-6f8d-11ec-9b3c-54ee75e6a623", remark = null, created_at = DateTime.Now, created_by = null, updated_at = DateTime.Now, updated_by = null });
        //    _list.Add(new iot_exe() { id = "58c2127e-6f6a-11ec-a0a9-000c299b8769", biz_id = "58c2127e-6f6a-11ec-a0a9-000c299b8769", remark = null, created_at = DateTime.Now, created_by = null, updated_at = DateTime.Now, updated_by = null });
        //    _list.Add(new iot_exe() { id = "8dca89fe-6f8a-11ec-9b3c-54ee75e6a623", biz_id = "8dca89fe-6f8a-11ec-9b3c-54ee75e6a623", remark = null, created_at = DateTime.Now, created_by = null, updated_at = DateTime.Now, updated_by = null });
        //    _list.Add(new iot_exe() { id = "abf50964-6f8a-11ec-9b3c-54ee75e6a623", biz_id = "abf50964-6f8a-11ec-9b3c-54ee75e6a623", remark = null, created_at = DateTime.Now, created_by = null, updated_at = DateTime.Now, updated_by = null });
        //    _list.Add(new iot_exe() { id = "d0e15139-6a98-11ec-ae88-000c299b8769", biz_id = "d0e15139-6a98-11ec-ae88-000c299b8769", remark = null, created_at = DateTime.Now, created_by = null, updated_at = DateTime.Now, updated_by = null });
        //    _list.Add(new iot_exe() { id = "d0e1a91b-6a98-11ec-ae88-000c299b8769", biz_id = "d0e1a91b-6a98-11ec-ae88-000c299b8769", remark = null, created_at = DateTime.Now, created_by = null, updated_at = DateTime.Now, updated_by = null });
        //    _list.Add(new iot_exe() { id = "d0e1aba3-6a98-11ec-ae88-000c299b8769", biz_id = "d0e1aba3-6a98-11ec-ae88-000c299b8769", remark = "", created_at = DateTime.Now, created_by = null, updated_at = DateTime.Now, updated_by = null });
        //    _list.Add(new iot_exe() { id = "d0e1ace8-6a98-11ec-ae88-000c299b8769", biz_id = "d0e1ace8-6a98-11ec-ae88-000c299b8769", remark = "", created_at = DateTime.Now, created_by = null, updated_at = DateTime.Now, updated_by = null });
        //    _list.Add(new iot_exe() { id = "d0e1ae05-6a98-11ec-ae88-000c299b8769", biz_id = "d0e1ae05-6a98-11ec-ae88-000c299b8769", remark = null, created_at = DateTime.Now, created_by = null, updated_at = DateTime.Now, updated_by = null });

        //    return _list;
        //}

        /// <summary>
        /// 设备执行不充值
        /// </summary>
        /// <returns></returns>
        public static List<iot_exeval> GenIotExeVal()
        {
            List<iot_exeval> _list = new List<iot_exeval>();
            _list.Add(new iot_exeval() { id = "41d5e257-6a44-11ec-ae88-000c299b8769", p_id = "4", val_field = "RSP1", val_opu = "P", exe_order = 1, val_mode = "Q", fix_val = null, sql = "SELECT COUNT(*) from pms_qtyjson WHERE company_id=\'?CompanyId?\' AND plant_id=\'?PlantId?\' AND line=\'?Line?\' AND station_no=\'?StationNo?\' AND SN=\'?SN?\';", misc = "", created_at = DateTime.Now, created_by = null, updated_at = DateTime.Now, updated_by = null });
            _list.Add(new iot_exeval()
            {
                id = "41d5e4cc-6a44-11ec-ae88-000c299b8769",
                p_id = "4",
                val_field = "RSP2",
                val_opu = "P",
                exe_order = 1,
                val_mode = "Q",
                fix_val = null,
                sql = "SELECT COUNT(*) FROM pms_plan WHERE company_id=\'?CompanyId?\' AND plant_id=\'?PlantId?\' AND line=\'?Line?\' AND SN=\'?SN?\'",
                misc = "",
                created_at = DateTime.Now,
                created_by = null,
                updated_at = DateTime.Now,
                updated_by = null
            });
            _list.Add(new iot_exeval() { id = "daa4658e-6f8f-11ec-9b3c-54ee75e6a623", p_id = "10560", val_field = "RSP1", val_opu = "P", exe_order = 1, val_mode = "Q", fix_val = null, sql = "SELECT COUNT(*) from pms_qtyjson WHERE company_id=\'?CompanyId?\' AND plant_id=\'?PlantId?\' AND line=\'?Line?\' AND station_no=\'?StationNo?\' AND SN=\'?SN?\';", misc = "", created_at = DateTime.Now, created_by = null, updated_at = DateTime.Now, updated_by = null });
            _list.Add(new iot_exeval() { id = "daa4662e-6f8f-11ec-9b3c-54ee75e6a623", p_id = "10560", val_field = "RSP2", val_opu = "P", exe_order = 2, val_mode = "Q", fix_val = null, sql = "SELECT COUNT(*) FROM pms_plan WHERE company_id=\'?CompanyId?\' AND plant_id=\'?PlantId?\' AND line=\'?Line?\' AND SN=\'?SN?\'", misc = "", created_at = DateTime.Now, created_by = null, updated_at = DateTime.Now, updated_by = null });
            _list.Add(new iot_exeval()
            {
                id = "7b7e0ac7-5163-41a9-b05c-d9c2e35a0596",
                p_id = "12",
                val_field = "type",
                val_opu = "p",
                exe_order = 1,
                val_mode = "F",
                fix_val = "SKT",
                sql = "",
                misc = "",
                created_at = DateTime.Now,
                created_by = null,
                updated_at = DateTime.Now,
                updated_by = null
            });

            return _list;
        }
    }
}

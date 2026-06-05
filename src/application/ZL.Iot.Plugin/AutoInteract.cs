using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ZL.Dao.IotDevice;
using ZL.Iot.Interface;
using ZL.PlcBase.Core;
using ZL.PlcBase.Models;
using ZL.Tag;

namespace ZL.Iot.Plugin
{
    /// <summary>
    /// 自动交互插件（已迁移至 PlcBase 架构）
    /// </summary>
    public class AutoInteract : ZL.PlcBase.Core.DeviceBase
    {
        private DataTable _dtGeneralParamValue = new DataTable();
        DataTable dtGeneralParam = new DataTable();
        DataTable dtDbItem = new DataTable();
        private string DeviceId = "";
        private string CompanyId = "";
        private string PlantId = "";
        private string Line = "";
        private string RegionNo = "";
        private string StationNo = "";
        private string LogFile = "";
        static object sync = new object();

        // 兼容旧架构的交互信息列表
        public List<dynamic> InteractInfoList = new List<dynamic>();

        public AutoInteract(IotDeviceDriverDto dto) : base(MapDtoToConfig(dto))
        {
            DeviceId = dto.device_id;
            CompanyId = dto.company_id;
            PlantId = dto.plant_id;
            Line = dto.line;
            RegionNo = dto.region_no;
            StationNo = dto.station_no;
            LogFile = dto.station_no;
        }

        private static DeviceConfig MapDtoToConfig(IotDeviceDriverDto dto)
        {
            var config = new DeviceConfig();
            config.SetParam("DeviceId", dto.device_id);
            config.SetParam("StationNo", dto.station_no);
            config.SetParam("IpAddress", dto.ip);
            config.SetParam("Port", dto.port);
            return config;
        }

        public override bool Connect()
        {
            // 由 PlcBase 核心处理连接逻辑
            return true;
        }

        /// <summary>
        /// 批量读取循环（框架要求必须实现），
        /// 此桥接类不直接处理 IO——实际的 PLC 读写由 CreateDriver 创建的驱动处理。</summary>
        public override async Task BatchReadLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try { await Task.Delay(1000, token); }
                catch (OperationCanceledException) { break; }
            }
        }

        /// <summary>
        /// 标签值变化事件。B 框架在每轮采集或触发写入后调用此方法。</summary>
        public override void OnTriggerDataChange(string id, object value, TagItem tag)
        {
            HandleDataChange(id, value, tag);
        }

        private void HandleDataChange(string tagId, object value, TagItem tag)
        {
            // 将新架构的点位变化桥接到旧的交互逻辑
            lock (sync)
            {
                // 模拟 DataChangeArgs
                var e = new { Id = tagId, Value = value };
                
                // 查找对应的交互配置
                var tagInfo = InteractInfoList.FirstOrDefault(it => it.id == tagId);
                if (tagInfo == null) return;

                string info_type = tagInfo.info_type;
                bool val = false;
                if (value is bool b) val = b;
                else if (value != null && int.TryParse(value.ToString(), out int iv)) val = iv != 0;

                // 具体的业务触发逻辑（保留原有 PLAN/SCAN 等逻辑，此处为示意，实际应从原文件复制）
                if (info_type == "PLAN" && val)
                {
                    ExecutePlanLogic(tagId, tagInfo);
                }
            }
        }

        private void ExecutePlanLogic(string tagId, dynamic tagInfo)
        {
            Log($"工位:【{StationNo}】，【PLC请求下发生产计划开始】", LogFile);
            // 实现原有的 PLAN 业务逻辑...
        }

        public string WirteGeneral2Plc(object device, DeviceAddress[] items, DataTable dt, int plc_add_list_start_index, string logFile)
        {
            // 适配新架构的批量写入
            foreach (var item in items)
            {
                // 使用 EnqueueNormalWriteAsync 实现高性能写入
                this.EnqueueNormalWriteAsync(item.Id, dt.Rows[0][item.Id]);
            }
            return "";
        }
    }
}

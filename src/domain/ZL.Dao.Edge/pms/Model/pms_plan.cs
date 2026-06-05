using System;
using SqlSugar;

namespace ZL.Dao.Edge
{
    public class pms_plan
    {
        [SugarColumn(IsPrimaryKey = true, IsIdentity = true, IsNullable = false)]
        public long id { get; set; }

        /// <summary>
        /// Desc:计划编号
        /// Default:
        /// Nullable:False
        /// </summary>           
        public string plan_id { get; set; }

        /// <summary>
        /// Desc:总量计划ID
        /// Default:
        /// Nullable:True
        /// </summary>           
        public int? plan_seq_id { get; set; }

        /// <summary>
        /// Desc:公司ID
        /// Default:
        /// Nullable:False
        /// </summary>           
        public string company_id { get; set; }

        /// <summary>
        /// Desc:工厂ID
        /// Default:
        /// Nullable:False
        /// </summary>           
        public string plant_id { get; set; }

        /// <summary>
        /// Desc:线体
        /// Default:
        /// Nullable:False
        /// </summary>           
        public string line { get; set; }

        /// <summary>
        /// Desc:产品序列号
        /// Default:
        /// Nullable:False
        /// </summary>           
        public string sn { get; set; }

        /// <summary>
        /// Desc:排序号
        /// Default:
        /// Nullable:True
        /// </summary>           
        public int? list_order { get; set; }

        /// <summary>
        /// Desc:系列(大的系列)
        /// Default:
        /// Nullable:True
        /// </summary>           
        public string catena { get; set; }

        /// <summary>
        /// Desc:产品型号
        /// Default:
        /// Nullable:False
        /// </summary>           
        public string model { get; set; }

        /// <summary>
        /// Desc:BOM版本
        /// Default:
        /// Nullable:True
        /// </summary>           
        public string bom_ver { get; set; }

        /// <summary>
        /// Desc:计划生产日期
        /// Default:
        /// Nullable:False
        /// </summary>           
        public string plan_date { get; set; }

        /// <summary>
        /// Desc:班次编号
        /// Default:
        /// Nullable:True
        /// </summary>           
        public string shift_no { get; set; }

        /// <summary>
        /// Desc:派工单号
        /// Default:
        /// Nullable:False
        /// </summary>           
        public string work_no { get; set; }

        /// <summary>
        /// Desc:订单号
        /// Default:
        /// Nullable:False
        /// </summary>           
        public string order_no { get; set; }

        /// <summary>
        /// Desc:订单状态
        /// Default:
        /// Nullable:False
        /// </summary>           
        public string plan_state { get; set; }

        /// <summary>
        /// Desc:接收类型(0-接口；1-手动录入，2-EXCEL导入)
        /// Default:
        /// Nullable:False
        /// </summary>           
        public int accept_type { get; set; }

        /// <summary>
        /// Desc:产品开工时间
        /// Default:
        /// Nullable:True
        /// </summary>           
        public DateTime? start_time { get; set; }

        /// <summary>
        /// Desc:产品完工时间(具备发交状态)
        /// Default:
        /// Nullable:True
        /// </summary>           
        public DateTime? end_time { get; set; }

        /// <summary>
        /// Desc:当前产品所在工位
        /// Default:
        /// Nullable:True
        /// </summary>           
        public string station_no { get; set; }

        /// <summary>
        /// Desc:备注
        /// Default:
        /// Nullable:True
        /// </summary>           
        public string remark { get; set; }

        /// <summary>
        /// Desc:产品报废标记(0-正常,1-报废)
        /// Default:
        /// Nullable:False
        /// </summary>           
        public string waste_mark { get; set; }

        /// <summary>
        /// Desc:优先标记(跳号，0-正常，1-优先)
        /// Default:
        /// Nullable:False
        /// </summary>           
        public int priority_mark { get; set; }

        /// <summary>
        /// Desc:主线上线标记(0-未上线，1-已上线)
        /// Default:
        /// Nullable:False
        /// </summary>           
        public string on_mark { get; set; }

        /// <summary>
        /// Desc:主线上线时间
        /// Default:
        /// Nullable:True
        /// </summary>           
        public string on_time { get; set; }

        /// <summary>
        /// Desc:主线下线标记(0-未下线，1-已下线)
        /// Default:
        /// Nullable:False
        /// </summary>           
        public string off_mark { get; set; }

        /// <summary>
        /// Desc:主线下线时间
        /// Default:
        /// Nullable:True
        /// </summary>           
        public string off_time { get; set; }

        /// <summary>
        /// Desc:返修标记(0-正常，1-返修)
        /// Default:
        /// Nullable:False
        /// </summary>           
        public string repair_mark { get; set; }

        /// <summary>
        /// Desc:返修上线时间
        /// Default:
        /// Nullable:True
        /// </summary>           
        public string repair_time { get; set; }

        /// <summary>
        /// Desc:返修目标工位
        /// Default:
        /// Nullable:True
        /// </summary>           
        public string repair_dest_station { get; set; }

        /// <summary>
        /// Desc:创建时间
        /// Default:
        /// Nullable:False
        /// </summary>           
        public DateTime created_at { get; set; }

        /// <summary>
        /// Desc:创建人
        /// Default:
        /// Nullable:False
        /// </summary>           
        public string created_by { get; set; }

        /// <summary>
        /// Desc:更新时间
        /// Default:
        /// Nullable:False
        /// </summary>           
        public DateTime updated_at { get; set; }

        /// <summary>
        /// Desc:更新人
        /// Default:
        /// Nullable:False
        /// </summary>           
        public string updated_by { get; set; }

        /// <summary>
        /// Desc:差速器上线
        /// Default:
        /// Nullable:True
        /// </summary>           
        //public string csq_on_mark { get; set; }
    }
}

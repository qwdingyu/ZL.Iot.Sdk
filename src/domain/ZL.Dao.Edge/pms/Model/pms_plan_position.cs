using System;
using SqlSugar;

namespace ZL.Dao.Edge
{
    public class pms_plan_position
    {
        [SugarColumn(IsPrimaryKey = true, IsIdentity = true, IsNullable = false)]
        public long id { get; set; }

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
        /// Desc:区域号
        /// Default:
        /// Nullable:True
        /// </summary>           
        public string region_no { get; set; }

        /// <summary>
        /// Desc:工位号
        /// Default:
        /// Nullable:False
        /// </summary>           
        public string station_no { get; set; }

        /// <summary>
        /// Desc:产品（工件）流水号
        /// Default:
        /// Nullable:False
        /// </summary>           
        public string sn { get; set; }

        /// <summary>
        /// Desc:分总成序列号
        /// Default:
        /// Nullable:True
        /// </summary>           
        public string part_sn { get; set; }

        /// <summary>
        /// Desc:开始时间
        /// Default:
        /// Nullable:True
        /// </summary>           
        public DateTime start_time { get; set; }

        /// <summary>
        /// Desc:结束时间
        /// Default:
        /// Nullable:True
        /// </summary>           
        public DateTime end_time { get; set; }

        /// <summary>
        /// Desc:是否合格(0-未知，1-合格，2-不合格)
        /// Default:
        /// Nullable:False
        /// </summary>           
        public int ok_mark { get; set; }

        /// <summary>
        /// Desc:托盘号
        /// Default:
        /// Nullable:True
        /// </summary>           
        public string pallet_no { get; set; }

        /// <summary>
        /// Desc:删除标记(0-正常，1-删除)
        /// Default:
        /// Nullable:False
        /// </summary>           
        public byte del_mark { get; set; }

        /// <summary>
        /// Desc:分总成的批次号
        /// Default:
        /// Nullable:True
        /// </summary>           
        public string batch_no { get; set; }

        /// <summary>
        /// Desc:报废标记(0-正常，1-报废)
        /// Default:
        /// Nullable:False
        /// </summary>           
        public byte waste_mark { get; set; }

        /// <summary>
        /// Desc:报废时间
        /// Default:
        /// Nullable:True
        /// </summary>           
        public DateTime? waste_time { get; set; }

        /// <summary>
        /// Desc:接口发送标记(0-未发送，1-已发送)
        /// Default:
        /// Nullable:False
        /// </summary>           
        public byte if_send_mark { get; set; }

        /// <summary>
        /// Desc:发送时间
        /// Default:
        /// Nullable:True
        /// </summary>           
        public DateTime? if_send_time { get; set; }

        /// <summary>
        /// Desc:创建时间
        /// Default:
        /// Nullable:True
        /// </summary>           
        public DateTime? created_at { get; set; }

        /// <summary>
        /// Desc:创建人
        /// Default:
        /// Nullable:True
        /// </summary>           
        public string created_by { get; set; }

        /// <summary>
        /// Desc:更新时间
        /// Default:
        /// Nullable:True
        /// </summary>           
        public DateTime? updated_at { get; set; }

        /// <summary>
        /// Desc:更新人
        /// Default:
        /// Nullable:True
        /// </summary>           
        public string updated_by { get; set; }
    }
}

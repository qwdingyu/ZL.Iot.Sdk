using System;
using SqlSugar;

namespace ZL.Dao.Edge
{
    ///<summary>
    ///工序计划
    ///</summary>
    [SugarTable("pms_plan_list")]
    public class pms_plan_list
    {

        /// <summary>
        /// Desc:
        /// Default:
        /// Nullable:False
        /// </summary>           
        [SugarColumn(IsPrimaryKey = true, IsIdentity = true, IsNullable = false)]
        public long id { get; set; }

        /// <summary>
        /// Desc:公司ID
        /// Default:
        /// Nullable:True
        /// </summary>           
        public string company_id { get; set; }

        /// <summary>
        /// Desc:工厂ID
        /// Default:
        /// Nullable:True
        /// </summary>           
        public string plant_id { get; set; }

        /// <summary>
        /// Desc:线体
        /// Default:
        /// Nullable:True
        /// </summary>           
        public string line { get; set; }

        /// <summary>
        /// Desc:产品序列号
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
        /// Desc:托盘号
        /// Default:
        /// Nullable:True
        /// </summary>           
        public int? pallet_no { get; set; }

        /// <summary>
        /// Desc:工位号
        /// Default:
        /// Nullable:False
        /// </summary>           
        public string station_no { get; set; }

        /// <summary>
        /// Desc:开始时间
        /// Default:
        /// Nullable:True
        /// </summary>           
        public DateTime? start_time { get; set; }

        /// <summary>
        /// Desc:结束时间
        /// Default:
        /// Nullable:True
        /// </summary>           
        public DateTime? end_time { get; set; }

        /// <summary>
        /// Desc:返修标记(0-正常，1-返修)
        /// Default:
        /// Nullable:False
        /// </summary>           
        public byte repaire_mark { get; set; }

        /// <summary>
        /// Desc:报废标记(0-正常，1-报废)
        /// Default:
        /// Nullable:False
        /// </summary>           
        public byte waste_mark { get; set; }

        /// <summary>
        /// Desc:实绑定分总成序列号
        /// Default:
        /// Nullable:True
        /// </summary>           
        public string act_part_sn { get; set; }

        /// <summary>
        /// Desc:实绑定时间
        /// Default:
        /// Nullable:True
        /// </summary>           
        public DateTime? act_part_time { get; set; }

        /// <summary>
        /// Desc:是否可加工(0-可加工，1-不可加工)
        /// Default:
        /// Nullable:True
        /// </summary>           
        public byte? can_work_mark { get; set; }

        /// <summary>
        /// 工作完成
        /// </summary>
        public byte job_completed { get; set; }

        /// <summary>
        /// Desc:删除标记(0-正常，1-删除)
        /// Default:
        /// Nullable:True
        /// </summary>           
        public byte? del_mark { get; set; }

        /// <summary>
        /// Desc:是否是首工位(0-不是，1-是)
        /// Default:
        /// Nullable:True
        /// </summary>           
        public byte? Is_first_station_no { get; set; }

        /// <summary>
        /// Desc:是否是最后工位(0-不是，1-是)
        /// Default:
        /// Nullable:True
        /// </summary>           
        public byte? Is_last_station_no { get; set; }

        /// <summary>
        /// Desc:备注
        /// Default:
        /// Nullable:True
        /// </summary>           
        public string remark { get; set; }

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

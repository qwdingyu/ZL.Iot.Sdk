using System;
using SqlSugar;

namespace ZL.Dao.Edge
{
    ///<summary>
    ///工位
    ///</summary>
    [SugarTable("op")]
    public partial class op
    {

        /// <summary>
        /// Desc:
        /// Default:
        /// Nullable:False
        /// </summary>           
        [SugarColumn(IsPrimaryKey = true, IsIdentity = true, IsNullable = false)]
        public int id { get; set; }

        /// <summary>
        /// Desc:公司
        /// Default:
        /// Nullable:False
        /// </summary>           
        public string company_id { get; set; }

        /// <summary>
        /// Desc:工厂
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
        /// Desc:工位编号
        /// Default:
        /// Nullable:False
        /// </summary>           
        public string station_no { get; set; }

        /// <summary>
        /// Desc:工位名称
        /// Default:
        /// Nullable:False
        /// </summary>           
        public string station_name { get; set; }

        /// <summary>
        /// Desc:IP地址
        /// Default:
        /// Nullable:False
        /// </summary>           
        public string ip_add { get; set; }

        /// <summary>
        /// Desc:Mac地址
        /// Default:
        /// Nullable:True
        /// </summary>           
        public string mac { get; set; }

        /// <summary>
        /// Desc:PC登陆名
        /// Default:
        /// Nullable:True
        /// </summary>           
        public string login { get; set; }

        /// <summary>
        /// Desc:PC登陆密码
        /// Default:
        /// Nullable:True
        /// </summary>           
        public string password { get; set; }

        /// <summary>
        /// Desc:工位类型(工单、追溯、质量、拧紧)
        /// Default:
        /// Nullable:True
        /// </summary>           
        public string work_type { get; set; }

        /// <summary>
        /// Desc:是否启用(0-不启用，1-启用)
        /// Default:
        /// Nullable:False
        /// </summary>           
        public byte enabled { get; set; }

        /// <summary>
        /// Desc:备注
        /// Default:
        /// Nullable:True
        /// </summary>           
        public string remark { get; set; }

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
        /// Desc:当前工位产品序列号
        /// Default:
        /// Nullable:True
        /// </summary>           
        public string sn { get; set; }

    }
}

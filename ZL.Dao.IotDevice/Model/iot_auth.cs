using System;
using SqlSugar;

namespace ZL.Dao.IotDevice
{
    ///<summary>
    ///
    ///</summary>
    public partial class iot_auth
    {
        public iot_auth()
        {

        }
        /// <summary>
        /// Desc:
        /// Default:
        /// Nullable:False
        /// </summary>           
        [SugarColumn(IsPrimaryKey = true, IsNullable = false)]
        public string id { get; set; }

        /// <summary>
        /// Desc:公司id
        /// Default:
        /// Nullable:True
        /// </summary>           
        public string company_id { get; set; }

        /// <summary>
        /// Desc:工厂id
        /// Default:
        /// Nullable:True
        /// </summary>           
        public string plant_id { get; set; }

        /// <summary>
        /// Desc:线号
        /// Default:
        /// Nullable:True
        /// </summary>           
        public string line { get; set; }

        /// <summary>
        /// Desc:采集终端码
        /// Default:
        /// Nullable:True
        /// </summary>           
        public string edge_sn { get; set; }

        /// <summary>
        /// Desc:采集终端ip地址
        /// Default:
        /// Nullable:True
        /// </summary>           
        [SugarColumn(IsNullable = true)]
        public string edge_ip { get; set; }

        /// <summary>
        /// Desc:授权码
        /// Default:
        /// Nullable:True
        /// </summary>           
        [SugarColumn(IsNullable = true)]
        public string auth_sn { get; set; }

        /// <summary>
        /// Desc:状态 1-正在运行，0-已停止
        /// Default:
        /// Nullable:True
        /// </summary>           
        public int? run_status { get; set; }

        /// <summary>
        /// Desc:有效期
        /// Default:
        /// Nullable:True
        /// </summary>           
        public DateTime? valid_date { get; set; }

        /// <summary>
        /// Desc:
        /// Default:
        /// Nullable:True
        /// </summary>           
        [SugarColumn(IsNullable = true)]
        public string remark { get; set; }

        /// <summary>
        /// Desc:
        /// Default:
        /// Nullable:True
        /// </summary>           
        public DateTime? created_at { get; set; }

        /// <summary>
        /// Desc:
        /// Default:
        /// Nullable:True
        /// </summary>           
           [SugarColumn(Length=36,IsNullable=true)]
           public string created_by {get;set;}

        /// <summary>
        /// Desc:
        /// Default:
        /// Nullable:True
        /// </summary>           
        public DateTime? updated_at { get; set; }

        /// <summary>
        /// Desc:
        /// Default:
        /// Nullable:True
        /// </summary>           
           [SugarColumn(Length=36,IsNullable=true)]
           public string updated_by {get;set;}

    }
}

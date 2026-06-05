using SqlSugar;
using System;
using System.Data.SqlTypes;

namespace ZL.Dao.IotDevice
{
    ///<summary>
    ///
    ///</summary>
    public partial class iot_auth_log
    {
        public iot_auth_log()
        {


        }
        /// <summary>
        /// Desc:
        /// Default:
        /// Nullable:False
        /// </summary>           
        public int id { get; set; }

        /// <summary>
        /// Desc:iot_auth.id
        /// Default:
        /// Nullable:False
        /// </summary>           
        public string edge_id { get; set; }

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
        public string edge_ip { get; set; }

        /// <summary>
        /// Desc:授权码
        /// Default:
        /// Nullable:True
        /// </summary>           
        public string auth_sn { get; set; }

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

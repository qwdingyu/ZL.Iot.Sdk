using System;
using SqlSugar;


namespace ZL.Dao.IotDevice
{
    ///<summary>
    ///
    ///</summary>
    public partial class iot_node
    {
        public iot_node()
        {
        }
        /// <summary>
        /// Desc:
        /// Default:
        /// Nullable:False
        /// </summary>           
        [SugarColumn(IsNullable = false, IsPrimaryKey = true, IsIdentity = true)]
        public int id { get; set; }

        /// <summary>
        /// Desc:公司编号
        /// Default:
        /// Nullable:False
        /// </summary>           
        public string company_id { get; set; }

        /// <summary>
        /// Desc:工厂编号
        /// Default:
        /// Nullable:False
        /// </summary>           
        public string plant_id { get; set; }

        /// <summary>
        /// Desc:线号
        /// Default:
        /// Nullable:False
        /// </summary>           
        public string line { get; set; }

        /// <summary>
        /// Desc:工位
        /// Default:
        /// Nullable:False
        /// </summary>           
        public string station_no { get; set; }

        /// <summary>
        /// Desc:IP地址
        /// Default:
        /// Nullable:False
        /// </summary>           
        public string ip { get; set; }

        /// <summary>
        /// Desc:是否在线 0不在线，1在线
        /// Default:
        /// Nullable:False
        /// </summary>           
        public byte is_online { get; set; }

        /// <summary>
        /// Desc:备注
        /// Default:
        /// Nullable:True
        /// </summary>           
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
        public DateTime? update_at { get; set; }

    }
}

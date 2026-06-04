using System;
using SqlSugar;

namespace ZL.Dao.IotDevice
{
    ///<summary>
    ///
    ///</summary>
    public partial class iot_edge_relation
    {
        public iot_edge_relation()
        {


        }
        /// <summary>
        /// Desc:
        /// Default:
        /// Nullable:False
        /// </summary>           
        [SugarColumn(IsPrimaryKey = true, Length=36, IsNullable = false)]
        public string id { get; set; }

        /// <summary>
        /// Desc:iot_auth.id
        /// Default:
        /// Nullable:False
        /// </summary>           
        [SugarColumn(Length=36, IsNullable = false)]
        public string edge_id { get; set; }

        /// <summary>
        /// Desc:iot_device.id
        /// Default:
        /// Nullable:True
        /// </summary>           
        [SugarColumn(Length=36, IsNullable = false)]
        public string device_id { get; set; }

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

using System;
using SqlSugar;

namespace ZL.Dao.Edge
{
    public class pms_compqrcode
    {
        [SugarColumn(IsPrimaryKey = true, IsIdentity = true, IsNullable = false)]
        public long id { get; set; }

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
        /// Desc:线体
        /// Default:
        /// Nullable:False
        /// </summary>           
        public string line { get; set; }

        /// <summary>
        /// Desc:工位号
        /// Default:
        /// Nullable:False
        /// </summary>           
        public string station_no { get; set; }

        /// <summary>
        /// Desc:父零件序列号
        /// Default:
        /// Nullable:False
        /// </summary>           
        public string sn { get; set; }

        /// <summary>
        /// Desc:子零件序列号
        /// Default:
        /// Nullable:True
        /// </summary>           
        public string part_sn { get; set; }

        /// <summary>
        /// Desc:二维码类型
        /// Default:
        /// Nullable:False
        /// </summary>           
        public string type { get; set; }

        /// <summary>
        /// Desc:二维码编号
        /// Default:
        /// Nullable:True
        /// </summary>           
        public string qrcode { get; set; }

        /// <summary>
        /// Desc:备注
        /// Default:
        /// Nullable:True
        /// </summary>           
        public string remark { get; set; }

        /// <summary>
        /// Desc:发送标记
        /// Default:
        /// Nullable:True
        /// </summary>           
        public byte? if_send_mark { get; set; }

        /// <summary>
        /// Desc:发送时间
        /// Default:
        /// Nullable:True
        /// </summary>           
        public DateTime? if_send_time { get; set; }

        /// <summary>
        /// Desc:创建时间
        /// Default:
        /// Nullable:False
        /// </summary>           
        public DateTime created_at { get; set; }

        /// <summary>
        /// Desc:创建人
        /// Default:
        /// Nullable:True
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
        /// Nullable:True
        /// </summary>           
        public string updated_by { get; set; }
    }
}

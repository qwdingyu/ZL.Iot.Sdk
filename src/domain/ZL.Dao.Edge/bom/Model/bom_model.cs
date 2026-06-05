using System;
using SqlSugar;

namespace ZL.Dao.Edge
{
    ///<summary>
    ///
    ///</summary>
    [SugarTable("bom_model")]
    public class bom_model
    {

        /// <summary>
        /// Desc:
        /// Default:
        /// Nullable:False
        /// </summary>           
        [SugarColumn(IsPrimaryKey = true, IsIdentity = true, IsNullable = false)]
        public int id { get; set; }

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
        /// Desc:型号名称
        /// Default:
        /// Nullable:False
        /// </summary>           
        public string model_name { get; set; }

        /// <summary>
        /// Desc:短代码
        /// Default:
        /// Nullable:True
        /// </summary>           
        public string short_code { get; set; }

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

    }
}

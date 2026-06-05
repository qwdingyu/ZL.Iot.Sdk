using System;
using SqlSugar;

namespace ZL.Dao.Edge
{
    ///<summary>
    ///
    ///</summary>
    public partial class bom_line
    {
        public bom_line()
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
        /// Nullable:False
        /// </summary>           
        public string line { get; set; }
        public string line_name { get; set; }
        public string list_order { get; set; }
        public string plan_starttime { get; set; }
        public string plan_overtime { get; set; }

        /// <summary>
        /// Desc:创建时间
        /// Default:
        /// Nullable:True
        /// </summary>           
        public DateTime? created_at { get; set; }

        /// <summary>
        /// Desc:更新时间
        /// Default:
        /// Nullable:True
        /// </summary>           
        public DateTime? updated_at { get; set; }

    }
}

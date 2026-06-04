using System;
using SqlSugar;

namespace ZL.Dao.Edge
{
    ///<summary>
    ///
    ///</summary>
    [SugarTable("print_queue")]
    public class print_queue
    {

        /// <summary>
        /// Desc:
        /// Default:
        /// Nullable:False
        /// </summary>           
        [SugarColumn(IsPrimaryKey = true, IsIdentity = true, IsNullable = false)]
        public long id { get; set; }
        public string company_id { get; set; }
        public string plant_id { get; set; }

        /// <summary>
        /// Desc:装配线
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
        /// Desc:条码编号
        /// Default:
        /// Nullable:True
        /// </summary>           
        public string barcode { get; set; }

        /// <summary>
        /// Desc:打印变量值
        /// Default:
        /// Nullable:False
        /// </summary>           
        public string print_vals { get; set; }

        /// <summary>
        /// Desc:当天打印序号
        /// Default:
        /// Nullable:True
        /// </summary>           
        public int? list_order { get; set; }

        /// <summary>
        /// Desc:打印时间
        /// Default:
        /// Nullable:True
        /// </summary>           
        public DateTime? print_time { get; set; }

        /// <summary>
        /// Desc:是否打印0未打印，1打印
        /// Default:
        /// Nullable:False
        /// </summary>           
        public string printed { get; set; }

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
        /// Nullable:True
        /// </summary>           
        public DateTime? updated_at { get; set; }

        /// <summary>
        /// Desc:bom_barcode表中的id
        /// Default:
        /// Nullable:True
        /// </summary>           
        public int? print_def_id { get; set; }

        /// <summary>
        /// Desc:
        /// Default:
        /// Nullable:True
        /// </summary>           
        public string comp_no { get; set; }

    }
}

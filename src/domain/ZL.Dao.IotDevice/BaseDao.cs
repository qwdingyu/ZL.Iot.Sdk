using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using SqlSugar;
using ZL.DB.Acc;

namespace ZL.Dao.IotDevice
{
    public class BaseDao
    {
        SqlSugarScope db;
        public static Repository<iot_device> iot_alarm_logDb => new Repository<iot_device>();
        public static Repository<iot_alarmlog> iot_alarmlogDb => new Repository<iot_alarmlog>();
        public static Repository<iot_auth> iot_authDb => new Repository<iot_auth>();
        public static Repository<iot_auth_log> iot_auth_logDb => new Repository<iot_auth_log>();
        public static Repository<iot_device> iot_deviceDb => new Repository<iot_device>();
        public static Repository<iot_device_arg> iot_device_argDb => new Repository<iot_device_arg>();
        public static Repository<iot_device_type> iot_device_typeDb => new Repository<iot_device_type>();
        public static Repository<iot_driver> iot_driverDb => new Repository<iot_driver>();
        public static Repository<iot_edge_relation> iot_edge_relationDb => new Repository<iot_edge_relation>();
        public static Repository<iot_group> iot_groupDb => new Repository<iot_group>();
        public static Repository<iot_rank_def> iot_rank_defDb => new Repository<iot_rank_def>();
        public static Repository<iot_tag> iot_tagDb => new Repository<iot_tag>();
        public static Repository<iot_tag_sub> iot_tag_subDb => new Repository<iot_tag_sub>();


        public BaseDao()
        {
            //db = SugarAcc.GetSugarClient();
            //注意model的命名空间
            //var modelTypes = from t in Assembly.GetExecutingAssembly().GetTypes()
            //                 where t.IsClass && t.Namespace == "ZL.Dao.IotDevice.Model"
            //                 select t;
            ////创建表
            //modelTypes.ToList().ForEach(t =>
            //{
            //    if (!db.DbMaintenance.IsAnyTable(t.Name))
            //    {
            //        //Console.WriteLine(t.Name);
            //        db.CodeFirst.InitTables(t);
            //    }
            //});

            //在此如何将仓库注册为全局使用的内容
        }

        //public static JsonResult ValidateModel(object primaryValue = null)
        //{
        //    JsonResult errorResult = null;
        //    var validateItem = this.HttpContext.Items?.FirstOrDefault(it => it.Key.ToString() == Pubconst.ITEMKEY);
        //    if (validateItem != null && validateItem.Value.Value is ValidateUnique)
        //    {
        //        var unItem = (validateItem.Value.Value as ValidateUnique);
        //        var queryable = Db.Queryable<object>().AS(unItem.TableName).Where(new List<IConditionalModel>() {
        //            new ConditionalModel(){ FieldName=unItem.DbColumnName,FieldValue=unItem.Value +""}
        //        });
        //        if (unItem.PrimaryKey != null && primaryValue != null && primaryValue.ToString() != "0")
        //        {
        //            queryable.Where(new List<IConditionalModel>() {
        //            new ConditionalModel(){ FieldName=unItem.PrimaryKey , ConditionalType=ConditionalType.NoEqual,FieldValue=primaryValue +""}
        //        });
        //        }
        //        if (queryable.Any())
        //        {
        //            errorResult = new JsonResult(new ApiResult<List<KeyValuePair<string, string>>>()
        //            {
        //                Data = new List<KeyValuePair<string, string>>() {
        //                    new KeyValuePair<string, string>(unItem.FieldName,unItem.Message)
        //                },
        //                IsSuccess = false,
        //                IsKeyValuePair = true
        //            });
        //        }
        //    }

        //    return errorResult;
        //}

    }
}

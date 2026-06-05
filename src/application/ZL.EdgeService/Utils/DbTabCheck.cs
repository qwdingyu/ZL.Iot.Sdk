using SqlSugar;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using ZL.DB.Acc;
using ZL.PFLite.Common;

namespace ZL.EdgeService
{
    public class DbTabCheck : DaoBase
    {

        /// <summary>
        /// 对比model和数据库表结构是否有不一致的地方
        /// </summary>
        /// <param name="tabPrefix">表前缀</param>
        /// <param name="dllFile">Dao中model对应的dll</param>
        /// <returns></returns>
        public bool TabHasDiff(out string diffString, string tabPrefix = "iot_", string dllFile = "ZL.Dao.IotDevice.dll")
        {
            bool ok = false;
            diffString = string.Empty;
            try
            {
                string dllPath = Path.Combine(Environment.CurrentDirectory, dllFile);
                if (!File.Exists(dllPath))
                {
                    LogKit.WriteAndTrace($"数据库表结构核查时，指定的dll【{dllPath}】不存在，请在项目根目录下检查！");
                    return ok;
                }
                Type[] types = Assembly.LoadFrom(dllPath).GetTypes().Where(it => it.FullName.Contains(tabPrefix)).ToArray();
                if (types.Length == 0)
                {
                    LogKit.WriteAndTrace($"数据库表结构核查时，指定的dll【{dllPath}】表前缀【{tabPrefix}】对应的model为空，无法核查表结构是否一致！");
                    return ok;
                }
                //var diffList = db.CodeFirst.GetDifferenceTables(types).ToDiffList();
                //if (diffList.Count > 0)
                //{
                //    ok = true;
                //    LogKit.WriteAndTrace("===============================ERROR===============================");
                //    LogKit.WriteAndTrace($"数据库表结构同model存在不一致的地方，请调整数据库表结构，具体信息如下：");
                //    foreach (var it in diffList)
                //    {
                //        if (it.IsDiff)
                //        {
                //            LogKit.WriteAndTrace(it.TableName);
                //            LogKit.WriteAndTrace(it.ToString());
                //        }
                //    }
                //}
                diffString = db.CodeFirst.GetDifferenceTables(types).ToDiffString();
                if (!string.IsNullOrEmpty(diffString))
                {
                    ok = true;
                    LogKit.WriteAndTrace("===============================ERROR===============================");
                    LogKit.WriteAndTrace($"数据库表结构同model存在不一致的地方，请调整数据库表结构，具体信息如下：");
                    LogKit.WriteAndTrace(diffString);
                }
            }
            catch (Exception ex)
            {
                string err = ex.Message;
                throw;
            }
            return ok;
        }
    }
}

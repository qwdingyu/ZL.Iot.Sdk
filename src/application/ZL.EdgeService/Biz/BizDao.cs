using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ZL.EdgeService
{
    /// <summary>
    /// 业务 DAO 类（P1: 此类中的方法为空实现，需要重构或删除）
    /// </summary>
    /// <remarks>
    /// 警告：这些方法目前返回假成功，实际没有执行任何操作。
    /// 调用方会误以为操作成功，导致数据不一致。
    /// 建议：标记为 Obsolete 或实现实际逻辑。
    /// </remarks>
    [Obsolete("BizDao 类的方法为空实现，请勿使用。将在未来版本中移除。")]
    public class BizDao
    {

        /// <summary>
        /// 添加打印日志（空实现）
        /// </summary>
        /// <deprecated>该方法为空实现，返回假成功。请勿使用。</deprecated>
        public static bool AddPrintLog(string company_id, string plant_id, string line, string station_no, string sn, string print_def_id, string operater)
        {
            // P1: 空实现，返回假成功 - 违反 Fail-Fast 原则
            // 抛出异常比返回假成功更安全
            throw new NotImplementedException(
                "BizDao.AddPrintLog 是空实现。" +
                "该方法应实现实际逻辑或在完全移除前抛出 NotImplementedException。");
        }

        /// <summary>
        /// 添加业务数据（空实现）
        /// </summary>
        /// <deprecated>该方法为空实现，返回假成功。请勿使用。</deprecated>
        public static bool Add(string company_id, string plant_id, string line, string station_no, string sn, string print_def_id, string operater)
        {
            // P1: 空实现，返回假成功 - 违反 Fail-Fast 原则
            throw new NotImplementedException(
                "BizDao.Add 是空实现。" +
                "该方法应实现实际逻辑或在完全移除前抛出 NotImplementedException。");
        }
    }
}

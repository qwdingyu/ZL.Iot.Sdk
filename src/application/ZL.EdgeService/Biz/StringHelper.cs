using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ZL.EdgeService.Biz
{
    /// <summary>字符串助手类</summary>
    /// <remarks>
    /// 文档 https://www.yuque.com/smartstone/nx/string_helper
    /// </remarks>
    public static class StringHelper
    {
        #region 字符串扩展
        /// <summary>忽略大小写的字符串相等比较，判断是否与任意一个待比较字符串相等</summary>
        /// <param name="value">字符串</param>
        /// <param name="strs">待比较字符串数组</param>
        /// <returns></returns>
        public static Boolean EqualIgnoreCase(this String value, params String[] strs)
        {
            foreach (var item in strs)
            {
                if (String.Equals(value, item, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        /// <summary>忽略大小写的字符串开始比较，判断是否与任意一个待比较字符串开始</summary>
        /// <param name="value">字符串</param>
        /// <param name="strs">待比较字符串数组</param>
        /// <returns></returns>
        public static Boolean StartsWithIgnoreCase(this String value, params String[] strs)
        {
            if (value == null || String.IsNullOrEmpty(value)) return false;

            foreach (var item in strs)
            {
                if (value.StartsWith(item, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        /// <summary>忽略大小写的字符串结束比较，判断是否以任意一个待比较字符串结束</summary>
        /// <param name="value">字符串</param>
        /// <param name="strs">待比较字符串数组</param>
        /// <returns></returns>
        public static Boolean EndsWithIgnoreCase(this String value, params String[] strs)
        {
            if (value == null || String.IsNullOrEmpty(value)) return false;

            foreach (var item in strs)
            {
                if (value.EndsWith(item, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        /// <summary>指示指定的字符串是 null 还是 String.Empty 字符串</summary>
        /// <param name="value">字符串</param>
        /// <returns></returns>
        public static Boolean IsNullOrEmpty(this String value) => value == null || value.Length <= 0;

        /// <summary>是否空或者空白字符串</summary>
        /// <param name="value">字符串</param>
        /// <returns></returns>
        public static Boolean IsNullOrWhiteSpace(this String value)
        {
            if (value != null)
            {
                for (var i = 0; i < value.Length; i++)
                {
                    if (!Char.IsWhiteSpace(value[i])) return false;
                }
            }
            return true;
        }

        /// <summary>拆分字符串，过滤空格，无效时返回空数组</summary>
        /// <param name="value">字符串</param>
        /// <param name="separators">分组分隔符，默认逗号分号</param>
        /// <returns></returns>
        public static String[] Split(this String value, params String[] separators)
        {
            //!! netcore3.0中新增Split(String? separator, StringSplitOptions options = StringSplitOptions.None)，优先于StringHelper扩展
            if (value == null || String.IsNullOrEmpty(value)) return new String[0];
            if (separators == null || separators.Length < 1 || separators.Length == 1 && separators[0].IsNullOrEmpty()) separators = new String[] { ",", ";" };

            return value.Split(separators, StringSplitOptions.RemoveEmptyEntries);
        }

        /// <summary>拆分字符串成为整型数组，默认逗号分号分隔，无效时返回空数组</summary>
        /// <remarks>过滤空格、过滤无效、不过滤重复</remarks>
        /// <param name="value">字符串</param>
        /// <param name="separators">分组分隔符，默认逗号分号</param>
        /// <returns></returns>
        public static Int32[] SplitAsInt(this String value, params String[] separators)
        {
            if (value == null || String.IsNullOrEmpty(value)) return new Int32[0];
            if (separators == null || separators.Length < 1) separators = new String[] { ",", ";" };

            var ss = value.Split(separators, StringSplitOptions.RemoveEmptyEntries);
            var list = new List<Int32>();
            foreach (var item in ss)
            {
                int id;
                if (!Int32.TryParse(item.Trim(), out id)) continue;

                // 本意只是拆分字符串然后转为数字，不应该过滤重复项
                //if (!list.Contains(id))
                list.Add(id);
            }

            return list.ToArray();
        }

        /// <summary>拆分字符串成为不区分大小写的可空名值字典。逗号分号分组，等号分隔</summary>
        /// <param name="value">字符串</param>
        /// <param name="nameValueSeparator">名值分隔符，默认等于号</param>
        /// <param name="separators">分组分隔符，默认逗号分号</param>
        /// <returns></returns>
        [Obsolete("该扩展容易带来误解")]
        public static IDictionary<String, String> SplitAsDictionary(this String value, String nameValueSeparator = "=", params String[] separators)
        {
            var dic = new NullableDictionary<String, String>(StringComparer.OrdinalIgnoreCase);
            if (value == null || value.IsNullOrWhiteSpace()) return dic;

            if (String.IsNullOrEmpty(nameValueSeparator)) nameValueSeparator = "=";
            if (separators == null || separators.Length == 0) separators = new String[] { ",", ";" };

            var ss = value.Split(separators, StringSplitOptions.RemoveEmptyEntries);
            if (ss == null || ss.Length == 0) return dic;

            foreach (var item in ss)
            {
                var p = item.IndexOf(nameValueSeparator);
                // 在前后都不行
                if (p <= 0 || p >= item.Length - 1) continue;

                var key = item.Substring(0, p).Trim();
                dic[key] = item.Substring(p + nameValueSeparator.Length).Trim();
            }

            return dic;
        }

        /// <summary>拆分字符串成为不区分大小写的可空名值字典。逗号分组，等号分隔</summary>
        /// <param name="value">字符串</param>
        /// <param name="nameValueSeparator">名值分隔符，默认等于号</param>
        /// <param name="separator">分组分隔符，默认分号</param>
        /// <param name="trimQuotation">去掉括号</param>
        /// <returns></returns>
        public static IDictionary<String, String> SplitAsDictionary(this String value, String nameValueSeparator = "=", String separator = ";", Boolean trimQuotation = false)
        {
            var dic = new NullableDictionary<String, String>(StringComparer.OrdinalIgnoreCase);
            if (value == null || value.IsNullOrWhiteSpace()) return dic;

            if (nameValueSeparator.IsNullOrEmpty()) nameValueSeparator = "=";
            //if (separator == null || separator.Length < 1) separator = new String[] { ",", ";" };

            var ss = value.Split(new[] { separator }, StringSplitOptions.RemoveEmptyEntries);
            if (ss == null || ss.Length < 1) return dic;

            var k = 0;
            foreach (var item in ss)
            {
                var p = item.IndexOf(nameValueSeparator);
                if (p <= 0)
                {
                    dic[$"[{k}]"] = item;
                    k++;
                    continue;
                }

                var key = item.Substring(0, p).Trim();
                var val = item.Substring(p + nameValueSeparator.Length).Trim();

                // 处理单引号双引号
                if (trimQuotation && !val.IsNullOrEmpty())
                {
                    if (val[0] == '\'' && val[val.Length - 1] == '\'') val = val.Trim('\'');
                    if (val[0] == '"' && val[val.Length - 1] == '"') val = val.Trim('"');
                }

                k++;
                dic[key] = val;
            }

            return dic;
        }

        /// <summary>
        /// 在.netCore需要区分该部分内容
        /// </summary>
        /// <param name="value"></param>
        /// <param name="nameValueSeparator"></param>
        /// <param name="separator"></param>
        /// <param name="trimQuotation"></param>
        /// <returns></returns>
        public static IDictionary<String, String> SplitAsDictionaryT(this String value, Char nameValueSeparator = '=', Char separator = ';', Boolean trimQuotation = false)
        {
            var dic = new NullableDictionary<String, String>(StringComparer.OrdinalIgnoreCase);
            if (value == null || value.IsNullOrWhiteSpace()) return dic;

            //if (nameValueSeparator == null) nameValueSeparator = '=';
            //if (separator == null || separator.Length < 1) separator = new String[] { ",", ";" };

            var ss = value.Split(new[] { separator }, StringSplitOptions.RemoveEmptyEntries);
            if (ss == null || ss.Length < 1) return dic;

            foreach (var item in ss)
            {
                var p = item.IndexOf(nameValueSeparator);
                if (p <= 0) continue;

                var key = item.Substring(0, p).Trim();
                var val = item.Substring(p + 1).Trim();


                // 处理单引号双引号
                if (trimQuotation && !val.IsNullOrEmpty())
                {
                    if (val[0] == '\'' && val[val.Length - 1] == '\'') val = val.Trim('\'');
                    if (val[0] == '"' && val[val.Length - 1] == '"') val = val.Trim('"');
                }

                dic[key] = val;
            }

            return dic;
        }
        #endregion
    }
}

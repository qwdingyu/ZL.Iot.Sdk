using System;

namespace ZL.Shared.Utils
{
    /// <summary>
    /// 字符串距离计算工具（Levenshtein），用于模板匹配与协议分析。
    /// </summary>
    public static class StringDistance
    {
        /// <summary>
        /// 计算 s 和 t 之间的 Levenshtein 编辑距离。
        /// </summary>
        public static int Levenshtein(string s, string t)
        {
            if (string.IsNullOrEmpty(s)) return string.IsNullOrEmpty(t) ? 0 : t.Length;
            if (string.IsNullOrEmpty(t)) return s.Length;

            int n = s.Length;
            int m = t.Length;
            int[,] d = new int[n + 1, m + 1];

            for (int i = 0; i <= n; d[i, 0] = i++) ;
            for (int j = 0; j <= m; d[0, j] = j++) ;

            for (int i = 1; i <= n; i++)
            {
                for (int j = 1; j <= m; j++)
                {
                    int cost = (t[j - 1] == s[i - 1]) ? 0 : 1;
                    d[i, j] = Math.Min(
                        Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                        d[i - 1, j - 1] + cost);
                }
            }
            return d[n, m];
        }

        /// <summary>
        /// 计算 s 和 t 之间的相似度（0.0-1.0，1.0 表示完全相同）。
        /// </summary>
        public static double Similarity(string s, string t)
        {
            int distance = Levenshtein(s, t);
            int maxLength = Math.Max(s.Length, t.Length);
            if (maxLength == 0) return 1.0;
            return 1.0 - (double)distance / maxLength;
        }
    }
}

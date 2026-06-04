using System;
using System.Collections.Generic;
using System.IO;
using ZL.PFLite.Common;
using ZL.PFLite.Net;

namespace ZL.EdgeService
{
    public class Download
    {
        /// <summary>
        /// coding 云平台下载地址
        /// </summary>
        static string CodingBaseUrl = "https://joint.coding.net/p/iot/d/iot/git/raw/master/";
        /// <summary>
        /// coding 云平台IP地址
        /// </summary>
        static string CodingBaseIp = "118.126.70.252";

        /// <summary>
        /// 从本地拷贝文件或从云平台上下载需要dll
        /// </summary>
        /// <param name="dllName">仅文件名，如：ZL.Plc.X.dll</param>
        /// <param name="savePath">下载保存路径</param>
        /// <param name="SaveTempPath">下载保存临时路径</param>
        /// <returns></returns>
        public static bool DownOrCopyFile(string dllName, string savePath, string SaveTempPath, bool useDriverLocal = false)
        {
            bool ok = false;
            dllName = dllName.Trim();
            if (string.IsNullOrEmpty(dllName))
            {
                LogKit.WriteAndTrace($"下载文件名，不能为空，下载失败！");
                return false;
            }
            string saveDir = Path.GetDirectoryName(savePath);
            if (!Directory.Exists(saveDir))
                Directory.CreateDirectory(saveDir);
            string saveTempDir = Path.GetDirectoryName(SaveTempPath);
            if (!Directory.Exists(saveTempDir))
                Directory.CreateDirectory(saveTempDir);
            string verOld = FileVer.GetVersion(savePath);
            //如果是本地路径，拷贝就行了
            if (Path.IsPathRooted(dllName))
            {
                if (!File.Exists(dllName))
                {
                    LogKit.WriteAndTrace($"本地驱动路径【{dllName}】不存在，无法拷贝！");
                    return false;
                }
                string verNew = FileVer.GetVersion(dllName);
                //只有版本比在用版本高，才能执行更新动作！！！
                if (FileVer.CompareVersion(verOld, verNew) == 1)
                {
                    ok = CopyAndReplace(dllName, savePath);
                }
                else
                {
                    ok = true;
                    LogKit.WriteAndTrace($"源{dllName}版本【{verNew}】，目标{savePath}版本【{verOld}】，源版本小于等于目标版本，不执行更新！");
                }
                return ok;
            }
            //相对路径---从托管平台下载，需要有互联网权限
            string url = $"{CodingBaseUrl}{dllName}?download=true";
            //string url = "https://joint.coding.net/p/iot/d/iot/git/raw/master/ZL.PF.dll?download=true";
            bool isConn = false;
            try
            {
                isConn = PingKit.PingIP(CodingBaseIp);
            }
            catch (Exception ex)
            {
                LogKit.WriteAndTrace("PingIP错误，信息:" + ex.Message);
            }
            if (!isConn) { LogKit.WriteAndTrace($"网络无法访问，{dllName}下载失败！"); return false; }
            //下载先暂存
            ok = DownloadKit.downloadHttps(url, SaveTempPath);

            if (ok && File.Exists(SaveTempPath))
            {
                long length = new System.IO.FileInfo(SaveTempPath).Length;
                LogKit.WriteAndTrace($"{dllName}下载成功！存储路径为{SaveTempPath}！文件长度{length}");
                if (length < 10) { return false; }
                //做比对后判断版本，再决定是否覆盖
                string verNew = FileVer.GetVersion(SaveTempPath);
                //只有版本比在用版本高，才能执行更新动作！！！
                if (FileVer.CompareVersion(verOld, verNew) == 1)
                {
                    ok = CopyAndReplace(SaveTempPath, savePath);
                    if (ok)
                    {
                        //如果替换成功，则删除临时文件
                        try { File.Delete(SaveTempPath); } catch { }
                    }
                }
                else
                {
                    LogKit.WriteAndTrace($"源{SaveTempPath}版本【{verNew}】，目标{savePath}版本【{verOld}】，源版本小于等于目标版本，不执行更新！");
                }
            }
            else
                LogKit.WriteAndTrace($"{dllName}下载失败！");
            return ok;
        }

        /// <summary>获取目录内所有符合条件的文件，支持多文件扩展匹配</summary>
        /// <param name="di">目录</param>
        /// <param name="exts">文件扩展列表。比如*.exe;*.dll;*.config</param>
        /// <param name="allSub">是否包含所有子孙目录文件</param>
        /// <returns></returns>
        public static IEnumerable<FileInfo> GetAllFiles(DirectoryInfo di, String exts = null, Boolean allSub = false)
        {
            if (di == null || !di.Exists) yield break;

            if (String.IsNullOrEmpty(exts)) exts = "*";
            var opt = allSub ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

            foreach (var pattern in exts.Split(';', '|', ','))
            {
                foreach (var item in di.GetFiles(pattern, opt))
                {
                    yield return item;
                }
            }
        }
        /// <summary>确保字符串以指定的另一字符串结束，不区分大小写</summary>
        /// <param name="str">字符串</param>
        /// <param name="end"></param>
        /// <returns></returns>
        public static String EnsureEnd(String str, String end)
        {
            if (String.IsNullOrEmpty(end)) return str;
            if (String.IsNullOrEmpty(str)) return end;

            if (str.EndsWith(end, StringComparison.OrdinalIgnoreCase)) return str;

            return str + end;
        }

        /// <summary>合并多段路径</summary>
        /// <param name="path"></param>
        /// <param name="ps"></param>
        /// <returns></returns>
        public static String CombinePath(string path, params string[] ps)
        {
            if (ps == null || ps.Length < 1) return path;
            if (path == null) path = String.Empty;

            //return Path.Combine(path, path2);
            foreach (string item in ps)
            {
                if (!string.IsNullOrEmpty(item)) path = Path.Combine(path, item);
            }
            return path;
        }

        ///// <summary>拷贝并替换。正在使用锁定的文件不可删除，但可以改名</summary>
        ///// <param name="source">源目录</param>
        ///// <param name="dest">目标目录</param>
        public static bool CopyAndReplace(string sourceFilePath, string dest)
        {
            bool ok = false;
            if (!File.Exists(sourceFilePath)) return false;
            DirectoryInfo info = new DirectoryInfo(dest);
            FileInfo sFile = new FileInfo(sourceFilePath);
            try
            {
                // 拷贝覆盖
                sFile.CopyTo(dest, true);
                LogKit.WriteAndTrace($"拷贝【{sourceFilePath}】到【{dest}】！");
            }
            catch
            {
                // 如果是exe/dll，则先改名，因为可能无法覆盖
                //if (dest.EndsWithIgnoreCase(".exe", ".dll") && File.Exists(dest))
                if (File.Exists(dest))
                {
                    //// 先尝试删除
                    //WriteLog("Delete {0}", item);
                    //try
                    //{
                    //    File.Delete(dst);
                    //}
                    //catch
                    //{
                    // 直接Move文件，不要删除，否则Linux上可能导致当前进程退出
                    LogKit.WriteAndTrace(string.Format(@"Move {0}", sFile));
                    var del = dest + ".del";
                    if (File.Exists(del)) File.Delete(del);
                    File.Move(dest, del);
                    //}
                    sFile.CopyTo(dest, true);
                }
            }
            return true;
        }


        ///// <summary>拷贝并替换。正在使用锁定的文件不可删除，但可以改名</summary>
        ///// <param name="source">源目录</param>
        ///// <param name="dest">目标目录</param>
        //public static bool CopyAndReplace(String source, String dest)
        //{
        //    var sourcePath = GetPath(source, 1);

        //    var di = new DirectoryInfo(sourcePath);
        //    // 来源目录根，用于截断
        //    var root = EnsureEnd(di.FullName, Path.DirectorySeparatorChar.ToString());
        //    foreach (var item in GetAllFiles(di, null, true))
        //    {
        //        var name = item.FullName.TrimStart(root);
        //        var dst = GetPath(CombinePath(dest, name), 2);

        //        // 如果是应用配置文件，不要更新
        //        if (dst.EndsWithIgnoreCase(".exe.config")) continue;

        //        // 拷贝覆盖
        //        LogKit.WriteAndTrace(string.Format(@"Copy {0}", item));
        //        try
        //        {
        //            item.CopyTo(dst.EnsureDirectory(true), true);
        //        }
        //        catch
        //        {
        //            // 如果是exe/dll，则先改名，因为可能无法覆盖
        //            if (/*dst.EndsWithIgnoreCase(".exe", ".dll") &&*/ File.Exists(dst))
        //            {
        //                //// 先尝试删除
        //                //WriteLog("Delete {0}", item);
        //                //try
        //                //{
        //                //    File.Delete(dst);
        //                //}
        //                //catch
        //                //{
        //                // 直接Move文件，不要删除，否则Linux上可能导致当前进程退出
        //                LogKit.WriteAndTrace(string.Format(@"Move {0}", item));
        //                var del = dst + ".del";
        //                if (File.Exists(del)) File.Delete(del);
        //                File.Move(dst, del);
        //                //}

        //                item.CopyTo(dst, true);
        //            }
        //        }
        //    }

        //    // 删除临时目录
        //    LogKit.WriteAndTrace(string.Format(@"Delete {0}", di.FullName));
        //    di.Delete(true);
        //}

        private static String GetPath(String path, Int32 mode)
        {
            // 处理路径分隔符，兼容Windows和Linux
            var sep = Path.DirectorySeparatorChar;
            var sep2 = sep == '/' ? '\\' : '/';
            path = path.Replace(sep2, sep);

            var dir = "";
            switch (mode)
            {
                case 1:
                    dir = AppDomain.CurrentDomain.BaseDirectory;
                    break;
                //case 2:
                //    dir = BasePath;
                //    break;
                case 3:
                    dir = Environment.CurrentDirectory;
                    break;
                default:
                    break;
            }
            if (string.IsNullOrEmpty(dir)) return Path.GetFullPath(path);

            // 处理网络路径
            if (path.StartsWith(@"\\", StringComparison.Ordinal)) return Path.GetFullPath(path);

            if (!path.StartsWith(dir))
            {
                // path目录存在，不用再次拼接
                if (!Directory.Exists(path))
                {
                    path = path.TrimStart(sep);
                    path = Path.Combine(dir, path);
                }
            }


            return Path.GetFullPath(path);
        }
    }
}

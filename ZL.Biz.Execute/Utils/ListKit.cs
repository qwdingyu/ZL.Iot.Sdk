using System.Collections.Generic;

namespace ZL.Biz.Execute
{
    public class ListKit
    {
        public static List<List<T>> GetBlockList<T>(List<T> list, int blockSize = 10)
        {
            List<List<T>> result = new List<List<T>>();
            var temp = new List<T>();
            if (blockSize == 1)
            {
                for (int i = 0; i < list.Count; i++)
                {
                    temp.Add(list[i]);
                    result.Add(temp);
                    temp = new List<T>();
                }
            }
            else
            {
                for (int i = 0; i < list.Count; i++)
                {
                    temp.Add(list[i]);
                    //需要重新测试一下?????????????????
                    if (i % blockSize == 0 && i > 0)
                    {
                        result.Add(temp);
                        temp = new List<T>();
                    }
                    //if (i == list.Count - 1)
                    //{
                    //    result.Add(temp);
                    //}
                }
            }
            return result;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ZL.EdgeService
{
    /// <summary>可空字典。获取数据时如果指定键不存在可返回空而不是抛出异常</summary>
    /// <typeparam name="TKey"></typeparam>
    /// <typeparam name="TValue"></typeparam>
    public class NullableDictionary<TKey, TValue> : Dictionary<TKey, TValue>, IDictionary<TKey, TValue>
    {
        /// <summary>实例化一个可空字典</summary>
        public NullableDictionary() { }

        /// <summary>指定比较器实例化一个可空字典</summary>
        /// <param name="comparer"></param>
        public NullableDictionary(IEqualityComparer<TKey> comparer) : base(comparer) { }

        /// <summary>实例化一个可空字典</summary>
        /// <param name="dic"></param>
        /// <param name="comparer"></param>
        public NullableDictionary(IDictionary<TKey, TValue> dic, IEqualityComparer<TKey> comparer) : base(dic, comparer) { }

        /// <summary>获取 或 设置 数据</summary>
        /// <param name="item"></param>
        /// <returns></returns>
        public new TValue this[TKey item]
        {
            get
            {
                TValue v;
                if (TryGetValue(item, out v)) return v;

                return v;
            }
            set
            {
                base[item] = value;
            }
        }
    }
}

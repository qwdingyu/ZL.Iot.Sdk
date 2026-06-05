using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ZL.Iot.Interface
{
    public class BytesKit
    {
        public static byte[] strToBytes(string str, char splitChar = '-')
        {
            byte[] bdata = new byte[0];
            if (str.Length - str.Replace(splitChar.ToString(), "").Length > 0)
            {
                string[] data = str.Split(splitChar);
                bdata = new byte[data.Length];
                for (int i = 0; i < data.Length; i++)
                {
                    bdata[i] = byte.Parse(data[i]);
                }
            }
            return bdata;
        }
    }
}

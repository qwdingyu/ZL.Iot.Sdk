using System;
using System.Runtime.InteropServices;

namespace ZL.Iot.Interface
{
    [StructLayout(LayoutKind.Sequential)]
    public struct DeviceAddress : IComparable<DeviceAddress>
    {
        public string Id;
        public int Area;
        public int Start;
        public ushort DBNumber;
        public ushort DataSize;
        public ushort CacheIndex;
        public byte Bit;
        public DataType VarType;
        public ByteOrder ByteOrder;
        public string Address;
        public int End;
        public string Name;
        public string InfoType;
        public string GroupID;
        //public string Archive;
        //public string Su;
        //public string Sl;
        //public int Cycle;

        public DeviceAddress(string id, int area, ushort dbnumber, ushort cIndex, int start, ushort size, byte bit, DataType type, string address, string name, string infoType, string groupID, ByteOrder order = ByteOrder.None)
        {
            Id = id;
            Area = area;
            DBNumber = dbnumber;
            CacheIndex = cIndex;
            Start = start;
            DataSize = size;
            Bit = bit;
            VarType = type;
            ByteOrder = order;
            Address = address;
            End = Start + size - 1;
            Name = name;
            InfoType = infoType;
            GroupID = groupID;
        }

        public static readonly DeviceAddress Empty = new DeviceAddress("", 0, 0, 0, 0, 0, 0, DataType.NONE, "", "", "", "", ByteOrder.None);

        public int CompareTo(DeviceAddress other)
        {
            // DeviceAddress 是值类型，不需要 null 检查
            
            int cmp = this.Area.CompareTo(other.Area);
            if (cmp != 0) return cmp;
            
            cmp = this.DBNumber.CompareTo(other.DBNumber);
            if (cmp != 0) return cmp;
            
            cmp = this.Start.CompareTo(other.Start);
            if (cmp != 0) return cmp;
            
            return this.Bit.CompareTo(other.Bit);
        }

        public override string ToString()
        {
            return $"Id = {Id}, Area = {Area}, DB = {DBNumber}, Start = {Start}, DataSize = {DataSize}, Bit = {Bit}, VarType = {VarType}, AdrStr = {Address}";
        }

        public object GetDefaultVal()
        {
            object val = null;
            switch (VarType)
            {
                case DataType.BOOL:
                    val = false;
                    break;
                case DataType.BYTE:
                    val = (byte)0;
                    break;
                case DataType.BYTES:
                    val = new byte[DataSize];
                    break;
                case DataType.WORD:
                    val = 0;
                    break;
                case DataType.SHORT:
                    val = 0;
                    break;
                case DataType.DWORD:
                    val = 0;
                    break;
                case DataType.INT:
                    val = 0;
                    break;
                case DataType.FLOAT:
                    val = 0.0f;
                    break;
                case DataType.DOUBLE:
                    val = 0.0;
                    break;
                case DataType.STR:
                    val = "";
                    break;
                default:
                    val = 0;
                    break;
            }
            return val;
        }

        public string GetValStr(object val)
        {
            string valStr = string.Empty;
            if (VarType == DataType.BYTES)
                valStr = string.Join("-", (byte[])val);
            else
                valStr = val.ToString();

            return valStr;
        }
    }
}

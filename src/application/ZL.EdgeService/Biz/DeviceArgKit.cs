using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ZL.Dao.IotDevice;
using ZL.Iot.Interface;
using ZL.PFLite.Common;
using ZL.IotHub.Core;

namespace ZL.EdgeService
{
    public class DeviceArgKit
    {

        /// <summary>
        /// 为相应的驱动增加动态参数
        /// </summary>
        /// <param name="dv"></param>
        /// <param name="dvType"></param>
        /// <param name="device_type_id"></param>
        /// <param name="ip"></param>
        /// <param name="time_out"></param>
        public static IPlcDriver SetDriverArgs(List<iot_device_arg> DeviceArgList, IPlcDriver dv, Type dvType, string device_type_id, string ip, int time_out, out string arg_str)
        {
            arg_str = "";
            try
            {
                if (dv != null)
                {
                    var SelfArgList = DeviceArgList.Where(it => it.device_type_id == device_type_id).ToList();
                    foreach (var arg in SelfArgList)
                    {
                        arg_str = SetProp(dvType, dv, arg.pro_name, arg.pro_value, arg_str);
                    }
                    arg_str = SetProp(dvType, dv, "ServerName", ip, arg_str);
                    //device_type_id类型不能是uuid，因为需要根据整数值赋值给enum类型
                    arg_str = SetProp(dvType, dv, "Device_Type", device_type_id, arg_str);
                    arg_str = SetProp(dvType, dv, "TimeOut", time_out, arg_str);
                }
                if (arg_str.Length > 1)
                    arg_str = arg_str.Substring(0, arg_str.Length - 1);
            }
            catch (Exception ex)
            {
                LogKit.WriteAndTrace("DeviceKit.SetDriverArgs()函数加载错误，错误信息:" + ex.Message);
            }
            return dv;
        }

        /// <summary>
        /// 为相应的驱动增加动态参数
        /// </summary>
        /// <param name="dv"></param>
        /// <param name="dvType"></param>
        /// <param name="device_type_id"></param>
        /// <param name="ip"></param>
        /// <param name="time_out"></param>
        public static IPlcDriver SetDriverArgs(IPlcDriver dv, Type dvType, List<iot_device_arg> SelfArgList, string device_model, string ip, int time_out, out string arg_str)
        {
            arg_str = "";
            try
            {
                if (dv != null)
                {
                    foreach (var arg in SelfArgList)
                    {
                        arg_str = SetProp(dvType, dv, arg.pro_name, arg.pro_value, arg_str);
                    }
                    arg_str = SetProp(dvType, dv, "ServerName", ip, arg_str);
                    arg_str = SetProp(dvType, dv, "deviceType", device_model, arg_str);
                    arg_str = SetProp(dvType, dv, "TimeOut", time_out, arg_str);
                }
                if (arg_str.Length > 1)
                    arg_str = arg_str.Substring(0, arg_str.Length - 1);
            }
            catch (Exception ex)
            {
                LogKit.WriteAndTrace("DeviceKit.SetDriverArgs()函数加载错误，错误信息:" + ex.Message);
            }
            return dv;
        }

        public static string SetProp(Type dvType, IPlcDriver dv, string PropName, object PropVal, string arg_str)
        {
            var propInfo = dvType.GetProperty(PropName);
            if (propInfo != null)
            {
                object tval = null;
                if (propInfo.PropertyType.IsEnum)
                    tval = Enum.Parse(propInfo.PropertyType, PropVal.ToString());
                else
                    tval = Convert.ChangeType(PropVal, propInfo.PropertyType, CultureInfo.CreateSpecificCulture("en-US"));

                propInfo.SetValue(dv, tval, null);
                arg_str += $"{PropName}={tval.ToString()}，";
            }
            return arg_str;
        }
    }
}

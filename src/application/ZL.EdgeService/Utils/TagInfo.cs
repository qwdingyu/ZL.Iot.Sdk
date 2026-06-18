using System.Collections.Generic;
using ZL.Dao.IotDevice;

namespace ZL.EdgeService
{
    public class TagKit
    {
        public static IotTagDto TagChg(iot_tag it)
        {
            return new IotTagDto
            {
                Id = it.id,
                group_id = it.group_id,
                tag_name = it.tag_name,
                Address = it.address,
                DataType = it.data_type,
                data_size = it.data_size,
                TagType = it.tag_type,
                DataSource = it.data_source,
                enable_int = it.is_active != 0 ? 1 : 0,
                InfoType = it.info_type,
                preset = it.preset,
                set_type = it.set_type,
                archive = it.archive,
                su = it.su,
                sl = it.sl,
                cycle = it.cycle
            };
        }

        public static List<IotTagDto> TagChg(List<iot_tag> TagList)
        {
            List<IotTagDto> iotTagList = new List<IotTagDto>();
            foreach (var it in TagList)
            {
                iotTagList.Add(TagChg(it));
            }
            return iotTagList;
        }
    }

}

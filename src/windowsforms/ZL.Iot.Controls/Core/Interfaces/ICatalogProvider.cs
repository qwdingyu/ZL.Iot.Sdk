using System.Collections.Generic;

namespace ZL.Iot.Controls.Core.Interfaces
{
    /// <summary>
    /// 协议目录数据源接口
    /// 用于左侧导航树的协议列表。支持硬编码和 JSON 两种实现
    /// </summary>
    public interface ICatalogProvider
    {
        /// <summary>获取协议分组列表</summary>
        IReadOnlyList<CatalogGroup> GetGroups();
    }

    /// <summary>协议分组（对应左侧树的一级节点）</summary>
    public class CatalogGroup
    {
        public string Key { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public List<CatalogItem> Items { get; set; } = new();
    }

    /// <summary>协议条目（对应左侧树的二级节点）</summary>
    public class CatalogItem
    {
        public string Key { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string ProtocolType { get; set; } = string.Empty;
        public string DefaultPort { get; set; } = string.Empty;
        public CatalogTabMode TabMode { get; set; } = CatalogTabMode.AlwaysNew;
        public string TabKey { get; set; } = string.Empty;
    }

    /// <summary>Tab 页打开模式</summary>
    public enum CatalogTabMode
    {
        /// <summary>每次点击创建新 Tab（适用于 PLC 设备，可多开）</summary>
        AlwaysNew,
        /// <summary>复用已有 Tab（适用于工具类，如地址计算器）</summary>
        ReuseExisting,
    }
}
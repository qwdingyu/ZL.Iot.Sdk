using System.Drawing;

namespace ZL.Iot.Controls.Theme
{
    /// <summary>
    /// ZL.Iot.WinForms.Controls 统一颜色系统
    /// 所有控件使用此主题颜色，确保全局视觉一致性
    /// </summary>
    public static class AppTheme
    {
        // ===== 主色调 =====
        /// <summary>品牌蓝色，用于导航栏高亮、关键按钮</summary>
        public static Color Accent    = Color.FromArgb(0x02, 0x94, 0xFF);
        /// <summary>悬停态</summary>
        public static Color AccentHov = Color.FromArgb(0x01, 0x7A, 0xD5);
        /// <summary>禁用态</summary>
        public static Color AccentDis = Color.FromArgb(0x99, 0xCD, 0xFF);

        // ===== 背景色 =====
        /// <summary>窗口纯白背景</summary>
        public static Color BgWindow  = Color.FromArgb(0xFF, 0xFF, 0xFF);
        /// <summary>面板浅灰背景</summary>
        public static Color BgPanel   = Color.FromArgb(0xF9, 0xF9, 0xF9);
        /// <summary>工具栏背景</summary>
        public static Color BgToolbar = Color.FromArgb(0xF3, 0xF3, 0xF3);
        /// <summary>状态栏背景</summary>
        public static Color BgStatus  = Color.FromArgb(0xF9, 0xF9, 0xF9);

        // ===== 边框 =====
        /// <summary>分隔线/边框色</summary>
        public static Color Border    = Color.FromArgb(0xE5, 0xE5, 0xE5);

        // ===== 文字 =====
        /// <summary>主文字黑色</summary>
        public static Color Text      = Color.FromArgb(0x00, 0x00, 0x00);
        /// <summary>次要文字灰色</summary>
        public static Color TextSec   = Color.FromArgb(0x66, 0x66, 0x66);

        // ===== 日志颜色 =====
        public static Color LogInfo    = Color.FromArgb(0x55, 0x55, 0x55);
        public static Color LogSuccess = Color.FromArgb(0x00, 0x88, 0x00);
        public static Color LogError   = Color.FromArgb(0xCC, 0x00, 0x00);
        public static Color LogWarn    = Color.FromArgb(0xCC, 0x88, 0x00);
        /// <summary>原始报文蓝色</summary>
        public static Color LogRaw     = Color.FromArgb(0x00, 0x66, 0xAA);

        // ===== 连接状态灯 =====
        public static Color LampGreen  = Color.FromArgb(0x00, 0xCC, 0x00);
        public static Color LampYellow = Color.FromArgb(0xCC, 0xCC, 0x00);
        public static Color LampRed    = Color.FromArgb(0xCC, 0x00, 0x00);
    }
}
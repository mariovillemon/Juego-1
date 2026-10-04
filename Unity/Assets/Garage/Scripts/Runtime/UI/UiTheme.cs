using UnityEngine;

namespace Garage.Unity.UI
{
    /// <summary>
    /// The UI "stylesheet": sober colours and sizes of real workshop software (dark slate panels, one amber accent
    /// for warnings, green for OK), see docs/VISUAL_STYLE.md § Interfaz. Every panel takes its look from here.
    /// </summary>
    public static class UiTheme
    {
        public static readonly Color Backdrop = new Color(0f, 0f, 0f, 0.45f);
        public static readonly Color Panel = new Color(0.11f, 0.12f, 0.13f, 0.97f);
        public static readonly Color PanelAlt = new Color(0.15f, 0.16f, 0.18f, 1f);
        public static readonly Color Header = new Color(0.07f, 0.08f, 0.09f, 1f);
        public static readonly Color Row = new Color(0.17f, 0.18f, 0.2f, 1f);
        public static readonly Color RowSelected = new Color(0.22f, 0.3f, 0.38f, 1f);
        public static readonly Color Button = new Color(0.24f, 0.26f, 0.29f, 1f);
        public static readonly Color ButtonHover = new Color(0.31f, 0.34f, 0.38f, 1f);
        public static readonly Color ButtonPrimary = new Color(0.16f, 0.36f, 0.55f, 1f);
        public static readonly Color ButtonDanger = new Color(0.5f, 0.16f, 0.14f, 1f);
        public static readonly Color Text = new Color(0.88f, 0.89f, 0.9f, 1f);
        public static readonly Color TextDim = new Color(0.6f, 0.62f, 0.65f, 1f);
        public static readonly Color Accent = new Color(1f, 0.69f, 0.0f, 1f);
        public static readonly Color Ok = new Color(0.45f, 0.8f, 0.45f, 1f);
        public static readonly Color Bad = new Color(0.95f, 0.4f, 0.35f, 1f);
        public static readonly Color HudBack = new Color(0.05f, 0.06f, 0.07f, 0.72f);

        public const float FontSmall = 15f;
        public const float FontBody = 18f;
        public const float FontTitle = 26f;
        public const float FontHud = 19f;
        public const float RowHeight = 34f;
        public const float Padding = 12f;

        /// <summary>Hex for rich text.</summary>
        public static string Hex(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c);
    }
}

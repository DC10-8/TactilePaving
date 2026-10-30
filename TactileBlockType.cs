using UnityEngine;
using static TactilePaving.Localization;

namespace TactilePaving
{
    public enum TactileBlockType
    {
        GuideYellow = 0,
        WarningYellow = 1,
        GuideGray = 2,
        WarningGray = 3
    }

    internal static class TactileBlockTypes
    {
        // 日本の点字ブロックは1枚300mm角
        public const float BlockSize = 0.3f;

        public const int Count = 4;

        public static TactileBlockType Clamp(int value)
        {
            if (value < 0 || value >= Count)
            {
                return TactileBlockType.GuideYellow;
            }

            return (TactileBlockType)value;
        }

        public static bool IsWarning(TactileBlockType type)
        {
            return type == TactileBlockType.WarningYellow || type == TactileBlockType.WarningGray;
        }

        public static Color BaseColor(TactileBlockType type)
        {
            switch (type)
            {
                case TactileBlockType.GuideGray:
                case TactileBlockType.WarningGray:
                    return new Color(0.75f, 0.75f, 0.75f);
                default:
                    return new Color(1f, 0.85f, 0f);
            }
        }

        public static string DisplayName(TactileBlockType type)
        {
            switch (type)
            {
                case TactileBlockType.WarningYellow:
                    return T("点状ブロック（警告用・黄色）", "Warning blocks (dots, yellow)");
                case TactileBlockType.GuideGray:
                    return T("線状ブロック（誘導用・グレー）", "Guide blocks (bars, gray)");
                case TactileBlockType.WarningGray:
                    return T("点状ブロック（警告用・グレー）", "Warning blocks (dots, gray)");
                default:
                    return T("線状ブロック（誘導用・黄色）", "Guide blocks (bars, yellow)");
            }
        }

        public static string[] DisplayNames()
        {
            string[] names = new string[Count];
            for (int i = 0; i < Count; i++)
            {
                names[i] = DisplayName((TactileBlockType)i);
            }

            return names;
        }
    }
}

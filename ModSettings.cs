using System;
using ColossalFramework;

namespace TactilePaving
{
    internal static class ModSettings
    {
        public const int MinRows = 1;
        public const int MaxRows = 3;
        public const int DefaultRows = 1;

        private const string SettingsFileName = "TactilePaving";
        private const string BlockTypeKey = "BlockType";
        private const string RowsKey = "Rows";
        private const string LanguageKey = "Language";

        // -1: 自動（ゲームの言語に合わせる）
        private const int LanguageAuto = -1;

        private static readonly SavedInt SavedBlockType;
        private static readonly SavedInt SavedRows;
        private static readonly SavedInt SavedLanguage;

        static ModSettings()
        {
            EnsureSettingsFile();
            SavedBlockType = new SavedInt(
                BlockTypeKey,
                SettingsFileName,
                (int)TactileBlockType.GuideYellow,
                true);
            SavedRows = new SavedInt(
                RowsKey,
                SettingsFileName,
                DefaultRows,
                true);
            SavedLanguage = new SavedInt(
                LanguageKey,
                SettingsFileName,
                LanguageAuto,
                true);
        }

        // 値の意味は Localization.Language を参照
        public static int LanguageValue
        {
            get
            {
                return SavedLanguage.value;
            }
            set
            {
                SavedLanguage.value = value;
                GameSettings.SaveAll();
            }
        }

        public static TactileBlockType BlockType
        {
            get
            {
                return TactileBlockTypes.Clamp(SavedBlockType.value);
            }
            set
            {
                SavedBlockType.value = (int)TactileBlockTypes.Clamp((int)value);
                GameSettings.SaveAll();
            }
        }

        public static int Rows
        {
            get
            {
                return ClampRows(SavedRows.value);
            }
            set
            {
                SavedRows.value = ClampRows(value);
                GameSettings.SaveAll();
            }
        }

        private static void EnsureSettingsFile()
        {
            if (GameSettings.FindSettingsFileByName(SettingsFileName) != null)
            {
                return;
            }

            GameSettings.AddSettingsFile(new[]
            {
                new SettingsFile
                {
                    fileName = SettingsFileName
                }
            });
        }

        private static int ClampRows(int value)
        {
            return Math.Max(MinRows, Math.Min(MaxRows, value));
        }
    }
}

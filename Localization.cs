using System;
using ColossalFramework;
using ColossalFramework.Globalization;
using UnityEngine;

namespace TactilePaving
{
    internal enum UILanguage
    {
        Japanese = 0,
        English = 1
    }

    // 表示言語の切り替え。文字列は T("日本語", "English") の形でその場に両方書く
    internal static class Localization
    {
        // 言語が切り替わったときに、表示中のUIを作り直すための通知
        public static event Action Changed;

        public static UILanguage Language
        {
            get
            {
                int saved = ModSettings.LanguageValue;
                if (saved == (int)UILanguage.Japanese || saved == (int)UILanguage.English)
                {
                    return (UILanguage)saved;
                }

                return DetectGameLanguage();
            }
            set
            {
                if (value == Language && ModSettings.LanguageValue == (int)value)
                {
                    return;
                }

                ModSettings.LanguageValue = (int)value;
                Action handler = Changed;
                if (handler != null)
                {
                    handler();
                }
            }
        }

        public static bool IsJapanese
        {
            get { return Language == UILanguage.Japanese; }
        }

        public static string T(string japanese, string english)
        {
            return IsJapanese ? japanese : english;
        }

        // 設定がまだないときは、ゲームの言語が日本語なら日本語、それ以外は英語
        private static UILanguage DetectGameLanguage()
        {
            try
            {
                string language = SingletonLite<LocaleManager>.instance.language;
                if (!string.IsNullOrEmpty(language) && language.StartsWith("ja", StringComparison.OrdinalIgnoreCase))
                {
                    return UILanguage.Japanese;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[TactilePaving] Failed to detect the game language: " + e.Message);
            }

            return UILanguage.English;
        }
    }
}

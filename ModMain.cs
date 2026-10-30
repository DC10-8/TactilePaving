using System.Collections.Generic;
using ColossalFramework.UI;
using ICities;
using UnityEngine;
using static TactilePaving.Localization;

namespace TactilePaving
{
    public sealed class ModMain : IUserMod
    {
        private static UIHelper settingsHelper;

        // 名前はコンテンツマネージャーで探しやすいよう、言語によらず両方を表示する
        public string Name
        {
            get { return "Tactile Paving (点字ブロック敷設)"; }
        }

        public string Description
        {
            get
            {
                return T(
                    "UUIのアイコンから、道路の歩道上に点字ブロックを敷設できます。",
                    "Lay tactile paving on road sidewalks from a UUI button.");
            }
        }

        public void OnSettingsUI(UIHelperBase helper)
        {
            settingsHelper = helper as UIHelper;
            BuildSettings(helper);
        }

        private static void BuildSettings(UIHelperBase helper)
        {
            UIHelperBase languageGroup = helper.AddGroup("Language / 言語");
            languageGroup.AddDropdown(
                "Language / 言語",
                new[] { "日本語 (Japanese)", "English" },
                (int)Localization.Language,
                index =>
                {
                    Localization.Language = (UILanguage)index;

                    // 選択中のドロップダウン自身も作り直すので、次のフレームまで待つ
                    DeferredAction.Run(RebuildSettings);
                });

            UIHelperBase group = helper.AddGroup(T("点字ブロック敷設", "Tactile Paving"));

            group.AddDropdown(
                T("敷設する点字ブロックの種類", "Block type to lay"),
                TactileBlockTypes.DisplayNames(),
                (int)ModSettings.BlockType,
                index =>
                {
                    ModSettings.BlockType = (TactileBlockType)index;
                });

            group.AddSlider(
                T("ブロックの列数", "Number of rows"),
                ModSettings.MinRows,
                ModSettings.MaxRows,
                1f,
                ModSettings.Rows,
                value =>
                {
                    ModSettings.Rows = (int)value;
                });

            UIHelperBase usage = helper.AddGroup(T("使い方", "How to use"));
            AddLabel(usage, T(
                "UUIのアイコン: ツールのON/OFF（ON の間は設定パネルを表示）",
                "UUI button: turn the tool on/off (the settings panel is shown while it is on)"));
            AddLabel(usage, T(
                "左クリック: 1回目で開始地点、2回目で終了地点を決めて敷設",
                "Left-click: first click sets the start point, second click sets the end point and lays the blocks"));
            AddLabel(usage, T(
                "右クリック: 敷設中はキャンセル、それ以外はカーソル下のブロックを1枚削除",
                "Right-click: cancel while laying, otherwise remove the single block under the cursor"));
            AddLabel(usage, T(
                "Ctrl+Z: 直前に敷設したブロックを取り消し",
                "Ctrl+Z: undo the last placed strip"));
        }

        // 言語を切り替えたら、オプション画面をその場で作り直す
        private static void RebuildSettings()
        {
            UIComponent root = settingsHelper != null ? settingsHelper.self as UIComponent : null;
            if (root == null)
            {
                return;
            }

            foreach (UIComponent child in new List<UIComponent>(root.components))
            {
                root.RemoveUIComponent(child);
                Object.Destroy(child.gameObject);
            }

            BuildSettings(settingsHelper);
        }

        private static void AddLabel(UIHelperBase group, string text)
        {
            UIHelper uiHelper = group as UIHelper;
            UIComponent parent = uiHelper != null ? uiHelper.self as UIComponent : null;
            if (parent == null)
            {
                return;
            }

            UILabel label = parent.AddUIComponent<UILabel>();
            label.text = text;
            label.textScale = 0.9f;
        }
    }
}

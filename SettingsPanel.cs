using ColossalFramework.UI;
using UnityEngine;
using static TactilePaving.Localization;

namespace TactilePaving
{
    // ツールがONの間だけ表示する設定パネル。ブロックの種類と列数を選べる
    internal sealed class SettingsPanel : UIPanel
    {
        private const float PanelWidth = 300f;
        private const float Padding = 10f;
        private const float TitleHeight = 40f;
        private const float ButtonHeight = 34f;
        private const float ButtonSpacing = 4f;
        private const float PreviewSize = 26f;

        private static readonly Vector3 DefaultPosition = new Vector3(10f, 120f);

        private static SettingsPanel instance;

        // 言語切り替えで作り直したときも、ドラッグした位置を保つ
        private static Vector3 savedPosition = DefaultPosition;

        private readonly UIButton[] typeButtons = new UIButton[TactileBlockTypes.Count];
        private readonly UIButton[] rowButtons = new UIButton[ModSettings.MaxRows - ModSettings.MinRows + 1];
        private readonly Texture2D[] previews = new Texture2D[TactileBlockTypes.Count];

        public static void Create()
        {
            if (instance != null)
            {
                return;
            }

            UIView view = UIView.GetAView();
            if (view == null)
            {
                return;
            }

            instance = view.AddUIComponent(typeof(SettingsPanel)) as SettingsPanel;
            instance.isVisible = false;
            Localization.Changed += OnLanguageChanged;
        }

        public static void DestroyPanel()
        {
            Localization.Changed -= OnLanguageChanged;
            savedPosition = DefaultPosition;
            if (instance != null)
            {
                Destroy(instance.gameObject);
                instance = null;
            }
        }

        // 文字はすべて作成時に入るので、言語が変わったらパネルごと作り直す
        private static void OnLanguageChanged()
        {
            if (instance == null)
            {
                return;
            }

            bool visible = instance.isVisible;
            savedPosition = instance.relativePosition;
            UIView view = instance.GetUIView();
            Destroy(instance.gameObject);

            instance = view.AddUIComponent(typeof(SettingsPanel)) as SettingsPanel;
            instance.isVisible = false;
            SetVisible(visible);
        }

        public static void SetVisible(bool visible)
        {
            if (instance == null)
            {
                return;
            }

            instance.isVisible = visible;
            if (visible)
            {
                instance.Refresh();
                instance.BringToFront();
            }
        }

        public override void Start()
        {
            base.Start();

            name = "TactilePavingSettingsPanel";
            backgroundSprite = "MenuPanel2";
            width = PanelWidth;
            relativePosition = savedPosition;

            UILabel title = AddUIComponent<UILabel>();
            title.text = T("点字ブロック設定", "Tactile Paving");
            title.relativePosition = new Vector3(Padding + 2f, 12f);

            UIDragHandle dragHandle = AddUIComponent<UIDragHandle>();
            dragHandle.target = this;
            dragHandle.size = new Vector2(PanelWidth - 40f, TitleHeight);
            dragHandle.relativePosition = Vector3.zero;

            UIButton close = AddUIComponent<UIButton>();
            close.size = new Vector2(32f, 32f);
            close.relativePosition = new Vector3(PanelWidth - 36f, 4f);
            close.normalBgSprite = "buttonclose";
            close.hoveredBgSprite = "buttonclosehover";
            close.pressedBgSprite = "buttonclosepressed";
            close.eventClick += (component, e) => ToolsModifierControl.toolController.CurrentTool = ToolsModifierControl.GetTool<DefaultTool>();

            float y = TitleHeight + 4f;
            y = AddSectionLabel(T("種類", "Block type"), y);
            float buttonWidth = PanelWidth - Padding * 2f;
            for (int i = 0; i < typeButtons.Length; i++)
            {
                TactileBlockType type = (TactileBlockType)i;
                UIButton button = CreateButton(TactileBlockTypes.DisplayName(type), new Vector3(Padding, y), buttonWidth);
                button.textHorizontalAlignment = UIHorizontalAlignment.Left;
                button.textPadding = new RectOffset((int)(PreviewSize + 14f), 0, 4, 0);
                button.eventClick += (component, e) =>
                {
                    ModSettings.BlockType = type;
                    Refresh();
                };

                previews[i] = TactileTextures.CreateBlockTexture(type, false);
                UITextureSprite preview = button.AddUIComponent<UITextureSprite>();
                preview.texture = previews[i];
                preview.size = new Vector2(PreviewSize, PreviewSize);
                preview.relativePosition = new Vector3(6f, (ButtonHeight - PreviewSize) * 0.5f);
                preview.isInteractive = false;

                typeButtons[i] = button;
                y += ButtonHeight + ButtonSpacing;
            }

            y = AddSectionLabel(T("列数（1列 = 300mm）", "Rows (1 row = 300 mm)"), y + 4f);
            float rowWidth = (buttonWidth - ButtonSpacing * (rowButtons.Length - 1)) / rowButtons.Length;
            for (int i = 0; i < rowButtons.Length; i++)
            {
                int rows = ModSettings.MinRows + i;
                UIButton button = CreateButton(T(rows + "列", rows == 1 ? "1 row" : rows + " rows"), new Vector3(Padding + i * (rowWidth + ButtonSpacing), y), rowWidth);
                button.eventClick += (component, e) =>
                {
                    ModSettings.Rows = rows;
                    Refresh();
                };

                rowButtons[i] = button;
            }

            y += ButtonHeight + Padding;
            UILabel hint = AddUIComponent<UILabel>();
            hint.text = T("右クリック: ブロックを1枚ずつ削除", "Right-click: remove blocks one by one");
            hint.textScale = 0.8f;
            hint.relativePosition = new Vector3(Padding + 2f, y);
            height = y + 24f;

            Refresh();
        }

        public override void OnDestroy()
        {
            foreach (Texture2D preview in previews)
            {
                if (preview != null)
                {
                    Destroy(preview);
                }
            }

            base.OnDestroy();
        }

        private void Refresh()
        {
            for (int i = 0; i < typeButtons.Length; i++)
            {
                SetSelected(typeButtons[i], (int)ModSettings.BlockType == i);
            }

            for (int i = 0; i < rowButtons.Length; i++)
            {
                SetSelected(rowButtons[i], ModSettings.Rows == ModSettings.MinRows + i);
            }
        }

        private float AddSectionLabel(string text, float y)
        {
            UILabel label = AddUIComponent<UILabel>();
            label.text = text;
            label.textScale = 0.85f;
            label.relativePosition = new Vector3(Padding + 2f, y);
            return y + 22f;
        }

        private UIButton CreateButton(string text, Vector3 position, float buttonWidth)
        {
            UIButton button = AddUIComponent<UIButton>();
            button.size = new Vector2(buttonWidth, ButtonHeight);
            button.relativePosition = position;
            button.text = text;
            button.textScale = 0.85f;
            button.hoveredBgSprite = "ButtonMenuHovered";
            button.pressedBgSprite = "ButtonMenuPressed";
            return button;
        }

        private static void SetSelected(UIButton button, bool selected)
        {
            if (button == null)
            {
                return;
            }

            string sprite = selected ? "ButtonMenuFocused" : "ButtonMenu";
            button.normalBgSprite = sprite;
            button.focusedBgSprite = sprite;
            button.textColor = selected ? new Color32(255, 255, 255, 255) : new Color32(200, 200, 200, 255);
        }
    }
}

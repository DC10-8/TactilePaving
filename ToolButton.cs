using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using ColossalFramework;
using ColossalFramework.UI;
using UnifiedUI.Helpers;
using UnityEngine;

namespace TactilePaving
{
    // ツールのON/OFFボタン。UUIへの登録を試し、だめならゲーム画面に独自のボタンを出す
    internal sealed class ToolButton : MonoBehaviour
    {
        private const string ButtonName = "TactilePaving";
        private const string ButtonGroup = "TactilePaving";

        private static string ButtonTooltip
        {
            get { return Localization.T("点字ブロック敷設 (ON/OFF)", "Tactile Paving (ON/OFF)"); }
        }
        private const string IconFileName = "TactilePavingIcon.png";
        private const float FallbackButtonSize = 46f;
        private const string UUILibName = "UnifiedUILib";
        private const float PanelScanInterval = 2f;
        private const float PanelScanDuration = 120f;

        public TactilePavingTool Tool;

        private Texture2D icon;
        private UIComponent uuiButton;
        private UUICustomButton uuiCustomButton;
        private UIButton fallbackButton;

        // 他のモッドが古い UnifiedUILib を直接使うと、UUIのパネルが別にもう1つ作られる。
        // どちらのパネルを開いてもアイコンが見えるよう、既にあるパネルすべてに登録する
        private readonly List<UIComponent> extraButtons = new List<UIComponent>();
        private readonly List<Assembly> registeredLibs = new List<Assembly>();
        private float nextPanelScan;
        private float panelScanEnd;

        // Start は全モッドの OnLevelLoaded の後に呼ばれるので、UUI の準備が済んでから登録できる
        private void Start()
        {
            icon = TactileTextures.CreateIcon();
            Localization.Changed += UpdateTooltips;

            uuiButton = RegisterUUIButton();
            if (uuiButton != null)
            {
                registeredLibs.Add(uuiButton.GetType().Assembly);
                Debug.Log("[TactilePaving] UUIにボタンを登録しました: " + uuiButton.GetType().Assembly.FullName);
            }

            // パネルが後から作られることもあるので、しばらくの間は定期的に探す
            panelScanEnd = Time.realtimeSinceStartup + PanelScanDuration;
            RegisterInOtherPanels();

            if (uuiButton == null && extraButtons.Count == 0)
            {
                CreateFallbackButton();
            }
        }

        private void Update()
        {
            if (Time.realtimeSinceStartup < panelScanEnd && Time.realtimeSinceStartup >= nextPanelScan)
            {
                RegisterInOtherPanels();
            }

            if (fallbackButton == null)
            {
                return;
            }

            bool active = Tool != null && ToolsModifierControl.toolController.CurrentTool == Tool;
            fallbackButton.normalBgSprite = active ? "OptionBaseFocused" : "OptionBase";
        }

        private void OnDestroy()
        {
            Localization.Changed -= UpdateTooltips;

            if (uuiButton != null)
            {
                UUIHelpers.Destroy(uuiButton);
                uuiButton = null;
            }

            foreach (UIComponent button in extraButtons)
            {
                if (button != null)
                {
                    UUIHelpers.Destroy(button);
                }
            }

            extraButtons.Clear();

            if (fallbackButton != null)
            {
                Destroy(fallbackButton.gameObject);
                fallbackButton = null;
            }

            if (icon != null)
            {
                Destroy(icon);
                icon = null;
            }
        }

        // 読み込まれている UnifiedUILib のうち、パネルが既にあるものにボタンを追加する。
        // パネルがないものには登録しない（登録するとパネルが新しく作られてしまうため）
        private void RegisterInOtherPanels()
        {
            nextPanelScan = Time.realtimeSinceStartup + PanelScanInterval;

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.GetName().Name != UUILibName || registeredLibs.Contains(assembly))
                {
                    continue;
                }

                MethodInfo register;
                if (!HasMainPanel(assembly) || (register = FindRegisterMethod(assembly)) == null)
                {
                    continue;
                }

                registeredLibs.Add(assembly);
                try
                {
                    UIComponent button = register.Invoke(
                        null,
                        new object[] { ButtonName, ButtonGroup, ButtonTooltip, Tool, icon, null, null }) as UIComponent;
                    if (button == null)
                    {
                        Debug.LogWarning("[TactilePaving] 別のUUIパネルへの登録で null が返りました: " + assembly.FullName);
                        continue;
                    }

                    extraButtons.Add(button);
                    Debug.Log("[TactilePaving] 別のUUIパネルにもボタンを登録しました: " + assembly.FullName);
                    RemoveFallbackButton();
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[TactilePaving] 別のUUIパネルへの登録に失敗しました: " + assembly.FullName + "\n" + e);
                }
            }
        }

        private static bool HasMainPanel(Assembly assembly)
        {
            try
            {
                Type panelType = assembly.GetType("UnifiedUI.GUI.MainPanel", false);
                PropertyInfo exists = panelType != null
                    ? panelType.GetProperty("Exists", BindingFlags.Public | BindingFlags.Static)
                    : null;
                return exists != null && (bool)exists.GetValue(null, null);
            }
            catch (Exception)
            {
                return false;
            }
        }

        // UUIAPI.Register(name, group, tooltip, ToolBase, Texture2D, SavedInputKey, Dictionary) を探す
        private static MethodInfo FindRegisterMethod(Assembly assembly)
        {
            Type apiType = assembly.GetType("UnifiedUI.API.UUIAPI", false);
            if (apiType == null)
            {
                return null;
            }

            foreach (MethodInfo method in apiType.GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                ParameterInfo[] parameters = method.GetParameters();
                if (method.Name == "Register" &&
                    parameters.Length == 7 &&
                    parameters[3].ParameterType == typeof(ToolBase) &&
                    parameters[4].ParameterType == typeof(Texture2D) &&
                    typeof(UIComponent).IsAssignableFrom(method.ReturnType))
                {
                    return method;
                }
            }

            return null;
        }

        private void UpdateTooltips()
        {
            string tooltip = ButtonTooltip;
            if (uuiButton != null)
            {
                uuiButton.tooltip = tooltip;
            }

            foreach (UIComponent button in extraButtons)
            {
                if (button != null)
                {
                    button.tooltip = tooltip;
                }
            }

            if (fallbackButton != null)
            {
                fallbackButton.tooltip = tooltip;
            }
        }

        private void RemoveFallbackButton()
        {
            if (fallbackButton != null)
            {
                Destroy(fallbackButton.gameObject);
                fallbackButton = null;
            }
        }

        private UIComponent RegisterUUIButton()
        {
            // 新しいUUIは本体 (UnifiedUIMod) と API (UnifiedUILib) が別アイテムに分かれており、
            // IsUUIEnabled() は false を返す。登録処理自体は読み込み済みの最新 UnifiedUILib を
            // 使って動くので、ここでは判定に使わずログに残すだけにする
            try
            {
                Debug.Log("[TactilePaving] UUIHelpers.IsUUIEnabled() = " + UUIHelpers.IsUUIEnabled());
            }
            catch (Exception e)
            {
                Debug.LogWarning("[TactilePaving] UUIの確認に失敗しました: " + e.Message);
            }

            // 新しいUUI: テクスチャを直接渡す
            try
            {
                UIComponent button = UUIHelpers.RegisterToolButton(ButtonName, ButtonGroup, ButtonTooltip, Tool, icon, null);
                if (button != null)
                {
                    return button;
                }

                Debug.LogWarning("[TactilePaving] UUIへの登録（テクスチャ指定）で null が返りました");
            }
            catch (Exception e)
            {
                Debug.LogWarning("[TactilePaving] UUIへの登録（テクスチャ指定）に失敗しました: " + e);
            }

            // 古いUUI: PNGファイルのパスを渡す
            try
            {
                string iconPath = WriteIconFile();
#pragma warning disable 612
                UIComponent button = UUIHelpers.RegisterToolButton(
                    ButtonName,
                    ButtonGroup,
                    ButtonTooltip,
                    iconPath,
                    Tool,
                    (SavedInputKey)null,
                    (IEnumerable<SavedInputKey>)null);
#pragma warning restore 612
                if (button != null)
                {
                    return button;
                }

                Debug.LogWarning("[TactilePaving] UUIへの登録（画像ファイル指定）で null が返りました");
            }
            catch (Exception e)
            {
                Debug.LogWarning("[TactilePaving] UUIへの登録（画像ファイル指定）に失敗しました: " + e);
            }

            // ツールを渡さないカスタムボタン。ON/OFFの切り替えはこちらで行う
            try
            {
                uuiCustomButton = UUIHelpers.RegisterCustomButton(
                    ButtonName,
                    ButtonGroup,
                    ButtonTooltip,
                    icon,
                    OnCustomButtonToggled,
                    OnToolChanged,
                    null);
                if (uuiCustomButton != null && uuiCustomButton.Button != null)
                {
                    return uuiCustomButton.Button;
                }

                Debug.LogWarning("[TactilePaving] UUIへの登録（カスタムボタン）で null が返りました");
            }
            catch (Exception e)
            {
                Debug.LogError("[TactilePaving] UUIへの登録（カスタムボタン）に失敗しました: " + e);
            }

            uuiCustomButton = null;
            return null;
        }

        private string WriteIconFile()
        {
            string directory = Path.Combine(Path.GetTempPath(), "TactilePaving");
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, IconFileName);
            File.WriteAllBytes(path, icon.EncodeToPNG());
            return path;
        }

        // UUIが使えないときの予備ボタン（画面左上）
        private void CreateFallbackButton()
        {
            UIView view = UIView.GetAView();
            if (view == null)
            {
                return;
            }

            fallbackButton = view.AddUIComponent(typeof(UIButton)) as UIButton;
            fallbackButton.name = "TactilePavingButton";
            fallbackButton.size = new Vector2(FallbackButtonSize, FallbackButtonSize);
            fallbackButton.relativePosition = new Vector3(120f, 60f);
            fallbackButton.normalBgSprite = "OptionBase";
            fallbackButton.hoveredBgSprite = "OptionBaseHovered";
            fallbackButton.pressedBgSprite = "OptionBasePressed";
            fallbackButton.tooltip = ButtonTooltip;
            fallbackButton.eventClick += (component, e) => ToggleTool();

            UITextureSprite sprite = fallbackButton.AddUIComponent<UITextureSprite>();
            sprite.texture = icon;
            sprite.size = new Vector2(FallbackButtonSize - 8f, FallbackButtonSize - 8f);
            sprite.relativePosition = new Vector3(4f, 4f);
            sprite.isInteractive = false;

            Debug.Log("[TactilePaving] UUIの代わりに画面左上へボタンを表示しました");
        }

        private void OnCustomButtonToggled(bool active)
        {
            ToolController toolController = ToolsModifierControl.toolController;
            if (active)
            {
                toolController.CurrentTool = Tool;
            }
            else if (toolController.CurrentTool == Tool)
            {
                toolController.CurrentTool = ToolsModifierControl.GetTool<DefaultTool>();
            }
        }

        // 他のツールに切り替わったらボタンの押下状態を戻す
        private void OnToolChanged(ToolBase newTool)
        {
            if (uuiCustomButton != null)
            {
                uuiCustomButton.IsPressed = newTool == Tool;
            }
        }

        private void ToggleTool()
        {
            ToolController toolController = ToolsModifierControl.toolController;
            if (toolController.CurrentTool == Tool)
            {
                toolController.CurrentTool = ToolsModifierControl.GetTool<DefaultTool>();
            }
            else
            {
                toolController.CurrentTool = Tool;
            }
        }
    }
}

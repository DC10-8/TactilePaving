using ICities;
using UnityEngine;

namespace TactilePaving
{
    public sealed class LoadingExtension : LoadingExtensionBase
    {
        private static GameObject modObject;
        private static TactilePavingTool tool;

        public override void OnLevelLoaded(LoadMode mode)
        {
            if (managers.loading.currentMode != AppMode.Game)
            {
                return;
            }

            Debug.Log("[TactilePaving] 読み込み開始");

            ToolController toolController = ToolsModifierControl.toolController;
            tool = toolController.gameObject.AddComponent<TactilePavingTool>();
            tool.enabled = false;

            SettingsPanel.Create();

            modObject = new GameObject("TactilePaving");
            modObject.AddComponent<TactilePavingRenderer>();
            modObject.AddComponent<ToolButton>().Tool = tool;
        }

        public override void OnLevelUnloading()
        {
            SettingsPanel.DestroyPanel();

            if (modObject != null)
            {
                Object.Destroy(modObject);
                modObject = null;
            }

            if (tool != null)
            {
                ToolController toolController = ToolsModifierControl.toolController;
                if (toolController != null && toolController.CurrentTool == tool)
                {
                    toolController.CurrentTool = ToolsModifierControl.GetTool<DefaultTool>();
                }

                Object.Destroy(tool);
                tool = null;
            }

            TactilePavingData.Clear();
        }
    }
}

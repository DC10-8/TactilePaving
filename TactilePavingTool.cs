using ColossalFramework;
using ColossalFramework.Math;
using UnityEngine;
using static TactilePaving.Localization;

namespace TactilePaving
{
    // 歩道上を2回クリックして、開始地点から終了地点まで点字ブロックを敷設するツール
    public sealed class TactilePavingTool : ToolBase
    {
        private const float MinimumLength = TactileBlockTypes.BlockSize;
        private const float DeleteSearchDistance = 1f;
        private const float NodeRadius = 0.6f;
        private const float NodeVisibleDistance = 400f;

        private static readonly Color ValidColor = new Color(0.2f, 0.9f, 0.3f, 0.8f);
        private static readonly Color InvalidColor = new Color(0.9f, 0.2f, 0.2f, 0.8f);
        private static readonly Color DeleteColor = new Color(1f, 0.3f, 0.2f, 0.7f);
        private static readonly Color NodeColor = new Color(0.3f, 0.7f, 1f, 0.7f);
        private static readonly Color NodeHoverColor = new Color(0.4f, 1f, 1f, 0.95f);

        private bool hasHover;
        private SidewalkPoint hover;
        private Vector3 rawHitPos;
        private bool hasRawHit;
        private bool hoverIsNode;

        private bool hasStart;
        private SidewalkPoint start;
        private Vector3[] previewPath;

        private bool hasDeleteBlock;
        private BlockRef deleteBlock;

        protected override void OnEnable()
        {
            base.OnEnable();
            ResetState();
            SettingsPanel.SetVisible(true);
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            ResetState();
            ShowToolInfo(false, null, Vector3.zero);
            SettingsPanel.SetVisible(false);
        }

        protected override void OnToolUpdate()
        {
            base.OnToolUpdate();

            UpdateHover();
            UpdateHoverNode();

            previewPath = null;
            hasDeleteBlock = false;
            if (hasStart && hasHover)
            {
                previewPath = TactileGeometry.BuildPath(start, hover);
            }
            else if (!hasStart && hasRawHit)
            {
                hasDeleteBlock = TactilePavingData.FindBlock(rawHitPos, DeleteSearchDistance, out deleteBlock);
            }

            UpdateToolInfo();
        }

        protected override void OnToolGUI(Event e)
        {
            base.OnToolGUI(e);

            if (m_toolController.IsInsideUI)
            {
                return;
            }

            if (e.type == EventType.MouseDown)
            {
                if (e.button == 0)
                {
                    OnLeftClick();
                    e.Use();
                }
                else if (e.button == 1)
                {
                    OnRightClick();
                    e.Use();
                }
            }
            else if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Z && e.control)
            {
                TactilePavingData.RemoveLast();
                e.Use();
            }
        }

        public override void RenderOverlay(RenderManager.CameraInfo cameraInfo)
        {
            base.RenderOverlay(cameraInfo);

            OverlayEffect overlay = Singleton<RenderManager>.instance.OverlayEffect;
            ToolManager toolManager = Singleton<ToolManager>.instance;
            float width = ModSettings.Rows * TactileBlockTypes.BlockSize;
            Color typeColor = TactileBlockTypes.BaseColor(ModSettings.BlockType);
            typeColor.a = 0.8f;

            RenderNodes(cameraInfo, overlay, toolManager);

            if (hasDeleteBlock)
            {
                DrawPath(cameraInfo, overlay, toolManager, deleteBlock.CenterLine, TactileBlockTypes.BlockSize, DeleteColor);
            }

            if (previewPath != null)
            {
                Color color = TactileGeometry.PathLength(previewPath) >= MinimumLength ? typeColor : InvalidColor;
                DrawPath(cameraInfo, overlay, toolManager, previewPath, width, color);
            }

            if (hasStart)
            {
                overlay.DrawCircle(cameraInfo, ValidColor, start.Position, width + 0.4f, -1f, 1280f, false, true);
                toolManager.m_drawCallData.m_overlayCalls++;
            }

            if (hasHover && hoverIsNode)
            {
                overlay.DrawCircle(cameraInfo, NodeHoverColor, hover.Position, NodeRadius * 2.6f, -1f, 1280f, false, true);
                toolManager.m_drawCallData.m_overlayCalls++;
            }
            else if (hasHover)
            {
                overlay.DrawCircle(cameraInfo, hasStart ? typeColor : ValidColor, hover.Position, width + 0.4f, -1f, 1280f, false, true);
                toolManager.m_drawCallData.m_overlayCalls++;
            }
            else if (hasRawHit && !hasDeleteBlock)
            {
                overlay.DrawCircle(cameraInfo, InvalidColor, rawHitPos, 1f, -1f, 1280f, false, true);
                toolManager.m_drawCallData.m_overlayCalls++;
            }
        }

        private void OnLeftClick()
        {
            if (!hasHover)
            {
                return;
            }

            if (!hasStart)
            {
                start = hover;
                hasStart = true;
                return;
            }

            Vector3[] path = TactileGeometry.BuildPath(start, hover);
            if (TactileGeometry.PathLength(path) < MinimumLength)
            {
                return;
            }

            SidewalkPoint end = hover;
            end.Position = path[path.Length - 1];
            TactilePavingData.Add(new TactileStrip
            {
                Type = ModSettings.BlockType,
                Rows = ModSettings.Rows,
                Points = path,
                Start = start,
                End = end
            });

            hasStart = false;
            previewPath = null;
        }

        private void OnRightClick()
        {
            if (hasStart)
            {
                hasStart = false;
                previewPath = null;
                return;
            }

            if (hasDeleteBlock)
            {
                TactilePavingData.RemoveBlock(deleteBlock);
                hasDeleteBlock = false;
            }
        }

        private void UpdateHover()
        {
            hasHover = false;
            hasRawHit = false;

            if (m_toolController.IsInsideUI || !Cursor.visible)
            {
                return;
            }

            Camera camera = Camera.main;
            if (camera == null)
            {
                return;
            }

            RaycastInput input = new RaycastInput(camera.ScreenPointToRay(Input.mousePosition), camera.farClipPlane);
            input.m_rayRight = camera.transform.TransformDirection(Vector3.right);
            input.m_netService = new RaycastService(ItemClass.Service.Road, ItemClass.SubService.None, ItemClass.Layer.Default);
            input.m_netService2 = new RaycastService(ItemClass.Service.Beautification, ItemClass.SubService.None, ItemClass.Layer.Default);
            input.m_ignoreTerrain = true;
            input.m_ignoreSegmentFlags = NetSegment.Flags.None;

            RaycastOutput output;
            if (!RayCast(input, out output) || output.m_netSegment == 0)
            {
                return;
            }

            rawHitPos = output.m_hitPos;
            hasRawHit = true;

            float width = ModSettings.Rows * TactileBlockTypes.BlockSize;
            hasHover = TactileGeometry.TryGetSidewalkPoint(output.m_netSegment, output.m_hitPos, width, out hover);
        }

        // 敷設済みブロックの端（ノード）にカーソルが乗っていれば、そこへ吸着させる
        private void UpdateHoverNode()
        {
            hoverIsNode = false;
            if (m_toolController.IsInsideUI || !Cursor.visible || Camera.main == null)
            {
                return;
            }

            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            float bestDistance = NodeRadius;
            foreach (TactileStrip strip in TactilePavingData.Strips)
            {
                CheckNode(ray, strip.Start, ref bestDistance);
                CheckNode(ray, strip.End, ref bestDistance);
            }
        }

        private void CheckNode(Ray ray, SidewalkPoint node, ref float bestDistance)
        {
            Vector3 toNode = node.Position - ray.origin;
            float along = Vector3.Dot(toNode, ray.direction);
            if (along <= 0f)
            {
                return;
            }

            float distance = (toNode - ray.direction * along).magnitude;
            if (distance >= bestDistance)
            {
                return;
            }

            bestDistance = distance;
            hover = TactileGeometry.ResolveNode(node);
            hasHover = true;
            hoverIsNode = true;
        }

        private static void RenderNodes(RenderManager.CameraInfo cameraInfo, OverlayEffect overlay, ToolManager toolManager)
        {
            float maxDistanceSqr = NodeVisibleDistance * NodeVisibleDistance;
            foreach (TactileStrip strip in TactilePavingData.Strips)
            {
                RenderNode(cameraInfo, overlay, toolManager, strip.Start.Position, maxDistanceSqr);
                RenderNode(cameraInfo, overlay, toolManager, strip.End.Position, maxDistanceSqr);
            }
        }

        private static void RenderNode(RenderManager.CameraInfo cameraInfo, OverlayEffect overlay, ToolManager toolManager, Vector3 position, float maxDistanceSqr)
        {
            if ((position - cameraInfo.m_position).sqrMagnitude > maxDistanceSqr)
            {
                return;
            }

            overlay.DrawCircle(cameraInfo, NodeColor, position, NodeRadius * 2f, -1f, 1280f, false, true);
            toolManager.m_drawCallData.m_overlayCalls++;
        }

        private void UpdateToolInfo()
        {
            string typeName = TactileBlockTypes.DisplayName(ModSettings.BlockType);
            if (hasStart && hoverIsNode)
            {
                ShowToolInfo(true, typeName + T("\nクリックでこのノードまで敷設 / 右クリックでキャンセル", "\nClick to lay up to this node / Right-click to cancel"), hover.Position);
            }
            else if (hoverIsNode)
            {
                string deleteHint = hasDeleteBlock ? T("\n右クリックでこのブロックを1枚削除", "\nRight-click to remove this block") : string.Empty;
                ShowToolInfo(true, typeName + T("\nクリックでこのノードから続きを敷設", "\nClick to continue laying from this node") + deleteHint, hover.Position);
            }
            else if (hasStart)
            {
                string length = previewPath != null
                    ? string.Format("{0:0.0}m", TactileGeometry.PathLength(previewPath))
                    : "-";
                ShowToolInfo(true, typeName + T("\n長さ: ", "\nLength: ") + length + T("\nクリックで終了地点を決定 / 右クリックでキャンセル", "\nClick to set the end point / Right-click to cancel"), hasHover ? hover.Position : start.Position);
            }
            else if (hasHover)
            {
                string deleteHint = hasDeleteBlock ? T("\n右クリックでこのブロックを1枚削除", "\nRight-click to remove this block") : string.Empty;
                ShowToolInfo(true, typeName + T("\nクリックで開始地点を決定", "\nClick to set the start point") + deleteHint, hover.Position);
            }
            else if (hasDeleteBlock)
            {
                ShowToolInfo(true, T("右クリックでこのブロックを1枚削除", "Right-click to remove this block"), rawHitPos);
            }
            else if (hasRawHit)
            {
                ShowToolInfo(true, T("歩道の上をクリックしてください", "Click on a sidewalk"), rawHitPos);
            }
            else
            {
                ShowToolInfo(false, null, Vector3.zero);
            }
        }

        private static void DrawPath(RenderManager.CameraInfo cameraInfo, OverlayEffect overlay, ToolManager toolManager, Vector3[] points, float width, Color color)
        {
            for (int i = 1; i < points.Length; i++)
            {
                overlay.DrawSegment(cameraInfo, color, new Segment3(points[i - 1], points[i]), width, 0f, -1f, 1280f, false, true);
                toolManager.m_drawCallData.m_overlayCalls++;
            }
        }

        private void ResetState()
        {
            hasHover = false;
            hasRawHit = false;
            hoverIsNode = false;
            hasStart = false;
            previewPath = null;
            hasDeleteBlock = false;
        }
    }
}

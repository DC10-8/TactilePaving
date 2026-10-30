using System.Collections.Generic;
using ColossalFramework;
using ColossalFramework.Math;
using UnityEngine;

namespace TactilePaving
{
    // 歩道上の1点。どのセグメントのどの歩行者レーン上かを保持する
    internal struct SidewalkPoint
    {
        public ushort Segment;
        public uint Lane;
        public float LaneOffset;
        public float Lateral;
        public float MaxLateral;
        public Vector3 Position;
    }

    internal static class TactileGeometry
    {
        private const float SampleInterval = 0.5f;
        private const float StraightSampleInterval = 1f;
        private const float SidewalkTolerance = 1f;
        private const float MinimumSidewalkHalfWidth = 0.75f;
        private const float NodeLaneTolerance = 0.3f;

        // レイキャストで当たった道路セグメントから、最も近い歩道（歩行者レーン）上の位置を求める
        public static bool TryGetSidewalkPoint(ushort segmentId, Vector3 hitPos, float stripWidth, out SidewalkPoint point)
        {
            point = default(SidewalkPoint);

            NetManager netManager = Singleton<NetManager>.instance;
            NetSegment segment = netManager.m_segments.m_buffer[segmentId];
            if ((segment.m_flags & NetSegment.Flags.Created) == 0)
            {
                return false;
            }

            NetInfo info = segment.Info;
            if (info == null || info.m_lanes == null)
            {
                return false;
            }

            bool found = false;
            float bestDistance = float.MaxValue;
            uint laneId = segment.m_lanes;
            for (int i = 0; i < info.m_lanes.Length && laneId != 0; i++)
            {
                NetInfo.Lane laneInfo = info.m_lanes[i];
                NetLane lane = netManager.m_lanes.m_buffer[laneId];
                if ((laneInfo.m_laneType & NetInfo.LaneType.Pedestrian) != 0)
                {
                    Vector3 position;
                    float offset;
                    lane.GetClosestPosition(hitPos, out position, out offset);

                    Vector3 side = SideOf(lane.m_bezier.Tangent(offset));
                    Vector3 delta = hitPos - position;
                    delta.y = 0f;
                    float distance = delta.magnitude;
                    float halfWidth = Mathf.Max(laneInfo.m_width * 0.5f, MinimumSidewalkHalfWidth);

                    if (distance <= halfWidth + SidewalkTolerance && distance < bestDistance)
                    {
                        float maxLateral = Mathf.Max(0f, halfWidth - stripWidth * 0.5f);
                        float lateral = Mathf.Clamp(Vector3.Dot(delta, side), -maxLateral, maxLateral);

                        bestDistance = distance;
                        found = true;
                        point.Segment = segmentId;
                        point.Lane = laneId;
                        point.LaneOffset = offset;
                        point.Lateral = lateral;
                        point.MaxLateral = maxLateral;
                        point.Position = position + side * lateral;
                    }
                }

                laneId = lane.m_nextLane;
            }

            return found;
        }

        // 保存されているノードのレーン情報が今も有効か確かめる。道路が作り直されていればレーン情報を捨てる
        public static SidewalkPoint ResolveNode(SidewalkPoint node)
        {
            if (node.Lane != 0)
            {
                NetLane lane = Singleton<NetManager>.instance.m_lanes.m_buffer[node.Lane];
                bool valid = ((NetLane.Flags)lane.m_flags & NetLane.Flags.Created) != 0 && lane.m_segment == node.Segment;
                if (valid)
                {
                    Vector3 side = SideOf(lane.m_bezier.Tangent(node.LaneOffset));
                    Vector3 expected = lane.m_bezier.Position(node.LaneOffset) + side * node.Lateral;
                    valid = VectorUtils.LengthXZ(expected - node.Position) < NodeLaneTolerance;
                }

                if (!valid)
                {
                    node.Segment = 0;
                    node.Lane = 0;
                    node.LaneOffset = 0f;
                    node.Lateral = 0f;
                }
            }

            return node;
        }

        // 開始地点から終了地点までの敷設経路を作る
        public static Vector3[] BuildPath(SidewalkPoint start, SidewalkPoint end)
        {
            List<Vector3> points = new List<Vector3>();
            if (start.Lane != 0 && start.Lane == end.Lane)
            {
                // 同じ歩道上ならレーンの曲線に沿って敷設する
                Bezier3 bezier = Singleton<NetManager>.instance.m_lanes.m_buffer[start.Lane].m_bezier;
                float length = EstimateLength(bezier, start.LaneOffset, end.LaneOffset);
                int steps = Mathf.Max(1, Mathf.CeilToInt(length / SampleInterval));
                for (int i = 0; i <= steps; i++)
                {
                    float f = (float)i / steps;
                    float t = Mathf.Lerp(start.LaneOffset, end.LaneOffset, f);
                    float lateral = Mathf.Lerp(start.Lateral, end.Lateral, f);
                    Vector3 side = SideOf(bezier.Tangent(t));
                    points.Add(bezier.Position(t) + side * lateral);
                }
            }
            else
            {
                // 別の歩道どうしは直線でつなぐ
                float length = VectorUtils.LengthXZ(end.Position - start.Position);
                int steps = Mathf.Max(1, Mathf.CeilToInt(length / StraightSampleInterval));
                for (int i = 0; i <= steps; i++)
                {
                    points.Add(Vector3.Lerp(start.Position, end.Position, (float)i / steps));
                }
            }

            return points.ToArray();
        }

        public static float PathLength(Vector3[] points)
        {
            float length = 0f;
            for (int i = 1; i < points.Length; i++)
            {
                length += VectorUtils.LengthXZ(points[i] - points[i - 1]);
            }

            return length;
        }

        // 経路の始点から測った距離 from〜to の部分だけを切り出す
        public static Vector3[] SubPath(Vector3[] points, float from, float to)
        {
            List<Vector3> result = new List<Vector3>();
            float travelled = 0f;
            for (int i = 1; i < points.Length; i++)
            {
                float length = VectorUtils.LengthXZ(points[i] - points[i - 1]);
                float segmentStart = travelled;
                float segmentEnd = travelled + length;
                travelled = segmentEnd;
                if (segmentEnd < from || segmentStart > to || length <= 0f)
                {
                    continue;
                }

                if (result.Count == 0)
                {
                    result.Add(Vector3.Lerp(points[i - 1], points[i], Mathf.Clamp01((from - segmentStart) / length)));
                }

                result.Add(Vector3.Lerp(points[i - 1], points[i], Mathf.Clamp01((to - segmentStart) / length)));
                if (segmentEnd >= to)
                {
                    break;
                }
            }

            return result.ToArray();
        }

        // 経路を幅方向に offset だけずらす
        public static Vector3[] OffsetPath(Vector3[] points, float offset)
        {
            Vector3[] result = new Vector3[points.Length];
            for (int i = 0; i < points.Length; i++)
            {
                result[i] = points[i] + SideAt(points, i) * offset;
            }

            return result;
        }

        // 点を経路に投影し、始点からの距離と幅方向のずれを求める。戻り値は水平距離
        public static float Project(Vector3[] points, Vector3 position, out float along, out float lateral)
        {
            along = 0f;
            lateral = 0f;
            float best = float.MaxValue;
            float travelled = 0f;
            for (int i = 1; i < points.Length; i++)
            {
                Vector3 a = points[i - 1];
                Vector3 direction = points[i] - a;
                direction.y = 0f;
                float length = direction.magnitude;
                Vector3 delta = position - a;
                delta.y = 0f;
                float t = length > 0f ? Mathf.Clamp(Vector3.Dot(delta, direction) / length, 0f, length) : 0f;
                Vector3 closest = a + (length > 0f ? direction / length * t : Vector3.zero);
                Vector3 offset = position - closest;
                offset.y = 0f;
                float distance = offset.magnitude;
                if (distance < best)
                {
                    best = distance;
                    along = travelled + t;
                    lateral = Vector3.Dot(offset, SideOf(direction));
                }

                travelled += length;
            }

            return best;
        }

        // 各点での幅方向（水平・単位ベクトル）
        public static Vector3 SideAt(Vector3[] points, int index)
        {
            Vector3 previous = points[Mathf.Max(0, index - 1)];
            Vector3 next = points[Mathf.Min(points.Length - 1, index + 1)];
            return SideOf(next - previous);
        }

        public static Vector3 SideOf(Vector3 direction)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f)
            {
                return Vector3.right;
            }

            direction.Normalize();
            return new Vector3(direction.z, 0f, -direction.x);
        }

        private static float EstimateLength(Bezier3 bezier, float from, float to)
        {
            const int samples = 16;
            float length = 0f;
            Vector3 previous = bezier.Position(from);
            for (int i = 1; i <= samples; i++)
            {
                Vector3 current = bezier.Position(Mathf.Lerp(from, to, (float)i / samples));
                length += VectorUtils.LengthXZ(current - previous);
                previous = current;
            }

            return length;
        }
    }
}

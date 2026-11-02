using System.Collections.Generic;
using ColossalFramework;
using ColossalFramework.Math;
using UnityEngine;

namespace TactilePaving
{
    // 歩道上の1点。セグメントの歩行者レーン上か、交差点の角の歩道上かを保持する
    internal struct SidewalkPoint
    {
        public ushort Segment;
        public uint Lane;

        // 交差点の角の歩道（Lane が 0 で Node が 0 以外のとき）。角は両側のレーンで特定する
        public ushort Node;
        public uint CornerLaneA;
        public uint CornerLaneB;

        public float LaneOffset;
        public float Lateral;
        public float MaxLateral;
        public Vector3 Position;

        public bool IsCorner
        {
            get { return Lane == 0 && Node != 0; }
        }
    }

    // 交差点の角の歩道。レーンAの交差点側の端から、隣の道路のレーンBの端までをつなぐ曲線
    internal struct SidewalkCorner
    {
        public ushort Node;
        public uint LaneA;
        public float EndA;
        public uint LaneB;
        public float EndB;
        public Bezier3 Curve;
        public float HalfWidth;
    }

    internal static class TactileGeometry
    {
        private const float SampleInterval = 0.5f;
        private const float StraightSampleInterval = 1f;
        private const float SidewalkTolerance = 1f;
        private const float MinimumSidewalkHalfWidth = 0.75f;
        private const float NodeLaneTolerance = 0.3f;

        // 円弧を3次ベジェで近似するときの制御点の長さ（弦の長さに対する比）
        private const float CornerHandleRatio = 0.39f;

        private struct LaneEnd
        {
            public uint Lane;
            public float EndT;
            public Vector3 Position;
            public Vector3 Outward;
            public float Side;
            public float HalfWidth;
        }

        private struct SegmentEnd
        {
            public float Angle;
            public List<LaneEnd> Lanes;
        }

        // レイキャストで当たった道路セグメントか交差点から、最も近い歩道上の位置を求める。
        // セグメントの歩行者レーンに加え、その両端の交差点の角の歩道も候補にする
        public static bool TryGetSidewalkPoint(ushort segmentId, ushort nodeId, Vector3 hitPos, float stripWidth, out SidewalkPoint point)
        {
            point = default(SidewalkPoint);
            NetManager netManager = Singleton<NetManager>.instance;
            float bestDistance = float.MaxValue;
            bool found = false;

            List<ushort> segments = new List<ushort>();
            List<ushort> nodes = new List<ushort>();
            if (segmentId != 0)
            {
                NetSegment segment = netManager.m_segments.m_buffer[segmentId];
                segments.Add(segmentId);
                nodes.Add(segment.m_startNode);
                nodes.Add(segment.m_endNode);
            }

            if (nodeId != 0 && !nodes.Contains(nodeId))
            {
                nodes.Add(nodeId);
                NetNode node = netManager.m_nodes.m_buffer[nodeId];
                for (int i = 0; i < 8; i++)
                {
                    ushort nodeSegment = node.GetSegment(i);
                    if (nodeSegment != 0 && !segments.Contains(nodeSegment))
                    {
                        segments.Add(nodeSegment);
                    }
                }
            }

            foreach (ushort candidate in segments)
            {
                found |= TryLanePoints(candidate, hitPos, stripWidth, ref bestDistance, ref point);
            }

            List<SidewalkCorner> corners = new List<SidewalkCorner>();
            foreach (ushort candidate in nodes)
            {
                corners.Clear();
                GetCorners(candidate, corners);
                foreach (SidewalkCorner corner in corners)
                {
                    float t = ClosestT(corner.Curve, hitPos);
                    SidewalkPoint cornerPoint;
                    float distance;
                    if (MakePoint(corner.Curve, t, corner.HalfWidth, hitPos, stripWidth, out cornerPoint, out distance) &&
                        distance < bestDistance)
                    {
                        bestDistance = distance;
                        found = true;
                        cornerPoint.Node = corner.Node;
                        cornerPoint.CornerLaneA = corner.LaneA;
                        cornerPoint.CornerLaneB = corner.LaneB;
                        point = cornerPoint;
                    }
                }
            }

            return found;
        }

        private static bool TryLanePoints(ushort segmentId, Vector3 hitPos, float stripWidth, ref float bestDistance, ref SidewalkPoint point)
        {
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

                    SidewalkPoint lanePoint;
                    float distance;
                    if (MakePoint(lane.m_bezier, offset, LaneHalfWidth(laneInfo), hitPos, stripWidth, out lanePoint, out distance) &&
                        distance < bestDistance)
                    {
                        bestDistance = distance;
                        found = true;
                        lanePoint.Segment = segmentId;
                        lanePoint.Lane = laneId;
                        point = lanePoint;
                    }
                }

                laneId = lane.m_nextLane;
            }

            return found;
        }

        // 曲線上の t の位置を中心に、カーソル位置を歩道の幅の中に収めた点を作る
        private static bool MakePoint(Bezier3 curve, float t, float halfWidth, Vector3 hitPos, float stripWidth, out SidewalkPoint point, out float distance)
        {
            point = default(SidewalkPoint);
            Vector3 position = curve.Position(t);
            Vector3 side = SideOf(curve.Tangent(t));
            Vector3 delta = hitPos - position;
            delta.y = 0f;
            distance = delta.magnitude;
            if (distance > halfWidth + SidewalkTolerance)
            {
                return false;
            }

            float maxLateral = Mathf.Max(0f, halfWidth - stripWidth * 0.5f);
            float lateral = Mathf.Clamp(Vector3.Dot(delta, side), -maxLateral, maxLateral);
            point.LaneOffset = t;
            point.Lateral = lateral;
            point.MaxLateral = maxLateral;
            point.Position = position + side * lateral;
            return true;
        }

        // 交差点の角の歩道をすべて求める。道路を角度順に並べ、隣り合う道路の向かい合う側の
        // いちばん外側の歩行者レーンどうしを曲線でつなぐ（道路が2本なら両側の2つの角になる）
        public static void GetCorners(ushort nodeId, List<SidewalkCorner> corners)
        {
            NetManager netManager = Singleton<NetManager>.instance;
            NetNode node = netManager.m_nodes.m_buffer[nodeId];
            if (nodeId == 0 || (node.m_flags & NetNode.Flags.Created) == 0)
            {
                return;
            }

            List<SegmentEnd> ends = new List<SegmentEnd>();
            for (int i = 0; i < 8; i++)
            {
                ushort segmentId = node.GetSegment(i);
                if (segmentId != 0)
                {
                    SegmentEnd end;
                    if (TryGetSegmentEnd(nodeId, segmentId, out end))
                    {
                        ends.Add(end);
                    }
                }
            }

            if (ends.Count < 2)
            {
                return;
            }

            ends.Sort((x, y) => x.Angle.CompareTo(y.Angle));
            for (int k = 0; k < ends.Count; k++)
            {
                SegmentEnd from = ends[k];
                SegmentEnd to = ends[(k + 1) % ends.Count];

                // 角度の大きい（反時計回りの）隣の道路の側 = 道路の左側
                LaneEnd laneA;
                LaneEnd laneB;
                if (!TryOutermost(from.Lanes, true, out laneA) || !TryOutermost(to.Lanes, false, out laneB))
                {
                    continue;
                }

                Vector3 a = laneA.Position;
                Vector3 d = laneB.Position;
                float handle = VectorUtils.LengthXZ(d - a) * CornerHandleRatio;
                Bezier3 curve = new Bezier3(a, a - laneA.Outward * handle, d - laneB.Outward * handle, d);

                corners.Add(new SidewalkCorner
                {
                    Node = nodeId,
                    LaneA = laneA.Lane,
                    EndA = laneA.EndT,
                    LaneB = laneB.Lane,
                    EndB = laneB.EndT,
                    Curve = curve,
                    HalfWidth = (laneA.HalfWidth + laneB.HalfWidth) * 0.5f
                });
            }
        }

        private static bool TryGetSegmentEnd(ushort nodeId, ushort segmentId, out SegmentEnd end)
        {
            end = new SegmentEnd { Lanes = new List<LaneEnd>() };
            NetManager netManager = Singleton<NetManager>.instance;
            NetSegment segment = netManager.m_segments.m_buffer[segmentId];
            NetInfo info = segment.Info;
            if ((segment.m_flags & NetSegment.Flags.Created) == 0 || info == null || info.m_lanes == null)
            {
                return false;
            }

            // 交差点から道路の中へ向かう向き
            Vector3 direction = segment.m_startNode == nodeId ? segment.m_startDirection : segment.m_endDirection;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f)
            {
                return false;
            }

            direction.Normalize();
            end.Angle = Mathf.Atan2(direction.z, direction.x);
            Vector3 nodePosition = netManager.m_nodes.m_buffer[nodeId].m_position;

            uint laneId = segment.m_lanes;
            for (int i = 0; i < info.m_lanes.Length && laneId != 0; i++)
            {
                NetInfo.Lane laneInfo = info.m_lanes[i];
                NetLane lane = netManager.m_lanes.m_buffer[laneId];
                if ((laneInfo.m_laneType & NetInfo.LaneType.Pedestrian) != 0)
                {
                    Bezier3 bezier = lane.m_bezier;
                    bool atStart = VectorUtils.LengthSqrXZ(bezier.a - nodePosition) <= VectorUtils.LengthSqrXZ(bezier.d - nodePosition);
                    Vector3 position = atStart ? bezier.a : bezier.d;
                    Vector3 outward = atStart ? bezier.Tangent(0f) : -bezier.Tangent(1f);
                    outward.y = 0f;
                    outward = outward.sqrMagnitude > 0.0001f ? outward.normalized : direction;
                    Vector3 offset = position - nodePosition;

                    end.Lanes.Add(new LaneEnd
                    {
                        Lane = laneId,
                        EndT = atStart ? 0f : 1f,
                        Position = position,
                        Outward = outward,
                        Side = direction.x * offset.z - direction.z * offset.x,
                        HalfWidth = LaneHalfWidth(laneInfo)
                    });
                }

                laneId = lane.m_nextLane;
            }

            return end.Lanes.Count > 0;
        }

        // left: 道路の左側（交差点から見て）でいちばん外側のレーン、false なら右側
        private static bool TryOutermost(List<LaneEnd> lanes, bool left, out LaneEnd result)
        {
            result = default(LaneEnd);
            bool found = false;
            foreach (LaneEnd lane in lanes)
            {
                float side = left ? lane.Side : -lane.Side;
                if (side > 0f && (!found || side > (left ? result.Side : -result.Side)))
                {
                    result = lane;
                    found = true;
                }
            }

            return found;
        }

        private static bool TryGetCorner(ushort nodeId, uint laneA, uint laneB, out SidewalkCorner corner)
        {
            List<SidewalkCorner> corners = new List<SidewalkCorner>();
            GetCorners(nodeId, corners);
            foreach (SidewalkCorner candidate in corners)
            {
                if (candidate.LaneA == laneA && candidate.LaneB == laneB)
                {
                    corner = candidate;
                    return true;
                }
            }

            corner = default(SidewalkCorner);
            return false;
        }

        // 保存されているノードの歩道情報が今も有効か確かめる。道路が作り直されていれば歩道情報を捨てる
        public static SidewalkPoint ResolveNode(SidewalkPoint node)
        {
            Bezier3 curve;
            bool valid = false;
            if (TryGetCurve(node, out curve))
            {
                Vector3 expected = curve.Position(node.LaneOffset) + SideOf(curve.Tangent(node.LaneOffset)) * node.Lateral;
                valid = VectorUtils.LengthXZ(expected - node.Position) < NodeLaneTolerance;
            }

            if (!valid)
            {
                Vector3 position = node.Position;
                node = new SidewalkPoint { Position = position };
            }

            return node;
        }

        private static bool TryGetCurve(SidewalkPoint point, out Bezier3 curve)
        {
            curve = default(Bezier3);
            if (point.Lane != 0)
            {
                NetLane lane = Singleton<NetManager>.instance.m_lanes.m_buffer[point.Lane];
                if (((NetLane.Flags)lane.m_flags & NetLane.Flags.Created) == 0 || lane.m_segment != point.Segment)
                {
                    return false;
                }

                curve = lane.m_bezier;
                return true;
            }

            SidewalkCorner corner;
            if (point.IsCorner && TryGetCorner(point.Node, point.CornerLaneA, point.CornerLaneB, out corner))
            {
                curve = corner.Curve;
                return true;
            }

            return false;
        }

        // 開始地点から終了地点までの敷設経路を作る。同じ歩道ならその曲線に沿い、
        // 隣り合う歩道（歩道 → 交差点の角 → 歩道）ならそれらをつないで沿わせる。それ以外は直線
        public static Vector3[] BuildPath(SidewalkPoint start, SidewalkPoint end)
        {
            List<Vector3> center = new List<Vector3>();
            if (!TryBuildCenterLine(start, end, center))
            {
                float length = VectorUtils.LengthXZ(end.Position - start.Position);
                int steps = Mathf.Max(1, Mathf.CeilToInt(length / StraightSampleInterval));
                Vector3[] straight = new Vector3[steps + 1];
                for (int i = 0; i <= steps; i++)
                {
                    straight[i] = Vector3.Lerp(start.Position, end.Position, (float)i / steps);
                }

                return straight;
            }

            // 中心線に沿って、開始地点と終了地点の幅方向のずれを少しずつ移していく
            Vector3[] points = center.ToArray();
            float startLateral = Vector3.Dot(start.Position - points[0], SideAt(points, 0));
            float endLateral = Vector3.Dot(end.Position - points[points.Length - 1], SideAt(points, points.Length - 1));
            float total = PathLength(points);
            Vector3[] result = new Vector3[points.Length];
            float travelled = 0f;
            for (int i = 0; i < points.Length; i++)
            {
                if (i > 0)
                {
                    travelled += VectorUtils.LengthXZ(points[i] - points[i - 1]);
                }

                float f = total > 0f ? travelled / total : 0f;
                result[i] = points[i] + SideAt(points, i) * Mathf.Lerp(startLateral, endLateral, f);
            }

            result[0] = start.Position;
            result[result.Length - 1] = end.Position;
            return result;
        }

        private static bool TryBuildCenterLine(SidewalkPoint start, SidewalkPoint end, List<Vector3> center)
        {
            Bezier3 startCurve;
            Bezier3 endCurve;
            if (!TryGetCurve(start, out startCurve) || !TryGetCurve(end, out endCurve))
            {
                return false;
            }

            if (SameCurve(start, end))
            {
                AppendCurve(center, startCurve, start.LaneOffset, end.LaneOffset);
                return true;
            }

            if (!start.IsCorner && !end.IsCorner)
            {
                // 歩道 → 角 → 歩道
                SidewalkCorner corner;
                bool forward;
                if (!TryFindCornerBetween(start.Lane, end.Lane, out corner, out forward))
                {
                    return false;
                }

                AppendCurve(center, startCurve, start.LaneOffset, forward ? corner.EndA : corner.EndB);
                AppendCurve(center, corner.Curve, forward ? 0f : 1f, forward ? 1f : 0f);
                AppendCurve(center, endCurve, forward ? corner.EndB : corner.EndA, end.LaneOffset);
                return true;
            }

            if (start.IsCorner && !end.IsCorner)
            {
                // 角 → 歩道
                SidewalkCorner corner;
                if (!TryGetCorner(start.Node, start.CornerLaneA, start.CornerLaneB, out corner) ||
                    (end.Lane != corner.LaneA && end.Lane != corner.LaneB))
                {
                    return false;
                }

                bool towardB = end.Lane == corner.LaneB;
                AppendCurve(center, corner.Curve, start.LaneOffset, towardB ? 1f : 0f);
                AppendCurve(center, endCurve, towardB ? corner.EndB : corner.EndA, end.LaneOffset);
                return true;
            }

            if (!start.IsCorner && end.IsCorner)
            {
                // 歩道 → 角（角 → 歩道を逆向きにたどる）
                if (!TryBuildCenterLine(end, start, center))
                {
                    return false;
                }

                center.Reverse();
                return true;
            }

            // 角 → 歩道 → 角（短い道路の両端の角どうし）
            SidewalkCorner first;
            SidewalkCorner last;
            if (!TryGetCorner(start.Node, start.CornerLaneA, start.CornerLaneB, out first) ||
                !TryGetCorner(end.Node, end.CornerLaneA, end.CornerLaneB, out last))
            {
                return false;
            }

            uint shared = SharedLane(first, last);
            if (shared == 0)
            {
                return false;
            }

            NetLane sharedLane = Singleton<NetManager>.instance.m_lanes.m_buffer[shared];
            bool firstToB = first.LaneB == shared;
            bool lastFromA = last.LaneA == shared;
            AppendCurve(center, first.Curve, start.LaneOffset, firstToB ? 1f : 0f);
            AppendCurve(center, sharedLane.m_bezier, firstToB ? first.EndB : first.EndA, lastFromA ? last.EndA : last.EndB);
            AppendCurve(center, last.Curve, lastFromA ? 0f : 1f, end.LaneOffset);
            return true;
        }

        private static bool SameCurve(SidewalkPoint a, SidewalkPoint b)
        {
            if (a.Lane != 0)
            {
                return a.Lane == b.Lane;
            }

            return a.IsCorner && b.IsCorner && a.Node == b.Node &&
                a.CornerLaneA == b.CornerLaneA && a.CornerLaneB == b.CornerLaneB;
        }

        // 2本の歩行者レーンをつなぐ角を、どちらかのレーンの両端の交差点から探す。
        // forward: 角の曲線が laneFrom 側 (A) から laneTo 側 (B) へ向いているか
        private static bool TryFindCornerBetween(uint laneFrom, uint laneTo, out SidewalkCorner corner, out bool forward)
        {
            NetManager netManager = Singleton<NetManager>.instance;
            NetSegment segment = netManager.m_segments.m_buffer[netManager.m_lanes.m_buffer[laneFrom].m_segment];
            List<SidewalkCorner> corners = new List<SidewalkCorner>();
            GetCorners(segment.m_startNode, corners);
            GetCorners(segment.m_endNode, corners);
            foreach (SidewalkCorner candidate in corners)
            {
                if (candidate.LaneA == laneFrom && candidate.LaneB == laneTo)
                {
                    corner = candidate;
                    forward = true;
                    return true;
                }

                if (candidate.LaneA == laneTo && candidate.LaneB == laneFrom)
                {
                    corner = candidate;
                    forward = false;
                    return true;
                }
            }

            corner = default(SidewalkCorner);
            forward = true;
            return false;
        }

        private static uint SharedLane(SidewalkCorner first, SidewalkCorner last)
        {
            if (first.LaneA == last.LaneA || first.LaneA == last.LaneB)
            {
                return first.LaneA;
            }

            if (first.LaneB == last.LaneA || first.LaneB == last.LaneB)
            {
                return first.LaneB;
            }

            return 0;
        }

        // 曲線の from〜to を一定間隔で点にして追加する（つなぎ目の重複点は追加しない）
        private static void AppendCurve(List<Vector3> points, Bezier3 curve, float from, float to)
        {
            float length = EstimateLength(curve, from, to);
            int steps = Mathf.Max(1, Mathf.CeilToInt(length / SampleInterval));
            for (int i = 0; i <= steps; i++)
            {
                Vector3 position = curve.Position(Mathf.Lerp(from, to, (float)i / steps));
                if (points.Count > 0 && VectorUtils.LengthSqrXZ(position - points[points.Count - 1]) < 0.0001f)
                {
                    continue;
                }

                points.Add(position);
            }
        }

        // 水平面上で、曲線のうち点にいちばん近い位置の t
        private static float ClosestT(Bezier3 curve, Vector3 point)
        {
            const int samples = 32;
            float bestT = 0f;
            float best = float.MaxValue;
            for (int i = 0; i <= samples; i++)
            {
                float t = (float)i / samples;
                float distance = VectorUtils.LengthSqrXZ(curve.Position(t) - point);
                if (distance < best)
                {
                    best = distance;
                    bestT = t;
                }
            }

            // 近くを細かく探し直す
            float step = 1f / samples;
            for (int iteration = 0; iteration < 8; iteration++)
            {
                step *= 0.5f;
                foreach (float t in new[] { bestT - step, bestT + step })
                {
                    float clamped = Mathf.Clamp01(t);
                    float distance = VectorUtils.LengthSqrXZ(curve.Position(clamped) - point);
                    if (distance < best)
                    {
                        best = distance;
                        bestT = clamped;
                    }
                }
            }

            return bestT;
        }

        private static float LaneHalfWidth(NetInfo.Lane laneInfo)
        {
            return Mathf.Max(laneInfo.m_width * 0.5f, MinimumSidewalkHalfWidth);
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

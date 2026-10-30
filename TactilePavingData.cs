using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace TactilePaving
{
    internal sealed class TactileStrip
    {
        public TactileBlockType Type;
        public int Rows;
        public Vector3[] Points;

        // 両端のノード。レーン情報が残っていれば、続きを敷設するときに歩道のカーブに沿わせられる
        public SidewalkPoint Start;
        public SidewalkPoint End;

        public float Width
        {
            get { return Rows * TactileBlockTypes.BlockSize; }
        }
    }

    // 1枚の点字ブロック（ブロック列の何列目・先頭から何枚目か）
    internal struct BlockRef
    {
        public TactileStrip Strip;
        public int Row;
        public int Index;

        // そのブロックの中心線（描画の強調表示用）
        public Vector3[] CenterLine;
    }

    // 敷設済みの点字ブロック一覧。セーブデータへの保存もここで行う
    internal static class TactilePavingData
    {
        private const int FormatVersion = 2;

        private static readonly List<TactileStrip> StripList = new List<TactileStrip>();

        public static event Action Changed;

        public static IList<TactileStrip> Strips
        {
            get { return StripList; }
        }

        public static void Add(TactileStrip strip)
        {
            StripList.Add(strip);
            RaiseChanged();
        }

        public static void Remove(TactileStrip strip)
        {
            if (StripList.Remove(strip))
            {
                RaiseChanged();
            }
        }

        // 指定位置にある点字ブロックを1枚だけ探す
        public static bool FindBlock(Vector3 position, float maxDistance, out BlockRef block)
        {
            block = default(BlockRef);
            TactileStrip strip = FindNearest(position, maxDistance);
            if (strip == null)
            {
                return false;
            }

            float along;
            float lateral;
            TactileGeometry.Project(strip.Points, position, out along, out lateral);

            float size = TactileBlockTypes.BlockSize;
            float length = TactileGeometry.PathLength(strip.Points);
            block.Strip = strip;
            block.Row = Mathf.Clamp(Mathf.FloorToInt((lateral + strip.Width * 0.5f) / size), 0, strip.Rows - 1);
            block.Index = Mathf.Clamp(Mathf.FloorToInt(along / size), 0, Mathf.Max(0, Mathf.CeilToInt(length / size) - 1));
            block.CenterLine = TactileGeometry.SubPath(
                RowPath(strip, block.Row),
                block.Index * size,
                Mathf.Min(length, (block.Index + 1) * size));
            return block.CenterLine.Length >= 2;
        }

        // 点字ブロックを1枚だけ取り除く。ブロック列はその前後で2つに分かれる
        public static void RemoveBlock(BlockRef block)
        {
            TactileStrip strip = block.Strip;
            int index = StripList.IndexOf(strip);
            if (index < 0)
            {
                return;
            }

            List<TactileStrip> replacement = new List<TactileStrip>();
            for (int row = 0; row < strip.Rows; row++)
            {
                TactileStrip rowStrip = strip.Rows == 1 ? strip : CreateRowStrip(strip, row);
                if (row != block.Row)
                {
                    replacement.Add(rowStrip);
                    continue;
                }

                float size = TactileBlockTypes.BlockSize;
                float length = TactileGeometry.PathLength(rowStrip.Points);
                AddPiece(replacement, rowStrip, 0f, block.Index * size, true, false);
                AddPiece(replacement, rowStrip, (block.Index + 1) * size, length, false, true);
            }

            StripList.RemoveAt(index);
            StripList.InsertRange(index, replacement);
            RaiseChanged();
        }

        public static void RemoveLast()
        {
            if (StripList.Count == 0)
            {
                return;
            }

            StripList.RemoveAt(StripList.Count - 1);
            RaiseChanged();
        }

        public static void Clear()
        {
            StripList.Clear();
            RaiseChanged();
        }

        // 指定位置から maxDistance 以内にある最も近いブロック列を返す
        public static TactileStrip FindNearest(Vector3 position, float maxDistance)
        {
            TactileStrip best = null;
            float bestDistance = maxDistance;
            foreach (TactileStrip strip in StripList)
            {
                Vector3[] points = strip.Points;
                for (int i = 1; i < points.Length; i++)
                {
                    float distance = DistanceToSegmentXZ(position, points[i - 1], points[i]) - strip.Width * 0.5f;
                    if (distance < bestDistance && Mathf.Abs(position.y - points[i].y) < 5f)
                    {
                        bestDistance = distance;
                        best = strip;
                    }
                }
            }

            return best;
        }

        public static byte[] Serialize()
        {
            using (MemoryStream stream = new MemoryStream())
            {
                using (BinaryWriter writer = new BinaryWriter(stream))
                {
                    writer.Write(FormatVersion);
                    writer.Write(StripList.Count);
                    foreach (TactileStrip strip in StripList)
                    {
                        writer.Write((int)strip.Type);
                        writer.Write(strip.Rows);
                        writer.Write(strip.Points.Length);
                        foreach (Vector3 point in strip.Points)
                        {
                            writer.Write(point.x);
                            writer.Write(point.y);
                            writer.Write(point.z);
                        }

                        WriteNode(writer, strip.Start);
                        WriteNode(writer, strip.End);
                    }
                }

                return stream.ToArray();
            }
        }

        public static void Deserialize(byte[] data)
        {
            StripList.Clear();
            if (data != null && data.Length > 0)
            {
                using (MemoryStream stream = new MemoryStream(data))
                {
                    using (BinaryReader reader = new BinaryReader(stream))
                    {
                        int version = reader.ReadInt32();
                        if (version > FormatVersion)
                        {
                            throw new InvalidDataException("Unsupported data format version: " + version);
                        }

                        int count = reader.ReadInt32();
                        for (int i = 0; i < count; i++)
                        {
                            TactileStrip strip = new TactileStrip();
                            strip.Type = TactileBlockTypes.Clamp(reader.ReadInt32());
                            strip.Rows = Mathf.Clamp(reader.ReadInt32(), ModSettings.MinRows, ModSettings.MaxRows);
                            int pointCount = reader.ReadInt32();
                            strip.Points = new Vector3[pointCount];
                            for (int p = 0; p < pointCount; p++)
                            {
                                strip.Points[p] = new Vector3(
                                    reader.ReadSingle(),
                                    reader.ReadSingle(),
                                    reader.ReadSingle());
                            }

                            if (version >= 2)
                            {
                                strip.Start = ReadNode(reader);
                                strip.End = ReadNode(reader);
                            }

                            if (pointCount >= 2)
                            {
                                strip.Start.Position = strip.Points[0];
                                strip.End.Position = strip.Points[pointCount - 1];
                                StripList.Add(strip);
                            }
                        }
                    }
                }
            }

            RaiseChanged();
        }

        private const float MinimumPieceLength = 0.05f;

        private static Vector3[] RowPath(TactileStrip strip, int row)
        {
            float offset = (row + 0.5f) * TactileBlockTypes.BlockSize - strip.Width * 0.5f;
            return Mathf.Abs(offset) < 0.001f ? strip.Points : TactileGeometry.OffsetPath(strip.Points, offset);
        }

        // 複数列のブロック列を1列ずつに分ける。ずらした列はレーン情報を持たない
        private static TactileStrip CreateRowStrip(TactileStrip strip, int row)
        {
            Vector3[] points = RowPath(strip, row);
            return new TactileStrip
            {
                Type = strip.Type,
                Rows = 1,
                Points = points,
                Start = new SidewalkPoint { Position = points[0] },
                End = new SidewalkPoint { Position = points[points.Length - 1] }
            };
        }

        private static void AddPiece(List<TactileStrip> pieces, TactileStrip source, float from, float to, bool keepStart, bool keepEnd)
        {
            if (to - from < MinimumPieceLength)
            {
                return;
            }

            Vector3[] points = TactileGeometry.SubPath(source.Points, from, to);
            if (points.Length < 2)
            {
                return;
            }

            SidewalkPoint start = keepStart ? source.Start : new SidewalkPoint();
            SidewalkPoint end = keepEnd ? source.End : new SidewalkPoint();
            start.Position = points[0];
            end.Position = points[points.Length - 1];
            pieces.Add(new TactileStrip
            {
                Type = source.Type,
                Rows = 1,
                Points = points,
                Start = start,
                End = end
            });
        }

        private static void WriteNode(BinaryWriter writer, SidewalkPoint node)
        {
            writer.Write(node.Segment);
            writer.Write(node.Lane);
            writer.Write(node.LaneOffset);
            writer.Write(node.Lateral);
        }

        private static SidewalkPoint ReadNode(BinaryReader reader)
        {
            SidewalkPoint node = new SidewalkPoint();
            node.Segment = reader.ReadUInt16();
            node.Lane = reader.ReadUInt32();
            node.LaneOffset = reader.ReadSingle();
            node.Lateral = reader.ReadSingle();
            return node;
        }

        private static float DistanceToSegmentXZ(Vector3 point, Vector3 a, Vector3 b)
        {
            Vector2 p = new Vector2(point.x, point.z);
            Vector2 start = new Vector2(a.x, a.z);
            Vector2 end = new Vector2(b.x, b.z);
            Vector2 direction = end - start;
            float lengthSqr = direction.sqrMagnitude;
            float t = lengthSqr > 0f ? Mathf.Clamp01(Vector2.Dot(p - start, direction) / lengthSqr) : 0f;
            return Vector2.Distance(p, start + direction * t);
        }

        private static void RaiseChanged()
        {
            Action handler = Changed;
            if (handler != null)
            {
                handler();
            }
        }
    }
}

using System.Collections.Generic;
using ColossalFramework;
using ColossalFramework.Math;
using UnityEngine;

namespace TactilePaving
{
    // 敷設済みの点字ブロックをメッシュにまとめて毎フレーム描画する
    internal sealed class TactilePavingRenderer : MonoBehaviour
    {
        private const string PropShaderName = "Custom/Props/Prop/Default";
        private const float SurfaceOffset = 0.03f;
        private const int MaxVerticesPerMesh = 60000;

        private readonly List<Mesh>[] meshes = new List<Mesh>[TactileBlockTypes.Count];
        private Material[] materials;
        private Texture2D[] textures;
        private Texture2D[] normalTextures;
        private Texture2D aciTexture;
        private MaterialPropertyBlock propertyBlock;
        private int layer;
        private bool dirty = true;

        private void Awake()
        {
            for (int i = 0; i < meshes.Length; i++)
            {
                meshes[i] = new List<Mesh>();
            }

            layer = LayerMask.NameToLayer("Props");
            if (layer < 0)
            {
                layer = 0;
            }

            // ゲームのアセット読み込み処理が、元画像のない場合に作るのと同じ値
            // XYS: 平らな法線・反射なし（B は反転して保存される）、ACI: 不透明・色変化なし・発光なし
            aciTexture = TactileTextures.CreateDataTexture(new Color(0f, 0f, 0f, 1f));

            propertyBlock = new MaterialPropertyBlock();
            propertyBlock.SetColor(Shader.PropertyToID("_Color"), Color.white);
            propertyBlock.SetVector(Shader.PropertyToID("_ObjectIndex"), RenderManager.DefaultColorLocation);

            Material template = FindPropMaterial();
            textures = new Texture2D[TactileBlockTypes.Count];
            normalTextures = new Texture2D[TactileBlockTypes.Count];
            materials = new Material[TactileBlockTypes.Count];
            for (int i = 0; i < materials.Length; i++)
            {
                textures[i] = TactileTextures.CreateBlockTexture((TactileBlockType)i, true);
                normalTextures[i] = TactileTextures.CreateNormalTexture((TactileBlockType)i);
                materials[i] = CreateMaterial(template, textures[i], normalTextures[i]);
            }

            TactilePavingData.Changed += MarkDirty;
        }

        private void OnDestroy()
        {
            TactilePavingData.Changed -= MarkDirty;
            ClearMeshes();
            DestroyAll(materials);
            DestroyAll(textures);
            DestroyAll(normalTextures);
            Destroy(aciTexture);
        }

        private void Update()
        {
            if (dirty)
            {
                dirty = false;
                Rebuild();
            }

            for (int type = 0; type < meshes.Length; type++)
            {
                Material material = materials[type];
                if (material == null)
                {
                    continue;
                }

                foreach (Mesh mesh in meshes[type])
                {
                    Graphics.DrawMesh(mesh, Matrix4x4.identity, material, layer, null, 0, propertyBlock);
                }
            }
        }

        private void MarkDirty()
        {
            dirty = true;
        }

        private void Rebuild()
        {
            ClearMeshes();

            List<Vector3>[] vertices = new List<Vector3>[meshes.Length];
            List<Vector2>[] uvs = new List<Vector2>[meshes.Length];
            List<Vector4>[] tangents = new List<Vector4>[meshes.Length];
            List<int>[] triangles = new List<int>[meshes.Length];
            for (int i = 0; i < meshes.Length; i++)
            {
                vertices[i] = new List<Vector3>();
                uvs[i] = new List<Vector2>();
                tangents[i] = new List<Vector4>();
                triangles[i] = new List<int>();
            }

            foreach (TactileStrip strip in TactilePavingData.Strips)
            {
                int type = (int)strip.Type;
                if (vertices[type].Count + strip.Points.Length * 2 > MaxVerticesPerMesh)
                {
                    Flush(type, vertices[type], uvs[type], tangents[type], triangles[type]);
                }

                AppendStrip(strip, vertices[type], uvs[type], tangents[type], triangles[type]);
            }

            for (int type = 0; type < meshes.Length; type++)
            {
                Flush(type, vertices[type], uvs[type], tangents[type], triangles[type]);
            }
        }

        private static void AppendStrip(TactileStrip strip, List<Vector3> vertices, List<Vector2> uvs, List<Vector4> tangents, List<int> triangles)
        {
            Vector3[] points = strip.Points;
            float halfWidth = strip.Width * 0.5f;
            float length = 0f;
            int start = vertices.Count;

            for (int i = 0; i < points.Length; i++)
            {
                if (i > 0)
                {
                    length += VectorUtils.LengthXZ(points[i] - points[i - 1]);
                }

                Vector3 sideDirection = TactileGeometry.SideAt(points, i);
                Vector3 side = sideDirection * halfWidth;
                Vector3 center = points[i] + Vector3.up * SurfaceOffset;
                float u = length / TactileBlockTypes.BlockSize;

                vertices.Add(center - side);
                vertices.Add(center + side);
                uvs.Add(new Vector2(u, 0f));
                uvs.Add(new Vector2(u, strip.Rows));

                // 法線マップの向きを敷設方向に合わせる（接線 = u の方向、従法線 = v の方向）
                Vector4 tangent = new Vector4(-sideDirection.z, 0f, sideDirection.x, 1f);
                tangents.Add(tangent);
                tangents.Add(tangent);

                if (i > 0)
                {
                    int a = start + (i - 1) * 2;
                    triangles.Add(a);
                    triangles.Add(a + 2);
                    triangles.Add(a + 1);
                    triangles.Add(a + 1);
                    triangles.Add(a + 2);
                    triangles.Add(a + 3);
                }
            }
        }

        private void Flush(int type, List<Vector3> vertices, List<Vector2> uvs, List<Vector4> tangents, List<int> triangles)
        {
            if (vertices.Count == 0)
            {
                return;
            }

            Mesh mesh = new Mesh();
            mesh.name = "TactilePaving_" + (TactileBlockType)type;
            mesh.vertices = vertices.ToArray();
            mesh.uv = uvs.ToArray();
            mesh.triangles = triangles.ToArray();

            Vector3[] normals = new Vector3[vertices.Count];
            Color[] colors = new Color[vertices.Count];
            for (int i = 0; i < normals.Length; i++)
            {
                normals[i] = Vector3.up;
                colors[i] = Color.white;
            }

            mesh.normals = normals;
            mesh.tangents = tangents.ToArray();
            mesh.colors = colors;
            mesh.RecalculateBounds();
            meshes[type].Add(mesh);

            vertices.Clear();
            uvs.Clear();
            tangents.Clear();
            triangles.Clear();
        }

        private void ClearMeshes()
        {
            foreach (List<Mesh> list in meshes)
            {
                if (list == null)
                {
                    continue;
                }

                foreach (Mesh mesh in list)
                {
                    Destroy(mesh);
                }

                list.Clear();
            }
        }

        private Material CreateMaterial(Material template, Texture2D texture, Texture2D normalTexture)
        {
            Material material;
            if (template != null)
            {
                material = new Material(template);
            }
            else
            {
                Shader shader = Shader.Find(PropShaderName) ?? Shader.Find("Diffuse");
                if (shader == null)
                {
                    Debug.LogError("[TactilePaving] 描画用のシェーダーが見つかりません");
                    return null;
                }

                material = new Material(shader);
            }

            material.name = texture.name;
            material.mainTexture = texture;
            if (material.HasProperty("_XYSMap"))
            {
                material.SetTexture("_XYSMap", normalTexture);
            }

            if (material.HasProperty("_ACIMap"))
            {
                material.SetTexture("_ACIMap", aciTexture);
            }

            if (material.HasProperty("_Color"))
            {
                material.color = Color.white;
            }

            return material;
        }

        // バニラのプロップのマテリアルを複製元にして、ゲームの照明に馴染ませる
        private static Material FindPropMaterial()
        {
            int count = PrefabCollection<PropInfo>.LoadedCount();
            for (uint i = 0; i < count; i++)
            {
                PropInfo info = PrefabCollection<PropInfo>.GetLoaded(i);
                if (info == null || info.m_isDecal || info.m_material == null || info.m_material.shader == null)
                {
                    continue;
                }

                if (info.m_material.shader.name == PropShaderName)
                {
                    return info.m_material;
                }
            }

            return null;
        }

        private static void DestroyAll(Object[] objects)
        {
            if (objects == null)
            {
                return;
            }

            foreach (Object obj in objects)
            {
                if (obj != null)
                {
                    Destroy(obj);
                }
            }
        }
    }
}

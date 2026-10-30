using UnityEngine;

namespace TactilePaving
{
    // テクスチャはすべてコード内で生成するため、外部の画像ファイルは不要。
    // 突起の形は Height() にまとめ、色・法線・アイコンはすべてそこから作る
    internal static class TactileTextures
    {
        private const int TileSize = 128;
        private const int IconSize = 64;

        // 照明で白飛びしないよう、基本色に掛ける明るさ
        private const float FlatShade = 0.78f;
        private const float TopShade = 0.86f;
        private const float BevelShade = 0.66f;
        private const float JointShade = 0.45f;

        // ゲーム内ではこのマテリアルが画面上で約2.4倍明るく映る（目地の色の実測値より）ため、
        // 地面に敷くテクスチャだけ色を抑えておく。パネルのプレビューやアイコンには掛けない
        private const float WorldAlbedoScale = 0.5f;

        // 法線の強さ。大きいほど突起の陰影がはっきりする
        private const float BumpStrength = 0.02f;

        // 目地（ブロック同士のすき間）の幅
        private const float JointWidth = 0.012f;

        // テクスチャの u は敷設方向、v は幅方向（1ブロック = 1タイル）
        public static Texture2D CreateBlockTexture(TactileBlockType type, bool forWorld)
        {
            Color baseColor = TactileBlockTypes.BaseColor(type) * (forWorld ? WorldAlbedoScale : 1f);
            bool warning = TactileBlockTypes.IsWarning(type);

            Texture2D texture = new Texture2D(TileSize, TileSize, TextureFormat.ARGB32, true);
            texture.name = "TactilePaving_" + type;
            ConfigureTiling(texture);

            Color[] pixels = new Color[TileSize * TileSize];
            for (int y = 0; y < TileSize; y++)
            {
                for (int x = 0; x < TileSize; x++)
                {
                    float u = (x + 0.5f) / TileSize;
                    float v = (y + 0.5f) / TileSize;
                    Color color = baseColor * AlbedoShade(warning, u, v);
                    color.a = 1f;
                    pixels[y * TileSize + x] = color;
                }
            }

            texture.SetPixels(pixels);
            texture.Apply(true);
            return texture;
        }

        // ゲームの _XYSMap 形式: R,G = 法線のX,Y、B = 1 - 反射の強さ（反射なし）
        public static Texture2D CreateNormalTexture(TactileBlockType type)
        {
            bool warning = TactileBlockTypes.IsWarning(type);

            Texture2D texture = new Texture2D(TileSize, TileSize, TextureFormat.ARGB32, true, true);
            texture.name = "TactilePavingXYS_" + type;
            ConfigureTiling(texture);

            Color[] pixels = new Color[TileSize * TileSize];
            for (int y = 0; y < TileSize; y++)
            {
                for (int x = 0; x < TileSize; x++)
                {
                    float u = (x + 0.5f) / TileSize;
                    float v = (y + 0.5f) / TileSize;
                    Vector3 normal = Normal(warning, u, v);
                    pixels[y * TileSize + x] = new Color(normal.x * 0.5f + 0.5f, normal.y * 0.5f + 0.5f, 1f, 1f);
                }
            }

            texture.SetPixels(pixels);
            texture.Apply(true);
            return texture;
        }

        // 法線などの「色ではないデータ」を入れるテクスチャ。ゲームの読み込み処理と同じく
        // リニア扱いにしないと値が変換されてしまい、面が傾いて暗く濁った色になる
        public static Texture2D CreateDataTexture(Color value)
        {
            Texture2D texture = new Texture2D(4, 4, TextureFormat.ARGB32, false, true);
            texture.wrapMode = TextureWrapMode.Repeat;
            Color[] pixels = new Color[16];
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = value;
            }

            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }

        // UUIボタン用アイコン: 左半分が線状、右半分が点状の黄色いブロック。
        // ゲームの照明が当たらないので、左上から光が当たったように陰影を付ける
        public static Texture2D CreateIcon()
        {
            Texture2D texture = new Texture2D(IconSize, IconSize, TextureFormat.ARGB32, false);
            texture.name = "TactilePavingIcon";
            texture.filterMode = FilterMode.Bilinear;

            Color baseColor = TactileBlockTypes.BaseColor(TactileBlockType.GuideYellow);
            Color clear = new Color(0f, 0f, 0f, 0f);
            Color[] pixels = new Color[IconSize * IconSize];
            const int margin = 4;
            const int inner = IconSize - margin * 2;
            Vector3 light = new Vector3(-0.5f, 0.6f, 0.62f).normalized;

            for (int y = 0; y < IconSize; y++)
            {
                for (int x = 0; x < IconSize; x++)
                {
                    int ix = x - margin;
                    int iy = y - margin;
                    if (ix < 0 || iy < 0 || ix >= inner || iy >= inner)
                    {
                        pixels[y * IconSize + x] = clear;
                        continue;
                    }

                    if (ix < 2 || iy < 2 || ix >= inner - 2 || iy >= inner - 2)
                    {
                        pixels[y * IconSize + x] = new Color(0.2f, 0.15f, 0f, 1f);
                        continue;
                    }

                    // 左半分: 縦向きの線状ブロック、右半分: 点状ブロック。
                    // どちらも縦に正方形のタイルを2枚並べて、突起がつぶれないようにする
                    float s = (float)ix / inner;
                    float t = (float)iy / inner * 2f;
                    bool warning = s >= 0.5f;
                    float local = warning ? (s - 0.5f) * 2f : s * 2f;
                    float u = warning ? local : t;
                    float v = warning ? t : local;

                    Vector3 normal = Normal(warning, u, v);
                    if (!warning)
                    {
                        // u と v を入れ替えたので法線も入れ替える
                        normal = new Vector3(normal.y, normal.x, normal.z);
                    }

                    float lighting = 0.75f + 0.45f * Vector3.Dot(normal, light);
                    Color color = baseColor * (AlbedoShade(warning, u, v) / FlatShade) * lighting;
                    color.a = 1f;
                    pixels[y * IconSize + x] = color;
                }
            }

            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }

        private static void ConfigureTiling(Texture2D texture)
        {
            texture.wrapMode = TextureWrapMode.Repeat;
            texture.filterMode = FilterMode.Trilinear;
            texture.anisoLevel = 4;
        }

        private static float AlbedoShade(bool warning, float u, float v)
        {
            u = u - Mathf.Floor(u);
            v = v - Mathf.Floor(v);
            if (IsJoint(u, v))
            {
                return JointShade;
            }

            float height = Height(warning, u, v);
            if (height <= 0f)
            {
                return FlatShade;
            }

            // 突起の縁を少し暗くして、遠くからでも形が分かるようにする
            return Mathf.Lerp(BevelShade, TopShade, height);
        }

        private static Vector3 Normal(bool warning, float u, float v)
        {
            const float step = 1f / TileSize;
            float dhdu = (Height(warning, u + step, v) - Height(warning, u - step, v)) / (2f * step);
            float dhdv = (Height(warning, u, v + step) - Height(warning, u, v - step)) / (2f * step);
            return new Vector3(-dhdu * BumpStrength, -dhdv * BumpStrength, 1f).normalized;
        }

        private static bool IsJoint(float u, float v)
        {
            float edge = Mathf.Min(Mathf.Min(u, 1f - u), Mathf.Min(v, 1f - v));
            return edge < JointWidth;
        }

        // 突起の高さ（0: 平らな面、1: 突起の上面）。目地は少し低くする
        private static float Height(bool warning, float u, float v)
        {
            u = u - Mathf.Floor(u);
            v = v - Mathf.Floor(v);
            if (IsJoint(u, v))
            {
                return -0.3f;
            }

            return warning ? WarningHeight(u, v) : GuideHeight(u, v);
        }

        // 線状ブロック: 敷設方向に平行な、両端の丸い4本の突起
        private static float GuideHeight(float u, float v)
        {
            const int bars = 4;
            const float radius = 0.06f;
            const float barStart = 0.08f + radius;
            const float barEnd = 0.92f - radius;

            float center = (Mathf.Floor(v * bars) + 0.5f) / bars;
            float du = u - Mathf.Clamp(u, barStart, barEnd);
            float dv = v - center;
            return Profile(Mathf.Sqrt(du * du + dv * dv) / radius);
        }

        // 点状ブロック: 5×5の突起
        private static float WarningHeight(float u, float v)
        {
            const int dots = 5;
            const float radius = 0.3f;
            float lu = u * dots - Mathf.Floor(u * dots) - 0.5f;
            float lv = v * dots - Mathf.Floor(v * dots) - 0.5f;
            return Profile(Mathf.Sqrt(lu * lu + lv * lv) / radius);
        }

        // 上面が平らで縁が斜めになった断面（distance: 中心からの距離 / 半径）
        private static float Profile(float distance)
        {
            const float bevel = 0.35f;
            float t = Mathf.Clamp01((1f - distance) / bevel);
            return t * t * (3f - 2f * t);
        }
    }
}

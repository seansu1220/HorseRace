using UnityEngine;

namespace HorseRace.View
{
    /// <summary>執行期產生的小貼圖，不需要任何匯入的圖檔。</summary>
    public static class ProceduralTextures
    {
        /// <summary>
        /// 兩色直條紋（例如草坪的割草紋）：寬 2 像素、點取樣，
        /// 材質的 tiling.x 設成「表面長度 ÷ 兩條紋的寬度」即可得到指定寬度的條紋。
        /// </summary>
        public static Texture2D Stripes(string name, Color first, Color second)
        {
            Texture2D texture = new Texture2D(2, 1, TextureFormat.RGBA32, false);
            texture.name = name;
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Repeat;
            texture.SetPixel(0, 0, first);
            texture.SetPixel(1, 0, second);
            texture.Apply(false, true);
            return texture;
        }
    }
}

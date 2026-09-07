using System;
using UnityEngine;
using ZXing;
using ZXing.QrCode;
using ZXing.Rendering;

namespace HorseRace.View
{
    /// <summary>
    /// 產生 QRCode 貼圖。用 ZXing.Net（Apache-2.0，見 docs/THIRD_PARTY_NOTICES.md）。
    /// </summary>
    public static class QrCodeBuilder
    {
        /// <summary>
        /// 把文字編成 QRCode 貼圖。失敗時回傳 null 並記錄原因，
        /// 呼叫端要能接受沒有 QRCode 的情況——大螢幕上還是會顯示網址文字。
        /// </summary>
        public static Texture2D Create(string content, int size)
        {
            if (string.IsNullOrEmpty(content))
            {
                return null;
            }

            try
            {
                BarcodeWriterPixelData writer = new BarcodeWriterPixelData
                {
                    Format = BarcodeFormat.QR_CODE,
                    Options = new QrCodeEncodingOptions
                    {
                        Width = size,
                        Height = size,
                        Margin = 1,
                        CharacterSet = "UTF-8",
                        ErrorCorrection =
                            ZXing.QrCode.Internal.ErrorCorrectionLevel.M
                    }
                };

                PixelData pixelData = writer.Write(content);
                return BuildTexture(pixelData);
            }
            catch (Exception error)
            {
                Debug.LogError("[QrCodeBuilder] 產生 QRCode 失敗（內容：" + content + "）："
                               + error.GetType().Name + " - " + error.Message);
                return null;
            }
        }

        private static Texture2D BuildTexture(PixelData pixelData)
        {
            int width = pixelData.Width;
            int height = pixelData.Height;
            byte[] source = pixelData.Pixels;

            Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            // QRCode 是硬邊圖形，用點取樣才不會糊掉導致掃不到
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;

            Color32[] pixels = new Color32[width * height];
            Color32 black = new Color32(0, 0, 0, 255);
            Color32 white = new Color32(255, 255, 255, 255);

            for (int y = 0; y < height; y++)
            {
                // ZXing 的原點在左上，Unity 貼圖的原點在左下，要上下翻轉。
                // 不翻的話會得到上下顛倒的圖，手機掃不出來。
                int sourceRow = (height - 1 - y) * width;
                int targetRow = y * width;

                for (int x = 0; x < width; x++)
                {
                    // QRCode 只有黑與白，三個顏色通道的值一定相同，
                    // 所以取任一通道判斷亮度即可——不必去賭 ZXing 給的是 BGRA 還是 RGBA。
                    byte luminance = source[(sourceRow + x) * 4];
                    pixels[targetRow + x] = luminance < 128 ? black : white;
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            return texture;
        }
    }
}

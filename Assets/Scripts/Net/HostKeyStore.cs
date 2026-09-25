using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace HorseRace.Net
{
    /// <summary>
    /// 大螢幕連中繼伺服器用的金鑰。
    ///
    /// 開了對外通道之後，任何拿到網址的人都能連到中繼伺服器；
    /// 若不驗證，有人把自己的連線標成 role=host 就能冒充大螢幕、對全場手機亂發賽果。
    /// 伺服器由大螢幕自己啟動時，會透過環境變數拿到同一把金鑰。
    ///
    /// 金鑰存檔而不是每次重產，是為了對付「上次的伺服器還活著」的情況：
    /// 沿用舊伺服器時，它認得的還是上一把金鑰。
    /// </summary>
    internal static class HostKeyStore
    {
        private const int KeyBytes = 16;

        /// <summary>讀取既有金鑰，沒有就產生一把並存檔。存檔失敗時仍回傳可用的金鑰。</summary>
        public static string LoadOrCreate(string filePath, Action<string> onWarning)
        {
            string existing = TryRead(filePath);
            if (existing != null)
            {
                return existing;
            }

            string key = Generate();

            try
            {
                string directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(filePath, key, Encoding.ASCII);
            }
            catch (Exception error)
            {
                onWarning("無法儲存大螢幕金鑰（" + filePath + "）：" + error.Message
                          + "。本次仍可使用，但若沿用上次殘留的伺服器可能會被拒絕連線。");
            }

            return key;
        }

        private static string TryRead(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    return null;
                }

                string key = File.ReadAllText(filePath, Encoding.ASCII).Trim();
                return IsWellFormed(key) ? key : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static bool IsWellFormed(string key)
        {
            if (key.Length != KeyBytes * 2)
            {
                return false;
            }

            foreach (char character in key)
            {
                bool hex = (character >= '0' && character <= '9') || (character >= 'a' && character <= 'f');
                if (!hex)
                {
                    return false;
                }
            }

            return true;
        }

        private static string Generate()
        {
            byte[] bytes = new byte[KeyBytes];
            using (RandomNumberGenerator generator = RandomNumberGenerator.Create())
            {
                generator.GetBytes(bytes);
            }

            StringBuilder hex = new StringBuilder(KeyBytes * 2);
            foreach (byte value in bytes)
            {
                hex.Append(value.ToString("x2"));
            }

            return hex.ToString();
        }
    }
}

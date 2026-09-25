using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace HorseRace.Net
{
    /// <summary>
    /// 確保本機有 cloudflared.exe 可用，沒有就從官方 GitHub Release 下載一份。
    ///
    /// 不放進 repo：執行檔約 60 MB，而且官方會持續更新。
    /// 下載位置在使用者資料夾，編輯器與建置版共用，只有第一次會下載。
    /// </summary>
    internal static class CloudflaredInstaller
    {
        public const string ExecutableName = "cloudflared";

        /// <summary>
        /// 多久完全沒收到資料就放棄。刻意不設「總時間上限」：有些網路連 GitHub 只有 100 KB/s 上下，
        /// 55 MB 要下載十分鐘以上，慢但確實在動的下載不該被砍掉；真的卡死才放棄。
        /// </summary>
        private static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(60);

        /// <summary>小於這個大小的檔案不可能是完整的 cloudflared，多半是錯誤頁。</summary>
        private const long MinimumExecutableBytes = 1024 * 1024;

        private const int CopyBufferBytes = 81920;

        /// <summary>依序找：使用者指定的路徑 → PATH → 先前自動下載的位置。找不到回傳 null。</summary>
        public static string FindExisting(string configuredPath, string installPath)
        {
            if (!string.IsNullOrEmpty(configuredPath))
            {
                return File.Exists(configuredPath) ? configuredPath : null;
            }

            return ExecutableLocator.Find(ExecutableName, installPath);
        }

        /// <summary>
        /// 下載到 <paramref name="installPath"/>。先寫到 .part 暫存檔、驗證後才改名，
        /// 下載到一半斷掉不會留下一個「存在但壞掉」的執行檔。
        /// 失敗時拋出例外，訊息說明原因。
        /// </summary>
        public static async Task DownloadAsync(
            string downloadUrl, string installPath, Action<int> onProgressPercent, CancellationToken token)
        {
            string directory = Path.GetDirectoryName(installPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string partialPath = installPath + ".part";

            try
            {
                await DownloadToFileAsync(downloadUrl, partialPath, onProgressPercent, token).ConfigureAwait(false);
                VerifyExecutable(partialPath);

                if (File.Exists(installPath))
                {
                    File.Delete(installPath);
                }

                File.Move(partialPath, installPath);
            }
            catch
            {
                DeleteQuietly(partialPath);
                throw;
            }
        }

        private static async Task DownloadToFileAsync(
            string downloadUrl, string partialPath, Action<int> onProgressPercent, CancellationToken token)
        {
            using (CancellationTokenSource stall = CancellationTokenSource.CreateLinkedTokenSource(token))
            using (HttpClient client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan })
            {
                stall.CancelAfter(StallTimeout);

                try
                {
                    using (HttpResponseMessage response = await client
                               .GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, stall.Token)
                               .ConfigureAwait(false))
                    {
                        if (!response.IsSuccessStatusCode)
                        {
                            throw new IOException("下載 cloudflared 失敗，HTTP " + (int)response.StatusCode);
                        }

                        long? totalBytes = response.Content.Headers.ContentLength;
                        using (Stream source = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                        using (FileStream target = new FileStream(partialPath, FileMode.Create, FileAccess.Write))
                        {
                            await CopyWithProgressAsync(source, target, totalBytes, onProgressPercent, stall)
                                .ConfigureAwait(false);
                        }
                    }
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested)
                {
                    throw new IOException("下載 cloudflared 時超過 " + (int)StallTimeout.TotalSeconds + " 秒沒有任何進度");
                }
            }
        }

        /// <summary>清掉下載失敗留下的暫存檔。刪不掉也無妨，下次下載會直接覆寫。</summary>
        private static void DeleteQuietly(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception)
            {
                // 忽略
            }
        }

        /// <summary>邊複製邊回報進度；每收到一段資料就把 <paramref name="stall"/> 的計時重新歸零。</summary>
        private static async Task CopyWithProgressAsync(
            Stream source, Stream target, long? totalBytes, Action<int> onProgressPercent,
            CancellationTokenSource stall)
        {
            byte[] buffer = new byte[CopyBufferBytes];
            long copied = 0;
            int lastReported = -1;

            while (true)
            {
                int read = await source.ReadAsync(buffer, 0, buffer.Length, stall.Token).ConfigureAwait(false);
                if (read <= 0)
                {
                    break;
                }

                stall.CancelAfter(StallTimeout);
                await target.WriteAsync(buffer, 0, read, stall.Token).ConfigureAwait(false);
                copied += read;

                if (totalBytes.HasValue && totalBytes.Value > 0 && onProgressPercent != null)
                {
                    int percent = (int)(copied * 100 / totalBytes.Value);
                    if (percent != lastReported)
                    {
                        lastReported = percent;
                        onProgressPercent(percent);
                    }
                }
            }
        }

        /// <summary>檢查檔案大小與 Windows 執行檔開頭的「MZ」，擋掉 HTML 錯誤頁或截斷的檔案。</summary>
        private static void VerifyExecutable(string path)
        {
            FileInfo info = new FileInfo(path);
            if (info.Length < MinimumExecutableBytes)
            {
                throw new IOException("下載的 cloudflared 大小異常（" + info.Length + " bytes），可能不是執行檔");
            }

            using (FileStream stream = File.OpenRead(path))
            {
                int first = stream.ReadByte();
                int second = stream.ReadByte();
                if (first != 'M' || second != 'Z')
                {
                    throw new IOException("下載的 cloudflared 不是 Windows 執行檔");
                }
            }
        }
    }
}

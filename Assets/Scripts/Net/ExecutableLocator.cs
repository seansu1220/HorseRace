using System;
using System.IO;

namespace HorseRace.Net
{
    /// <summary>
    /// 找出外部執行檔的完整路徑。
    ///
    /// 自己找而不是把名字直接丟給作業系統，是為了在「找不到」時給出明確的訊息
    /// （例如「沒安裝 Node.js」），而不是一個看不懂的 Win32Exception。
    /// </summary>
    internal static class ExecutableLocator
    {
        /// <summary>
        /// 依序嘗試：指定的完整路徑 → PATH 環境變數 → <paramref name="fallbackPaths"/>。
        /// 找不到回傳 null。
        /// </summary>
        public static string Find(string command, params string[] fallbackPaths)
        {
            if (string.IsNullOrEmpty(command))
            {
                return null;
            }

            try
            {
                if (Path.IsPathRooted(command))
                {
                    return File.Exists(command) ? command : null;
                }

                string onPath = SearchPath(command);
                if (onPath != null)
                {
                    return onPath;
                }

                foreach (string candidate in fallbackPaths)
                {
                    if (!string.IsNullOrEmpty(candidate) && File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
            }
            catch (Exception)
            {
                // 路徑字串含非法字元之類的情況，一律當作找不到
            }

            return null;
        }

        private static string SearchPath(string command)
        {
            string pathVariable = Environment.GetEnvironmentVariable("PATH");
            if (string.IsNullOrEmpty(pathVariable))
            {
                return null;
            }

            bool hasExtension = Path.HasExtension(command);

            foreach (string rawDirectory in pathVariable.Split(Path.PathSeparator))
            {
                string directory = rawDirectory.Trim().Trim('"');
                if (directory.Length == 0)
                {
                    continue;
                }

                string exact = Path.Combine(directory, command);
                if (hasExtension && File.Exists(exact))
                {
                    return exact;
                }

                string withExe = exact + ".exe";
                if (!hasExtension && File.Exists(withExe))
                {
                    return withExe;
                }
            }

            return null;
        }
    }
}

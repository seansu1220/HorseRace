using System;
using System.Text;

namespace HorseRace.Net
{
    /// <summary>
    /// 把例外整理成給人看的一行字，連同所有內層例外一起列出。
    ///
    /// 只印最外層常常看不出原因：例如 TypeInitializationException 本身只說
    /// 「某個類別初始化失敗」，真正的原因在 InnerException 裡。
    /// </summary>
    internal static class ErrorText
    {
        private const int MaxDepth = 6;

        /// <summary>例：「TypeInitializationException: ... ← InvalidOperationException: ...」。</summary>
        public static string Describe(Exception error)
        {
            if (error == null)
            {
                return "（沒有例外資訊）";
            }

            StringBuilder text = new StringBuilder();
            Exception current = error;

            for (int depth = 0; current != null && depth < MaxDepth; depth++)
            {
                if (depth > 0)
                {
                    text.Append(" ← ");
                }

                text.Append(current.GetType().Name).Append(": ").Append(current.Message);

                AggregateException aggregate = current as AggregateException;
                current = aggregate != null && aggregate.InnerExceptions.Count == 1
                    ? aggregate.InnerExceptions[0]
                    : current.InnerException;
            }

            return text.ToString();
        }

        /// <summary>未預期的錯誤連堆疊一起記下，現場事後才查得出發生在哪一行。</summary>
        public static string DescribeWithStack(Exception error)
        {
            return error == null ? Describe(null) : Describe(error) + Environment.NewLine + error;
        }
    }
}

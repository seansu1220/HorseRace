using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;

namespace HorseRace.Net
{
    /// <summary>
    /// 由大螢幕啟動並負責收掉的子行程（中繼伺服器、cloudflared、npm install）。
    ///
    /// 執行緒約定：<c>onOutputLine</c> 會在背景執行緒被呼叫，stdout 與 stderr 可能同時進來，
    /// 呼叫端只能做 thread-safe 的事（丟佇列、設 TaskCompletionSource），不可碰 Unity API。
    /// </summary>
    internal sealed class ChildProcess : IDisposable
    {
        private readonly Process _process;
        private readonly TaskCompletionSource<int> _exited =
            new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

        private ChildProcess(Process process)
        {
            _process = process;
        }

        /// <summary>行程結束時完成，結果是結束代碼（取不到時為 -1）。</summary>
        public Task<int> Exited
        {
            get { return _exited.Task; }
        }

        /// <summary>
        /// 啟動行程。找不到執行檔等失敗會拋出例外，由呼叫端轉成給人看的狀態。
        /// <paramref name="onWarning"/> 用來回報「行程照跑、但少了保險」這類不致命的狀況。
        /// </summary>
        public static ChildProcess Start(
            ProcessStartInfo startInfo, Action<string> onOutputLine, Action<string> onWarning)
        {
            startInfo.UseShellExecute = false;
            startInfo.CreateNoWindow = true;
            startInfo.RedirectStandardOutput = true;
            startInfo.RedirectStandardError = true;
            startInfo.StandardOutputEncoding = Encoding.UTF8;
            startInfo.StandardErrorEncoding = Encoding.UTF8;

            Process process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            ChildProcess child = new ChildProcess(process);

            DataReceivedEventHandler forward = (sender, args) =>
            {
                if (args.Data != null && onOutputLine != null)
                {
                    onOutputLine(args.Data);
                }
            };

            process.OutputDataReceived += forward;
            process.ErrorDataReceived += forward;
            process.Exited += (sender, args) => child._exited.TrySetResult(child.ReadExitCode());

            try
            {
                process.Start();
            }
            catch
            {
                process.Dispose();
                throw;
            }

            string jobFailure;
            if (!ProcessJob.TryAssign(process, out jobFailure) && onWarning != null)
            {
                onWarning("子行程未能綁定到主程式（" + jobFailure + "），程式異常結束時可能需要手動關閉 "
                          + startInfo.FileName);
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            return child;
        }

        /// <summary>結束行程（若還在跑）並釋放資源。可重複呼叫。</summary>
        public void Dispose()
        {
            try
            {
                if (!_process.HasExited)
                {
                    _process.Kill();
                }
            }
            catch (InvalidOperationException)
            {
                // 行程已經結束或從未成功啟動
            }
            catch (Win32Exception)
            {
                // 行程正在結束中，Kill 會被拒絕；結果一樣
            }

            _process.Dispose();
            _exited.TrySetResult(-1);
        }

        private int ReadExitCode()
        {
            try
            {
                return _process.ExitCode;
            }
            catch (Exception)
            {
                return -1;
            }
        }
    }
}

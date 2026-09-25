using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace HorseRace.Net
{
    /// <summary>
    /// 把子行程綁在大螢幕程式的生命週期上（Windows Job Object）。
    ///
    /// 正常結束時 <see cref="ChildProcess"/> 會自己收掉子行程；這個類別是給「沒機會正常結束」的情況——
    /// 程式當掉或被工作管理員強制結束時，作業系統會連帶關掉所有掛在 Job 底下的行程，
    /// 不會留下佔著 8080 埠的孤兒 node 或一直連著 Cloudflare 的 cloudflared。
    ///
    /// Job 的 handle 刻意從不關閉：它一關，底下的子行程就會被砍。
    /// 行程結束時作業系統會替我們關。
    /// </summary>
    internal static class ProcessJob
    {
        private const int JobObjectExtendedLimitInformation = 9;
        private const uint JobObjectLimitKillOnJobClose = 0x2000;

        private static readonly object Gate = new object();
        private static IntPtr _job = IntPtr.Zero;
        private static bool _unavailable;

        /// <summary>
        /// 把行程加入 Job。失敗只回傳 false，不拋例外——
        /// 少了這層保險子行程照樣能跑，只是程式當掉時要手動收拾。
        /// </summary>
        public static bool TryAssign(Process process, out string failure)
        {
            failure = null;

            try
            {
                IntPtr job = GetOrCreateJob(out failure);
                if (job == IntPtr.Zero)
                {
                    return false;
                }

                if (!AssignProcessToJobObject(job, process.Handle))
                {
                    failure = "AssignProcessToJobObject 失敗，錯誤碼 " + Marshal.GetLastWin32Error();
                    return false;
                }

                return true;
            }
            catch (Exception error)
            {
                failure = error.GetType().Name + " - " + error.Message;
                return false;
            }
        }

        private static IntPtr GetOrCreateJob(out string failure)
        {
            failure = null;

            lock (Gate)
            {
                if (_job != IntPtr.Zero)
                {
                    return _job;
                }

                if (_unavailable)
                {
                    failure = "Job Object 先前建立失敗";
                    return IntPtr.Zero;
                }

                IntPtr job = CreateJobObject(IntPtr.Zero, null);
                if (job == IntPtr.Zero)
                {
                    _unavailable = true;
                    failure = "CreateJobObject 失敗，錯誤碼 " + Marshal.GetLastWin32Error();
                    return IntPtr.Zero;
                }

                if (!EnableKillOnClose(job))
                {
                    _unavailable = true;
                    failure = "SetInformationJobObject 失敗，錯誤碼 " + Marshal.GetLastWin32Error();
                    CloseHandle(job);
                    return IntPtr.Zero;
                }

                _job = job;
                return _job;
            }
        }

        private static bool EnableKillOnClose(IntPtr job)
        {
            JobObjectExtendedLimit limit = new JobObjectExtendedLimit();
            limit.BasicLimit.LimitFlags = JobObjectLimitKillOnJobClose;

            int size = Marshal.SizeOf(typeof(JobObjectExtendedLimit));
            IntPtr buffer = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(limit, buffer, false);
                return SetInformationJobObject(job, JobObjectExtendedLimitInformation, buffer, (uint)size);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        // ---- Win32 ----

        [StructLayout(LayoutKind.Sequential)]
        private struct JobObjectBasicLimit
        {
            public long PerProcessUserTimeLimit;
            public long PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize;
            public UIntPtr MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass;
            public uint SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IoCounters
        {
            public ulong ReadOperationCount;
            public ulong WriteOperationCount;
            public ulong OtherOperationCount;
            public ulong ReadTransferCount;
            public ulong WriteTransferCount;
            public ulong OtherTransferCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JobObjectExtendedLimit
        {
            public JobObjectBasicLimit BasicLimit;
            public IoCounters IoInfo;
            public UIntPtr ProcessMemoryLimit;
            public UIntPtr JobMemoryLimit;
            public UIntPtr PeakProcessMemoryUsed;
            public UIntPtr PeakJobMemoryUsed;
        }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateJobObject(IntPtr securityAttributes, string name);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetInformationJobObject(
            IntPtr job, int infoClass, IntPtr info, uint infoLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr handle);
    }
}

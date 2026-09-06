using System;

namespace HorseRace.Core.Protocol
{
    /// <summary>
    /// 大螢幕與手機之間的訊息定義。
    ///
    /// 全部是 public field 而非 property，因為 Unity 的 JsonUtility 只認得 field；
    /// 也刻意不做多型，改用一個扁平的 <see cref="InboundMessage"/> 接所有上行訊息——
    /// JsonUtility 沒有多型反序列化能力，扁平結構是最不會出錯的做法。
    ///
    /// **手機端 JS 的欄位名必須與這裡完全一致**，協定變更時兩邊要同一個 commit 一起改。
    /// </summary>
    public static class MessageType
    {
        // 上行（手機 → 大螢幕）
        public const string Join = "join";
        public const string Step = "step";

        // 下行（大螢幕 → 手機）
        public const string Phase = "phase";
        public const string Drive = "drive";
    }

    /// <summary>手機送上來的訊息。所有上行訊息共用這一個扁平結構。</summary>
    [Serializable]
    public sealed class InboundMessage
    {
        /// <summary>訊息種類，見 <see cref="MessageType"/>。</summary>
        public string t;

        /// <summary>目標閘號，0 起算。</summary>
        public int lane = -1;

        /// <summary>本次回報的步數增量。</summary>
        public int n;

        /// <summary>玩家暱稱。</summary>
        public string nick;

        /// <summary>玩家識別碼，由手機端存在 localStorage。</summary>
        public string pid;
    }

    /// <summary>單匹馬公開給手機端的資訊。</summary>
    [Serializable]
    public sealed class HorseInfo
    {
        public int id;
        public string name;
        public string color;
        public double odds;
    }

    /// <summary>階段變更。倒數一律傳結束時間戳，由手機自行遞減，避免網路抖動造成秒數跳動。</summary>
    [Serializable]
    public sealed class PhaseMessage
    {
        public string t = MessageType.Phase;
        public string phase;

        /// <summary>本階段結束的 Unix 毫秒時間戳。Racing 階段為 0（由比賽本身決定長度）。</summary>
        public long endsAt;

        public int race;
        public HorseInfo[] horses;
    }

    /// <summary>各匹馬目前的體力驅動強度 0~1，讓手機端能看到自己搖出來的效果。</summary>
    [Serializable]
    public sealed class DriveMessage
    {
        public string t = MessageType.Drive;

        /// <summary>依閘號排列的驅動強度。</summary>
        public float[] d;
    }
}

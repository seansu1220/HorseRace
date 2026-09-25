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
        public const string Bet = "bet";
        public const string Item = "item";

        // 下行（大螢幕 → 手機）
        public const string Phase = "phase";
        public const string Drive = "drive";
        public const string Wallet = "wallet";
        public const string Result = "result";
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

        /// <summary>下注金額。</summary>
        public int amount;

        /// <summary>玩家暱稱。</summary>
        public string nick;

        /// <summary>玩家識別碼，由手機端存在 localStorage。</summary>
        public string pid;

        /// <summary>道具券種類（item 訊息用），見 <see cref="ItemKinds"/>。</summary>
        public string kind;
    }

    /// <summary>道具券種類在協定上的字串，與 <see cref="ItemKind"/> 一對一。</summary>
    public static class ItemKinds
    {
        public const string Boost = "boost";
        public const string Slow = "slow";
        public const string Obstacle = "obstacle";

        /// <summary>解析手機送來的種類字串；不認得的一律拒絕，不猜。</summary>
        public static bool TryParse(string wire, out ItemKind kind)
        {
            switch (wire)
            {
                case Boost:
                    kind = ItemKind.Boost;
                    return true;
                case Slow:
                    kind = ItemKind.Slow;
                    return true;
                case Obstacle:
                    kind = ItemKind.Obstacle;
                    return true;
                default:
                    kind = ItemKind.Boost;
                    return false;
            }
        }

        public static string ToWire(ItemKind kind)
        {
            switch (kind)
            {
                case ItemKind.Slow:
                    return Slow;
                case ItemKind.Obstacle:
                    return Obstacle;
                default:
                    return Boost;
            }
        }
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

        /// <summary>
        /// <see cref="RacePhase"/> 的小寫名稱：lobby / idle / betting / racing / photo / settle。
        /// </summary>
        public string phase;

        /// <summary>本階段結束的 Unix 毫秒時間戳。Racing 與 Lobby 為 0（沒有倒數）。</summary>
        public long endsAt;

        public int race;
        public HorseInfo[] horses;

        /// <summary>目前入場人數。有人入場時會重播階段訊息，等待畫面的人數因此會跟著更新。</summary>
        public int players;

        // ---- 規則數值：手機只拿來顯示與播動畫，實際判定一律在大螢幕 ----

        /// <summary>最低下注金額。</summary>
        public int minBet;

        /// <summary>每張道具券的價格。</summary>
        public int itemCost;

        /// <summary>道具效果持續秒數。</summary>
        public double itemSeconds;

        /// <summary>同一種券兩次購買之間的冷卻秒數（券面「轉一圈」的時間）。</summary>
        public double itemCooldown;

        /// <summary>加速券的速度倍率，例如 1.35。</summary>
        public double boostX;

        /// <summary>減速券的速度倍率，例如 0.6。</summary>
        public double slowX;

        /// <summary>障礙券的價格。</summary>
        public int obstacleCost;

        /// <summary>撞到障礙物後停住的秒數。</summary>
        public double obstacleSeconds;

        /// <summary>障礙券兩次購買之間的冷卻秒數。</summary>
        public double obstacleCooldown;
    }

    /// <summary>
    /// 比賽中的即時賽況（每秒數次），全部依閘號排列：
    /// 驅動強度讓搖的人看到效果，進度與效果狀態讓手機畫出場上名次。
    /// </summary>
    [Serializable]
    public sealed class DriveMessage
    {
        public string t = MessageType.Drive;

        /// <summary>驅動強度 0~1（所有替這匹馬搖的人加總）。</summary>
        public float[] d;

        /// <summary>賽程進度 0~1。</summary>
        public float[] p;

        /// <summary>道具狀態的位元旗標（見 <see cref="EffectFlags"/>），可同時成立。</summary>
        public int[] fx;
    }

    /// <summary><see cref="DriveMessage.fx"/> 的位元定義。</summary>
    public static class EffectFlags
    {
        public const int Boosted = 1;
        public const int Slowed = 2;

        /// <summary>撞到障礙物，正停在原地。</summary>
        public const int Stunned = 4;

        /// <summary>前方有障礙物等著。</summary>
        public const int ObstacleAhead = 8;
    }

    /// <summary>一筆注。</summary>
    [Serializable]
    public sealed class BetInfo
    {
        public int lane;
        public int amount;
    }

    /// <summary>
    /// 個人錢包。這是唯一一種「只送給單一玩家」的訊息，
    /// 中繼站看到 <see cref="to"/> 就只轉給對應的那一台手機。
    /// </summary>
    [Serializable]
    public sealed class WalletMessage
    {
        public string t = MessageType.Wallet;

        /// <summary>目標玩家的識別碼。中繼站據此定向轉發。</summary>
        public string to;

        public string nick;
        public int balance;
        public BetInfo[] bets;

        /// <summary>上一場的派彩金額（含本金）。</summary>
        public int payout;

        /// <summary>上一場的淨輸贏，可為負數。</summary>
        public int delta;

        /// <summary>下注或買券被拒絕的原因；空字串代表沒有問題。</summary>
        public string reject;

        /// <summary>加速券還要冷卻幾秒，0 代表可以買。手機據此畫券面的倒數。</summary>
        public double boostCool;

        /// <summary>減速券還要冷卻幾秒，0 代表可以買。</summary>
        public double slowCool;

        /// <summary>障礙券還要冷卻幾秒，0 代表可以買。</summary>
        public double obstacleCool;
    }

    /// <summary>排行榜的一列。</summary>
    [Serializable]
    public sealed class LeaderEntry
    {
        public string name;
        public int balance;
    }

    /// <summary>某名玩家對某匹馬用了幾張某種券（賽後統計的一列）。</summary>
    [Serializable]
    public sealed class UsageEntry
    {
        public int lane;

        /// <summary>券的種類，見 <see cref="ItemKinds"/>。</summary>
        public string kind;

        public string name;
        public int count;
    }

    /// <summary>賽果與排行榜，廣播給所有手機。</summary>
    [Serializable]
    public sealed class ResultMessage
    {
        public string t = MessageType.Result;

        /// <summary>依名次排列的閘號，索引 0 是冠軍。</summary>
        public int[] order;

        /// <summary>依名次排列的完賽秒數，與 <see cref="order"/> 一一對應。</summary>
        public float[] times;

        public LeaderEntry[] top;

        /// <summary>本場每匹馬被誰用了什麼券、幾張。</summary>
        public UsageEntry[] usage;
    }
}

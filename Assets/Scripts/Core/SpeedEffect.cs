namespace HorseRace.Core
{
    /// <summary>道具種類。之後手機端送上來的字串會對應到這裡，不使用 magic string。</summary>
    public enum EffectKind
    {
        /// <summary>加速。</summary>
        Boost = 0,

        /// <summary>絆腳減速。刻意不做「完全靜止」——見 docs/ARCHITECTURE.md §4。</summary>
        Slow = 1
    }

    /// <summary>套在某匹馬身上、會隨時間消退的速度倍率。</summary>
    public sealed class SpeedEffect
    {
        public EffectKind Kind;

        /// <summary>速度倍率。1.35 代表加速三成五，0.6 代表掉速四成。</summary>
        public double Multiplier;

        /// <summary>剩餘秒數。歸零後由 RaceEngine 移除。</summary>
        public double RemainingSeconds;

        /// <summary>發動者。大螢幕要顯示「誰對誰用了什麼」，這是全場最好笑的部分。</summary>
        public string SourcePlayerId;

        /// <summary>發動者暱稱，供大螢幕直接顯示，省得再查名冊。</summary>
        public string SourceNickname;

        public SpeedEffect Clone()
        {
            return new SpeedEffect
            {
                Kind = Kind,
                Multiplier = Multiplier,
                RemainingSeconds = RemainingSeconds,
                SourcePlayerId = SourcePlayerId,
                SourceNickname = SourceNickname
            };
        }
    }
}

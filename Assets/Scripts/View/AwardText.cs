using HorseRace.Core;

namespace HorseRace.View
{
    /// <summary>賽後獎項的名稱與成績說明。大螢幕與手機顯示同一份文字（手機的由大螢幕送過去）。</summary>
    public static class AwardText
    {
        public static string Title(AwardKind kind)
        {
            switch (kind)
            {
                case AwardKind.TicketTycoon:
                    return "券券富翁";
                case AwardKind.TopCheerleader:
                    return "最強啦啦隊";
                case AwardKind.Roadblocker:
                    return "路霸";
                default:
                    return "本場大贏家";
            }
        }

        public static string Detail(Award award)
        {
            switch (award.Kind)
            {
                case AwardKind.TicketTycoon:
                    return "買了 " + award.Value + " 張券";
                case AwardKind.TopCheerleader:
                    return "出力 " + award.Value + " 步";
                case AwardKind.Roadblocker:
                    return "放了 " + award.Value + " 個障礙物";
                default:
                    return "贏了 " + award.Value + " 籌碼";
            }
        }
    }
}

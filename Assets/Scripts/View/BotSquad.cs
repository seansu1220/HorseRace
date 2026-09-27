using System;
using System.Collections.Generic;
using HorseRace.Core;
using HorseRace.Core.Protocol;

namespace HorseRace.View
{
    /// <summary>
    /// 測試用的電腦玩家。每個電腦玩家由 <see cref="BotBrain"/> 決策，產出的訊息交給
    /// <c>sink</c>——也就是處理手機訊息的同一個函式，所以測到的就是真人會走的流程
    /// （下注、加入啦啦隊、用券、出力、播報、結算、頒獎）。
    /// </summary>
    public sealed class BotSquad
    {
        private const string IdPrefix = "bot-";

        private readonly BotConfig _config;
        private readonly Action<InboundMessage> _sink;
        private readonly List<BotBrain> _bots = new List<BotBrain>();
        private readonly int _seedBase;
        private int _nextNumber = 1;

        public BotSquad(BotConfig config, Action<InboundMessage> sink, int seedBase)
        {
            _config = config;
            _sink = sink;
            _seedBase = seedBase;
        }

        public int Count
        {
            get { return _bots.Count; }
        }

        /// <summary>新增電腦玩家（受 <see cref="BotConfig.MaxBots"/> 限制），回傳實際新增幾個。</summary>
        public int Add(int count)
        {
            int added = 0;
            while (added < count && _bots.Count < _config.MaxBots)
            {
                int number = _nextNumber++;
                BotBrain bot = new BotBrain(IdPrefix + number, "電腦" + number, _config, _seedBase + number * 7919);
                _bots.Add(bot);
                _sink(bot.JoinMessage());
                added++;
            }

            return added;
        }

        /// <summary>移除所有電腦玩家，並把他們從帳本刪掉（排行榜上不會留著）。</summary>
        public int RemoveAll(BettingBook book)
        {
            int removed = _bots.Count;
            foreach (BotBrain bot in _bots)
            {
                book.Remove(bot.PlayerId);
            }

            _bots.Clear();
            return removed;
        }

        /// <summary>每幀推進所有電腦玩家的決策，把要送的訊息交給 sink。</summary>
        public void Tick(GameLoop loop, double deltaSeconds)
        {
            if (_bots.Count == 0 || loop.Lineup == null)
            {
                return;
            }

            int laneCount = loop.Lineup.Length;
            foreach (BotBrain bot in _bots)
            {
                PlayerAccount account = loop.Book.Find(bot.PlayerId);
                List<InboundMessage> actions = bot.Think(
                    loop.Phase, account, loop.Cheer.IsMember(bot.PlayerId), laneCount, deltaSeconds);

                for (int i = 0; i < actions.Count; i++)
                {
                    _sink(actions[i]);
                }
            }
        }
    }
}

'use strict';

/**
 * 手機端。依大螢幕推來的階段切換畫面，每個階段只顯示當下能做的事：
 * 進場 → 等待 → 下注 → 比賽（買券＋出力）→ 結果。
 *
 * 所有規則（能不能下注、券夠不夠錢、冷卻）都由大螢幕判定，這裡只負責顯示與送出操作；
 * 規則數值（最低下注、券價、效果秒數）也由大螢幕在 phase 訊息中提供。
 *
 * 欄位名必須與 Assets/Scripts/Core/Protocol/Messages.cs 完全一致，
 * 協定變更時兩邊要同一個 commit 一起改。
 */

// ---------------------------------------------------------------- 設定（純介面，不影響規則）

/** 每隔多久把累積的步數送出一次。太密會塞爆連線，太疏會讓力度條一頓一頓。 */
const SEND_INTERVAL_MS = 200;

/**
 * 搖動偵測：加速度偏離基準線多少 m/s² 算一步。
 * 刻意設高：要真的用力甩才算，輕輕晃不算（大螢幕另有每人每秒 10 步的上限）。
 */
const SHAKE_THRESHOLD = 6.0;

/** 兩步之間的最短間隔，濾掉單次晃動造成的連續觸發。 */
const SHAKE_REFRACTORY_MS = 150;

/** 重連退避上限。 */
const RECONNECT_MAX_MS = 5000;

/** 下注金額的快捷鍵。「全部」與自訂輸入另外處理。 */
const PRESET_AMOUNTS = [1, 10, 100, 1000];

/** 買券送出後多久沒回應就放棄等待（通常 0.2 秒內就會回來）。 */
const ITEM_PENDING_TIMEOUT_MS = 4000;

const KINDS = ['boost', 'slow', 'obstacle'];
const KIND_NAMES = { boost: '加速券', slow: '減速券', obstacle: '障礙券' };
const KIND_SHORT = { boost: '加速', slow: '減速', obstacle: '障礙' };

// ---------------------------------------------------------------- 狀態

const state = {
  socket: null,
  connected: false,
  reconnectDelay: 500,

  playerId: localStorage.getItem('pid') || createId(),
  nickname: localStorage.getItem('nick') || '',
  joined: false,

  phase: '',
  endsAt: 0,
  raceNumber: 0,
  players: 0,
  horses: [],
  rules: {
    minBet: 1, itemCost: 5, itemSeconds: 2, itemCooldown: 2, boostX: 1.2, slowX: 0.8,
    obstacleCost: 10, obstacleSeconds: 2, obstacleCooldown: 2, cheerCost: 5,
  },

  // 這一場是否已加入啦啦隊：只有加入的人比賽中能搖手機出力
  cheer: false,

  balance: 0,
  bets: [],
  delta: 0,
  payout: 0,

  amountMode: 'preset', // preset | all | custom
  presetAmount: 100,
  lastAction: '', // bet | item：錢包回來時，被拒的原因要顯示在哪裡

  // 比賽中
  progress: [],
  effects: [],
  driveLevels: [],
  driveLane: -1,
  driveChosen: false,
  // 買券要「券」和「馬」都選好才送出，送出後兩邊都清掉
  selectedKind: '',
  selectedLane: -1,
  cooldown: {
    boost: { until: 0, total: 1 }, slow: { until: 0, total: 1 }, obstacle: { until: 0, total: 1 },
  },
  pending: { kind: '', lane: -1, at: 0 },

  pendingSteps: 0,

  finishOrder: [],
  finishTimes: [],
  usage: [],
  awards: [],
  leaders: [],
  finalTop: [],
  upCounts: [],
  downCounts: [],
  obstacleCounts: [],
};

localStorage.setItem('pid', state.playerId);

function createId() {
  return 'p' + Math.random().toString(36).slice(2, 10);
}

const dom = {};
for (const id of [
  'phaseLabel', 'countdown', 'balanceBox', 'balance', 'bonusFlash', 'connection',
  'endScreen', 'endChips', 'endLine', 'endBoard',
  'joinScreen', 'nickInput', 'joinButton',
  'waitScreen', 'waitWho', 'waitBalance', 'waitHint', 'waitCrowd', 'waitCount', 'waitListTitle', 'waitList',
  'betScreen', 'chipTray', 'customBox', 'customAmount', 'oddsList', 'betNote',
  'cheerButton', 'cheerTitle', 'cheerHint', 'cheerPrice', 'cheerCost', 'awardsTitle', 'awardList',
  'slipRace', 'slipLines', 'slipTotal',
  'raceScreen', 'fieldList', 'boostTicket', 'slowTicket', 'obstacleTicket',
  'boostEffect', 'slowEffect', 'obstacleEffect',
  'itemPicks', 'drivePct', 'drivePicks', 'meterFill', 'tapPad', 'tapHint',
  'motionButton', 'motionNote', 'toast',
  'resultScreen', 'resultLabel', 'resultDelta', 'resultLine', 'resultOrder', 'leaderList',
]) {
  dom[id] = document.getElementById(id);
}

const tickets = { boost: dom.boostTicket, slow: dom.slowTicket, obstacle: dom.obstacleTicket };

// ---------------------------------------------------------------- 連線

function relayUrl() {
  const scheme = location.protocol === 'https:' ? 'wss:' : 'ws:';
  return `${scheme}//${location.host}/ws?role=play`;
}

function connect() {
  if (state.socket && (state.socket.readyState === WebSocket.OPEN ||
                       state.socket.readyState === WebSocket.CONNECTING)) {
    return;
  }

  let socket;
  try {
    socket = new WebSocket(relayUrl());
  } catch (error) {
    scheduleReconnect();
    return;
  }

  state.socket = socket;

  socket.addEventListener('open', () => {
    state.connected = true;
    state.reconnectDelay = 500;
    dom.connection.classList.add('online');
    dom.connection.classList.remove('offline');
    if (state.nickname) sendJoin();
    renderHeader();
  });

  socket.addEventListener('message', (event) => {
    let message;
    try {
      message = JSON.parse(event.data);
    } catch (error) {
      return; // 壞封包直接丟掉，不影響其他運作
    }
    handleMessage(message);
  });

  socket.addEventListener('close', () => {
    state.connected = false;
    dom.connection.classList.remove('online');
    dom.connection.classList.add('offline');
    renderHeader();
    scheduleReconnect();
  });

  socket.addEventListener('error', () => {
    try { socket.close(); } catch (_) { /* 忽略 */ }
  });
}

function scheduleReconnect() {
  const delay = state.reconnectDelay;
  state.reconnectDelay = Math.min(delay * 2, RECONNECT_MAX_MS);
  setTimeout(connect, delay);
}

function send(payload) {
  if (!state.socket || state.socket.readyState !== WebSocket.OPEN) return false;
  try {
    state.socket.send(JSON.stringify(payload));
    return true;
  } catch (error) {
    return false; // 送不出去就算了，下一次操作或重連會補上
  }
}

function sendJoin() {
  send({ t: 'join', pid: state.playerId, nick: state.nickname });
}

// ---------------------------------------------------------------- 收訊息

function handleMessage(message) {
  switch (message.t) {
    case 'phase':
      onPhaseMessage(message);
      break;
    case 'wallet':
      onWallet(message);
      break;
    case 'drive':
      onRaceSnapshot(message);
      break;
    case 'final':
      state.finalTop = Array.isArray(message.top) ? message.top : [];
      render();
      break;
    case 'result':
      state.finishOrder = Array.isArray(message.order) ? message.order : [];
      state.finishTimes = Array.isArray(message.times) ? message.times : [];
      state.usage = Array.isArray(message.usage) ? message.usage : [];
      state.awards = Array.isArray(message.awards) ? message.awards : [];
      state.leaders = Array.isArray(message.top) ? message.top : [];
      render();
      break;
  }
}

function onPhaseMessage(message) {
  const previousPhase = state.phase;
  const previousRace = state.raceNumber;

  state.phase = message.phase || '';
  state.endsAt = message.endsAt || 0;
  state.raceNumber = message.race || 0;
  state.players = message.players | 0;
  if (Array.isArray(message.horses)) state.horses = message.horses;

  const rules = state.rules;
  if (message.minBet > 0) rules.minBet = message.minBet;
  if (typeof message.itemCost === 'number' && message.itemCost >= 0) rules.itemCost = message.itemCost;
  if (message.itemSeconds > 0) rules.itemSeconds = message.itemSeconds;
  if (message.itemCooldown > 0) rules.itemCooldown = message.itemCooldown;
  if (message.boostX > 0) rules.boostX = message.boostX;
  if (message.slowX > 0) rules.slowX = message.slowX;
  if (typeof message.obstacleCost === 'number' && message.obstacleCost >= 0) rules.obstacleCost = message.obstacleCost;
  if (message.obstacleSeconds > 0) rules.obstacleSeconds = message.obstacleSeconds;
  if (message.obstacleCooldown > 0) rules.obstacleCooldown = message.obstacleCooldown;
  if (typeof message.cheerCost === 'number' && message.cheerCost >= 0) rules.cheerCost = message.cheerCost;

  if (state.phase !== previousPhase || state.raceNumber !== previousRace) {
    onPhaseChanged();
  }
  render();
}

function onPhaseChanged() {
  if (state.phase === 'betting') {
    state.cheer = false; // 每一場都要重新加入啦啦隊
    state.awards = [];
    state.bets = [];
    state.delta = 0;
    state.payout = 0;
    state.finishOrder = [];
    state.finishTimes = [];
    state.usage = [];
    dom.betNote.textContent = '';
  }

  if (state.phase === 'racing') {
    state.progress = state.horses.map(() => 0);
    state.effects = state.horses.map(() => 0);
    state.driveLevels = state.horses.map(() => 0);
    for (const kind of KINDS) state.cooldown[kind].until = 0;
    state.pending.kind = '';
    clearItemSelection();
    chooseRaceDefaults();
    buildRaceControls();
    clearTimeout(toastTimer);
    dom.toast.className = 'toast';
    renderItemHint();
  }
}

function onWallet(message) {
  state.joined = true;
  state.balance = message.balance | 0;
  state.bets = Array.isArray(message.bets) ? message.bets : [];
  state.delta = message.delta | 0;
  state.payout = message.payout | 0;
  state.cheer = !!message.cheer;
  if (message.bonus > 0) flashBonus(message.bonus);
  if (message.nick) {
    state.nickname = message.nick;
    localStorage.setItem('nick', state.nickname);
  }

  applyCooldowns(message);

  const reject = message.reject || '';
  if (state.lastAction === 'bet' || state.lastAction === 'cheer') {
    dom.betNote.textContent = reject;
  } else if (state.lastAction === 'item') {
    finishItemRequest(reject);
  }
  state.lastAction = '';

  render();
}

function onRaceSnapshot(message) {
  if (Array.isArray(message.d)) state.driveLevels = message.d;
  if (Array.isArray(message.p)) state.progress = message.p;
  if (Array.isArray(message.fx)) state.effects = message.fx;
  state.upCounts = Array.isArray(message.up) ? message.up : [];
  state.downCounts = Array.isArray(message.down) ? message.down : [];
  state.obstacleCounts = Array.isArray(message.obs) ? message.obs : [];
  if (state.phase === 'racing') renderRace();
}

// ---------------------------------------------------------------- 小工具

function horseById(lane) {
  return state.horses.find((horse) => horse.id === lane);
}

function horseName(lane) {
  const horse = horseById(lane);
  return horse ? horse.name : `第 ${lane + 1} 號`;
}

function horseColor(lane) {
  const horse = horseById(lane);
  return horse && horse.color ? horse.color : '#888';
}

function formatChips(value) {
  return Number(value || 0).toLocaleString('en-US');
}

function stakeOn(lane) {
  let total = 0;
  for (const bet of state.bets) {
    if (bet.lane === lane) total += bet.amount;
  }
  return total;
}

function totalStaked() {
  return state.bets.reduce((sum, bet) => sum + bet.amount, 0);
}

function biggestBetLane() {
  let best = -1;
  let bestAmount = 0;
  for (const bet of state.bets) {
    if (bet.amount > bestAmount) {
      bestAmount = bet.amount;
      best = bet.lane;
    }
  }
  return best;
}

function isValidLane(lane) {
  return lane >= 0 && lane < state.horses.length;
}

function el(tag, className, text) {
  const node = document.createElement(tag);
  if (className) node.className = className;
  if (text !== undefined) node.textContent = text;
  return node;
}

// ---------------------------------------------------------------- 畫面切換

const PHASE_TITLES = {
  lobby: '等待開賽',
  idle: '準備下一場',
  betting: '下注中',
  racing: '比賽中',
  photo: '衝線',
  settle: '結算',
  gameover: '遊戲結束',
};

function showScreen(name) {
  dom.joinScreen.hidden = name !== 'join';
  dom.waitScreen.hidden = name !== 'wait';
  dom.betScreen.hidden = name !== 'bet';
  dom.raceScreen.hidden = name !== 'race';
  dom.resultScreen.hidden = name !== 'result';
  dom.endScreen.hidden = name !== 'end';
}

function render() {
  renderHeader();

  if (!state.joined) {
    showScreen('join');
    return;
  }

  switch (state.phase) {
    case 'betting':
      showScreen('bet');
      renderBetting();
      break;
    case 'racing':
      showScreen('race');
      renderRace();
      break;
    case 'photo':
    case 'settle':
      showScreen('result');
      renderResult();
      break;
    case 'gameover':
      showScreen('end');
      renderEnd();
      break;
    default:
      showScreen('wait');
      renderWait();
      break;
  }
}

function renderHeader() {
  dom.phaseLabel.textContent = state.connected
    ? (PHASE_TITLES[state.phase] || '賽馬場')
    : '重新連線中…';

  if (state.endsAt > 0) {
    const remaining = Math.max(0, Math.ceil((state.endsAt - Date.now()) / 1000));
    dom.countdown.textContent = remaining > 0 ? `0:${String(remaining).padStart(2, '0')}` : '';
  } else {
    dom.countdown.textContent = '';
  }

  dom.balanceBox.hidden = !state.joined;
  dom.balance.textContent = formatChips(state.balance);
}

// ---------------------------------------------------------------- 等待

function renderWait() {
  const lobby = state.phase === 'lobby' || state.phase === '';
  dom.waitWho.textContent = `${state.nickname || '你'}，你已入場`;
  dom.waitBalance.textContent = formatChips(state.balance);
  dom.waitHint.textContent = lobby
    ? '主持人開始後，這裡會自動切到下注畫面。'
    : '下一場即將開始，準備好你的籌碼。';

  dom.waitCrowd.hidden = !lobby || state.players <= 0;
  dom.waitCount.textContent = String(state.players);

  dom.waitList.textContent = '';
  if (lobby || state.leaders.length === 0) {
    dom.waitListTitle.textContent = '今晚出賽';
    state.horses.forEach((horse, index) => {
      const item = el('div', 'item');
      const swatch = el('span', 'swatch');
      swatch.style.background = horse.color || '#888';
      item.append(swatch, el('span', '', horse.name), el('span', 'side', `${index + 1} 閘`));
      dom.waitList.append(item);
    });
  } else {
    dom.waitListTitle.textContent = '籌碼排行';
    state.leaders.forEach((entry, index) => {
      const item = el('div', entry.name === state.nickname ? 'item me' : 'item');
      item.append(el('span', 'n', String(index + 1)), el('span', '', entry.name),
        el('span', 'side', formatChips(entry.balance)));
      dom.waitList.append(item);
    });
  }
}

// ---------------------------------------------------------------- 下注

function selectedAmount() {
  if (state.amountMode === 'all') return state.balance;
  if (state.amountMode === 'custom') return parseInt(dom.customAmount.value, 10) || 0;
  return state.presetAmount;
}

function buildChipTray() {
  if (dom.chipTray.childElementCount > 0) return;

  const options = PRESET_AMOUNTS.map((amount) => ({ mode: 'preset', amount, label: formatChips(amount) }));
  options.push({ mode: 'all', amount: 0, label: '全部' });

  for (const option of options) {
    const button = el('button', option.mode === 'all' ? 'chip-btn all' : 'chip-btn', option.label);
    button.type = 'button';
    button.dataset.mode = option.mode;
    button.dataset.amount = String(option.amount);
    button.addEventListener('click', () => {
      state.amountMode = option.mode;
      if (option.mode === 'preset') state.presetAmount = option.amount;
      dom.customAmount.blur();
      renderBetting();
    });
    dom.chipTray.append(button);
  }
}

function renderChipTray() {
  buildChipTray();
  for (const button of dom.chipTray.children) {
    const mode = button.dataset.mode;
    const amount = mode === 'all' ? state.balance : Number(button.dataset.amount);
    const selected = state.amountMode === mode && (mode === 'all' || state.presetAmount === amount);
    button.setAttribute('aria-pressed', selected ? 'true' : 'false');
    button.disabled = amount <= 0 || amount > state.balance;
  }
  dom.customBox.classList.toggle('active', state.amountMode === 'custom');
}

function renderBetting() {
  renderChipRowAndOdds();
  renderCheerButton();
  renderSlip();
}

function renderCheerButton() {
  const joined = state.cheer;
  dom.cheerButton.classList.toggle('joined', joined);
  dom.cheerButton.disabled = joined || state.balance < state.rules.cheerCost;
  dom.cheerTitle.textContent = joined ? '已加入啦啦隊' : '加入啦啦隊';
  dom.cheerHint.textContent = joined ? '比賽開始後就能搖手機幫馬加油' : '比賽中才能搖手機幫馬加油，每場要重新加入';
  dom.cheerPrice.hidden = joined;
  dom.cheerCost.textContent = formatChips(state.rules.cheerCost);
}

dom.cheerButton.addEventListener('click', () => {
  if (state.cheer || state.phase !== 'betting') return;
  dom.betNote.textContent = '';
  state.lastAction = 'cheer';
  send({ t: 'cheer', pid: state.playerId, nick: state.nickname });
});

function renderChipRowAndOdds() {
  renderChipTray();

  dom.oddsList.textContent = '';
  const amount = selectedAmount();
  for (const horse of state.horses) {
    const button = el('button', 'odds');
    button.type = 'button';
    button.disabled = amount <= 0 || amount > state.balance;

    const stripe = el('span', 'stripe');
    stripe.style.background = horse.color || '#888';

    const name = el('span', 'name', horse.name || `第 ${horse.id + 1} 號`);
    const staked = stakeOn(horse.id);
    if (staked > 0) name.append(el('small', '', `已押 ${formatChips(staked)}`));

    const rate = el('span', 'rate', horse.odds > 0 ? horse.odds.toFixed(2) : '—');
    rate.append(el('small', '', horse.odds > 0 ? '倍' : '計算中'));

    button.append(stripe, name, rate);
    button.addEventListener('click', () => placeBet(horse.id));
    dom.oddsList.append(button);
  }
}

function renderSlip() {
  dom.slipRace.textContent = state.raceNumber > 0 ? `第 ${state.raceNumber} 場` : '';
  dom.slipLines.textContent = '';

  if (state.bets.length === 0) {
    dom.slipLines.append(el('div', 'empty', '還沒下注，先選金額再點馬'));
  }

  for (const bet of state.bets) {
    const horse = horseById(bet.lane);
    const line = el('div', 'line');
    const pip = el('span', 'pip');
    pip.style.background = horseColor(bet.lane);
    const win = horse && horse.odds > 0 ? Math.floor(bet.amount * horse.odds) : 0;
    line.append(pip, el('span', '', horseName(bet.lane)), el('span', 'amt', formatChips(bet.amount)),
      el('span', 'win', win > 0 ? `中可得 ${formatChips(win)}` : ''));
    dom.slipLines.append(line);
  }

  dom.slipTotal.textContent = formatChips(totalStaked());
}

function placeBet(lane) {
  const amount = selectedAmount();
  if (amount <= 0) {
    dom.betNote.textContent = '請先選擇或輸入金額';
    return;
  }
  if (amount > state.balance) {
    dom.betNote.textContent = '籌碼不足';
    return;
  }
  if (amount < state.rules.minBet && amount !== state.balance) {
    dom.betNote.textContent = `最低下注 ${formatChips(state.rules.minBet)}`;
    return;
  }

  dom.betNote.textContent = '';
  state.lastAction = 'bet';
  send({ t: 'bet', pid: state.playerId, nick: state.nickname, lane, amount });
}

dom.customAmount.addEventListener('focus', () => {
  state.amountMode = 'custom';
  renderChipTray();
});

dom.customAmount.addEventListener('input', () => {
  // 只留數字：inputmode=numeric 在部分鍵盤仍可能打出其他字元
  const digits = dom.customAmount.value.replace(/[^0-9]/g, '').slice(0, 9);
  if (digits !== dom.customAmount.value) dom.customAmount.value = digits;
  state.amountMode = 'custom';
  renderChipRowAndOdds();
});

dom.customAmount.addEventListener('keydown', (event) => {
  if (event.key === 'Enter') dom.customAmount.blur();
});

// ---------------------------------------------------------------- 比賽：券與出力

/**
 * 每場開跑時的出力對象：玩家自己選過就保留，沒選過就推自己押最多的那匹。
 * 券則不預選——券和馬都要玩家親手點過才會使用。
 */
function chooseRaceDefaults() {
  if (!state.driveChosen || !isValidLane(state.driveLane)) {
    state.driveLane = biggestBetLane();
  }
}

/** 比賽畫面的按鈕只在換場時建一次，之後只更新狀態，避免 10Hz 的賽況更新一直重建 DOM。 */
function buildRaceControls() {
  dom.fieldList.textContent = '';
  for (const horse of state.horses) {
    const row = el('div', 'row');
    row.dataset.lane = String(horse.id);
    const swatch = el('span', 'swatch');
    swatch.style.background = horse.color || '#888';
    const track = el('span', 'track');
    const bar = el('i');
    bar.style.background = horse.color || '#888';
    track.append(bar);
    row.append(el('span', 'rank', ''), swatch, el('span', 'nm', horse.name), track, el('span', 'fx'));
    dom.fieldList.append(row);
  }

  buildPicks(dom.itemPicks, selectItemLane);

  buildPicks(dom.drivePicks, (lane) => {
    state.driveLane = lane;
    state.driveChosen = true;
    renderRace();
  });

  const rules = state.rules;
  dom.boostEffect.textContent = `×${rules.boostX.toFixed(2)} · ${trimNumber(rules.itemSeconds)}秒`;
  dom.slowEffect.textContent = `×${rules.slowX.toFixed(2)} · ${trimNumber(rules.itemSeconds)}秒`;
  dom.obstacleEffect.textContent = `前方擋路 · 停${trimNumber(rules.obstacleSeconds)}秒`;
  for (const kind of KINDS) {
    tickets[kind].querySelector('.item-cost').textContent = formatChips(costOf(kind));
  }
}

/** 這種券的價格。障礙券另外定價。 */
function costOf(kind) {
  return kind === 'obstacle' ? state.rules.obstacleCost : state.rules.itemCost;
}

/** 這種券買下後的冷卻秒數（券面轉一圈的時間）。 */
function cooldownOf(kind) {
  return kind === 'obstacle' ? state.rules.obstacleCooldown : state.rules.itemCooldown;
}

function trimNumber(value) {
  return Number.isInteger(value) ? String(value) : value.toFixed(1);
}

function buildPicks(container, onPick) {
  container.textContent = '';
  for (const horse of state.horses) {
    const button = el('button', 'pick');
    button.type = 'button';
    button.dataset.lane = String(horse.id);
    const pip = el('span', 'pip');
    pip.style.background = horse.color || '#888';
    button.append(pip, el('span', '', horse.name));
    button.addEventListener('click', () => onPick(horse.id));
    container.append(button);
  }
}

function renderPicks() {
  const mark = (container, lane) => {
    for (const button of container.children) {
      button.setAttribute('aria-pressed', Number(button.dataset.lane) === lane ? 'true' : 'false');
    }
  };
  mark(dom.itemPicks, state.selectedLane);
  mark(dom.drivePicks, state.driveLane);
}

function renderRace() {
  if (dom.fieldList.childElementCount !== state.horses.length) buildRaceControls();

  renderField();
  renderPicks();
  renderTickets();

  const level = isValidLane(state.driveLane) ? (state.driveLevels[state.driveLane] || 0) : 0;
  const percent = Math.round(Math.max(0, Math.min(1, level)) * 100);
  dom.meterFill.style.width = percent + '%';
  dom.drivePct.textContent = percent + '%';

  const canDrive = state.cheer && isValidLane(state.driveLane);
  dom.tapPad.disabled = !canDrive;
  if (!state.cheer) {
    dom.tapHint.textContent = '這場沒加入啦啦隊，下一場下注時可以加入';
  } else {
    dom.tapHint.textContent = isValidLane(state.driveLane)
      ? `替${horseName(state.driveLane)}出力 · 要多人一起搖才會滿`
      : '先在上面選一匹要推的馬';
  }
}

function renderField() {
  const order = state.horses.map((horse) => horse.id)
    .sort((a, b) => (state.progress[b] || 0) - (state.progress[a] || 0));

  for (const row of dom.fieldList.children) {
    const lane = Number(row.dataset.lane);
    const rank = order.indexOf(lane);
    row.style.order = String(rank);

    const rankLabel = row.children[0];
    rankLabel.textContent = String(rank + 1);
    rankLabel.classList.toggle('first', rank === 0);

    row.querySelector('.track i').style.width = Math.round((state.progress[lane] || 0) * 100) + '%';

    // 有幾個效果就畫幾個圖示（幾個人同時加速就有幾個 ▲），太多改成「▲×10」；
    // 被絆住用位元旗標 4（見 Messages.cs 的 EffectFlags）
    const ups = state.upCounts[lane] | 0;
    const downs = state.downCounts[lane] | 0;
    const obstacles = state.obstacleCounts[lane] | 0;
    const stunned = ((state.effects[lane] | 0) & 4) !== 0;
    const fx = row.querySelector('.fx');
    const wanted = `${ups},${downs},${stunned ? 1 : 0},${obstacles}`;
    if (fx.dataset.flags !== wanted) {
      fx.dataset.flags = wanted;
      fx.textContent = '';
      appendBadge(fx, 'up', '▲', ups);
      appendBadge(fx, 'down', '▼', downs);
      appendBadge(fx, 'stun', '暈', stunned ? 1 : 0);
      appendBadge(fx, 'block', '■', obstacles);
    }
  }
}

/** 同一種效果最多逐一畫幾個圖示，再多就改成「▲×10」。與大螢幕的規則相同。 */
const MAX_REPEATED_ICONS = 3;

function appendBadge(container, className, icon, count) {
  if (count <= 0) return;
  const text = count <= MAX_REPEATED_ICONS ? icon.repeat(count) : `${icon}×${count}`;
  container.append(el('i', className, text));
}

// ---- 買券：券和馬都選好才使用 ----

/** 點券：選中或取消。冷卻中或等待回應的券不能選（按鈕本身也是停用的）。 */
function selectItemKind(kind) {
  if (tickets[kind].disabled) return;
  state.selectedKind = state.selectedKind === kind ? '' : kind;
  useSelectedItem();
}

/** 點馬：選中或取消。 */
function selectItemLane(lane) {
  state.selectedLane = state.selectedLane === lane ? -1 : lane;
  useSelectedItem();
}

/** 兩邊都選好就使用，並清掉選擇；只選了一邊就提示下一步。 */
function useSelectedItem() {
  const kind = state.selectedKind;
  const lane = state.selectedLane;

  if (kind && isValidLane(lane)) {
    clearItemSelection();
    buyTicket(kind, lane);
  } else {
    // 玩家剛有新的操作，下一步的提示比上一張券的結果重要，直接蓋掉
    clearTimeout(toastTimer);
    renderItemHint(true);
  }

  renderPicks();
  renderTickets();
}

function clearItemSelection() {
  state.selectedKind = '';
  state.selectedLane = -1;
}

/** 在買券結果的位置提示下一步；結果還在顯示時不蓋掉，除非玩家剛有新操作（force）。 */
function renderItemHint(force) {
  if (!force && dom.toast.classList.contains('show')) return;

  let hint = '點一張券和一匹馬，兩個都選好才會使用';
  if (state.selectedKind) hint = `已選${KIND_NAMES[state.selectedKind]}，再點一匹馬`;
  else if (isValidLane(state.selectedLane)) hint = `已選${horseName(state.selectedLane)}，再點一張券`;

  dom.toast.textContent = hint;
  dom.toast.className = 'toast hint';
}

function buyTicket(kind, lane) {
  if (state.phase !== 'racing' || isCooling(kind) || state.pending.kind) return;

  if (state.balance < costOf(kind)) {
    showToast('籌碼不足', true);
    return;
  }

  const sent = send({ t: 'item', pid: state.playerId, nick: state.nickname, kind, lane });
  if (!sent) {
    showToast('連線中斷，請稍後再試', true);
    return;
  }

  state.lastAction = 'item';
  state.pending = { kind, lane, at: performance.now() };
  startTicketAnimation();
}

function isCooling(kind) {
  return state.cooldown[kind].until > performance.now();
}

/** 以大螢幕回報的剩餘冷卻為準：它是唯一權威，手機只負責把倒數畫出來。 */
function applyCooldowns(message) {
  const now = performance.now();
  for (const kind of KINDS) {
    const seconds = Number(message[`${kind}Cool`]) || 0;
    const cooldown = state.cooldown[kind];
    if (seconds > 0) {
      // 新的一輪冷卻才重設總長，避免同一輪中途收到錢包時圓圈跳回起點
      if (cooldown.until <= now) cooldown.total = Math.max(seconds, cooldownOf(kind)) * 1000;
      cooldown.until = now + seconds * 1000;
    } else {
      cooldown.until = 0;
    }
  }
  if (KINDS.some(isCooling)) startTicketAnimation();
}

function finishItemRequest(reject) {
  const { kind, lane } = state.pending;
  state.pending = { kind: '', lane: -1, at: 0 };
  if (!kind) return;

  if (reject) {
    showToast(reject, true);
  } else {
    showToast(`已對 ${horseName(lane)} 使用${KIND_NAMES[kind]}，−${formatChips(costOf(kind))}`);
  }
}

let toastTimer = 0;
/** 在券下方顯示買券結果。元素永遠佔位、只切換透明度，出現與消失時版面不會跳動。 */
function showToast(text, bad) {
  dom.toast.textContent = text;
  dom.toast.className = bad ? 'toast show bad' : 'toast show';
  clearTimeout(toastTimer);
  toastTimer = setTimeout(() => {
    dom.toast.className = 'toast';
    if (state.phase === 'racing') renderItemHint();
  }, 2200);
}

/**
 * 券面的冷卻動畫：買下的瞬間整張變黑、不能按；之後黑色以 360 度順時針退去，
 * 轉完一圈（＝效果結束）恢復原色就能再買。用 requestAnimationFrame 逐幀畫 conic-gradient，
 * 不依賴 CSS @property，較舊的 iOS Safari 也能正常顯示。
 */
let ticketFrame = 0;
function startTicketAnimation() {
  if (!ticketFrame) ticketFrame = requestAnimationFrame(animateTickets);
}

function animateTickets() {
  ticketFrame = 0;
  const now = performance.now();

  if (state.pending.kind && now - state.pending.at > ITEM_PENDING_TIMEOUT_MS) {
    state.pending = { kind: '', lane: -1, at: 0 };
    state.lastAction = '';
    showToast('大螢幕沒有回應，請再試一次', true);
  }

  const busy = renderTickets();
  if (busy) ticketFrame = requestAnimationFrame(animateTickets);
}

/** 更新兩張券的狀態，回傳是否還有券在冷卻或等待回應（需要繼續動畫）。 */
function renderTickets() {
  const now = performance.now();
  let busy = false;

  for (const kind of KINDS) {
    const ticket = tickets[kind];
    const cover = ticket.querySelector('.cool');
    const text = cover.querySelector('.cool-text');
    const cooldown = state.cooldown[kind];
    const remaining = cooldown.until - now;
    const pending = state.pending.kind === kind;

    if (pending) {
      cover.hidden = false;
      cover.style.background = 'rgba(8, 12, 10, .92)';
      text.textContent = '…';
      busy = true;
    } else if (remaining > 0) {
      const swept = Math.max(0, Math.min(360, (1 - remaining / cooldown.total) * 360));
      cover.hidden = false;
      cover.style.background =
        `conic-gradient(transparent 0deg ${swept}deg, rgba(8, 12, 10, .92) ${swept}deg 360deg)`;
      text.textContent = (remaining / 1000).toFixed(1);
      busy = true;
    } else {
      cover.hidden = true;
    }

    const affordable = state.balance >= costOf(kind);
    ticket.disabled = pending || remaining > 0 || !affordable || state.phase !== 'racing';

    // 選中的券變成不能用（例如籌碼不夠了）就取消選擇，免得之後點馬時送出注定失敗的請求
    if (ticket.disabled && state.selectedKind === kind) state.selectedKind = '';
    ticket.setAttribute('aria-pressed', state.selectedKind === kind ? 'true' : 'false');
  }

  return busy;
}

for (const kind of KINDS) {
  tickets[kind].addEventListener('click', () => selectItemKind(kind));
}

// ---------------------------------------------------------------- 結果

function renderResult() {
  const settled = state.phase === 'settle';
  dom.resultLabel.textContent = settled ? '本場' : '衝線';

  if (!settled) {
    dom.resultDelta.textContent = '名次揭曉中…';
    dom.resultDelta.className = 'delta flat';
    dom.resultLine.textContent = '';
  } else if (totalStaked() === 0 && state.payout === 0 && state.delta === 0) {
    dom.resultDelta.textContent = '這場沒有下注';
    dom.resultDelta.className = 'delta flat';
    dom.resultLine.textContent = '下一場記得下注！';
  } else if (state.delta > 0) {
    dom.resultDelta.textContent = `+${formatChips(state.delta)}`;
    dom.resultDelta.className = 'delta win';
    dom.resultLine.textContent = winnerLine(true);
  } else {
    dom.resultDelta.textContent = state.delta < 0 ? `−${formatChips(-state.delta)}` : '±0';
    dom.resultDelta.className = 'delta lose';
    dom.resultLine.textContent = winnerLine(false);
  }

  dom.resultOrder.textContent = '';
  state.finishOrder.forEach((lane, index) => {
    const place = el('div', index === 0 ? 'place first' : 'place');
    const main = el('div', 'place-main');
    const swatch = el('span', 'swatch');
    swatch.style.background = horseColor(lane);
    const staked = stakeOn(lane);
    const seconds = state.finishTimes[index];
    main.append(positionBadge(index), swatch, el('span', '', horseName(lane)),
      el('span', 'mine', staked > 0 ? `你押 ${formatChips(staked)}` : ''),
      el('span', 'time', seconds > 0 ? `${seconds.toFixed(2)} 秒` : ''));
    place.append(main);

    const usage = renderUsage(lane);
    if (usage) place.append(usage);
    dom.resultOrder.append(place);
  });

  renderAwards();

  dom.leaderList.textContent = '';
  if (state.leaders.length === 0) {
    dom.leaderList.append(el('div', 'empty', '還沒有紀錄'));
  }
  state.leaders.forEach((entry, index) => {
    const you = entry.name === state.nickname;
    const row = el('div', you ? 'entry you' : 'entry');
    const badge = positionBadge(index);
    badge.classList.add('n');
    row.append(badge, el('span', '', you ? `${entry.name}（你）` : entry.name),
      el('span', 'v', formatChips(entry.balance)));
    dom.leaderList.append(row);
  });
}

/** 本場獎項：券券富翁、最強啦啦隊……文字由大螢幕產生，這裡直接顯示。 */
function renderAwards() {
  dom.awardList.textContent = '';
  dom.awardsTitle.hidden = state.awards.length === 0;
  // 四個格子一列，放進同一個四欄 grid：圖示、獎項名、玩家名、說明各自對齊
  for (const award of state.awards) {
    dom.awardList.append(awardIcon(award.kind), el('span', 'a-title', award.title),
      el('span', 'a-name', award.name), el('span', 'a-detail', award.detail));
  }
}

/** 獎項圖示檔名，索引即 Messages.cs 的 AwardEntry.kind（Core 的 AwardKind 數值）。 */
const AWARD_ICONS = ['ticket_tycoon', 'top_cheerleader', 'roadblocker', 'big_winner'];
const MEDAL_COUNT = 3;

/** 圖片載入失敗時換成文字，網路差也不會出現破圖。 */
function imageOr(className, src, fallbackText) {
  const image = el('img', className);
  image.alt = fallbackText;
  image.src = src;
  image.addEventListener('error', () => image.replaceWith(el('span', className + ' fallback', fallbackText)));
  return image;
}

function awardIcon(kind) {
  const name = AWARD_ICONS[kind];
  return name ? imageOr('a-icon', `img/award_${name}.png`, '★') : el('span', 'a-icon', '');
}

/** 名次：前三名用獎牌圖，其餘顯示數字。 */
function positionBadge(index) {
  return index < MEDAL_COUNT
    ? imageOr('pos medal', `img/medal_${index + 1}.png`, String(index + 1))
    : el('span', 'pos', String(index + 1));
}

/** 遊戲結束：自己的最終籌碼、名次（在前幾名時）與最終排名。 */
function renderEnd() {
  dom.endChips.textContent = `${formatChips(state.balance)} 籌碼`;
  const rank = state.finalTop.findIndex((entry) => entry.name === state.nickname);
  dom.endLine.textContent = rank >= 0 ? `你是第 ${rank + 1} 名！感謝參與` : '感謝參與！';

  dom.endBoard.textContent = '';
  state.finalTop.forEach((entry, index) => {
    const you = entry.name === state.nickname;
    const row = el('div', you ? 'entry you' : 'entry');
    const badge = positionBadge(index);
    badge.classList.add('n');
    row.append(badge, el('span', '', you ? `${entry.name}（你）` : entry.name),
      el('span', 'v', formatChips(entry.balance)));
    dom.endBoard.append(row);
  });
}

let bonusTimer = 0;
/** 發零用金時在餘額下方跳出「+50 零用金」。 */
function flashBonus(amount) {
  dom.bonusFlash.textContent = `+${formatChips(amount)} 零用金`;
  dom.bonusFlash.hidden = false;
  clearTimeout(bonusTimer);
  bonusTimer = setTimeout(() => { dom.bonusFlash.hidden = true; }, 2500);
}

/** 某匹馬本場被誰用了什麼券：「加速 阿明×2、小美×1　減速 老王×2」。沒人用過就不顯示。 */
function renderUsage(lane) {
  const lines = state.usage.filter((entry) => entry.lane === lane);
  if (lines.length === 0) return null;

  const box = el('div', 'place-usage');
  for (const kind of KINDS) {
    const ofKind = lines.filter((entry) => entry.kind === kind);
    if (ofKind.length === 0) continue;
    if (box.childElementCount > 0) box.append('　');
    box.append(el('b', kind, KIND_SHORT[kind]));
    box.append(' ' + ofKind.map((entry) => `${entry.name}×${entry.count}`).join('、'));
  }
  return box;
}

function winnerLine(won) {
  const winner = state.finishOrder.length > 0 ? horseName(state.finishOrder[0]) : '';
  if (!winner) return '';
  return won ? `${winner}跑第一，你押中了` : `${winner}跑第一，下一場再接再厲`;
}

// ---------------------------------------------------------------- 進場

/** 暱稱最多幾個字，與 BettingBook.MaxNicknameLength 相同（emoji 算一個字）。 */
const MAX_NICKNAME_CHARS = 10;

function clampNickname(text) {
  return Array.from(text).slice(0, MAX_NICKNAME_CHARS).join('');
}

/** 輸入中文時選字還沒完成不能截，否則注音會被切斷；選完字（compositionend）再截。 */
function enforceNicknameLimit(event) {
  if (event && event.isComposing) return;
  const clamped = clampNickname(dom.nickInput.value);
  if (clamped !== dom.nickInput.value) dom.nickInput.value = clamped;
}

state.nickname = clampNickname(state.nickname); // 舊版存下的暱稱可能超過上限
dom.nickInput.value = state.nickname;
dom.nickInput.addEventListener('input', enforceNicknameLimit);
dom.nickInput.addEventListener('compositionend', () => enforceNicknameLimit(null));

function submitJoin() {
  const nickname = clampNickname(dom.nickInput.value.trim()).trim();
  if (nickname.length === 0) {
    dom.nickInput.focus();
    return;
  }

  state.nickname = nickname;
  localStorage.setItem('nick', nickname);
  sendJoin();
}

dom.joinButton.addEventListener('click', submitJoin);
dom.nickInput.addEventListener('keydown', (event) => {
  if (event.key === 'Enter') submitJoin();
});

// ---------------------------------------------------------------- 出力（連打／搖動）

function addStep() {
  // 沒加入啦啦隊的人不送步數（大螢幕那邊也會擋，這裡先省掉無用的封包）
  if (state.phase !== 'racing' || !state.cheer || !isValidLane(state.driveLane)) return;
  state.pendingSteps += 1;
}

// 連打。用 pointerdown 而不是 click，反應快一拍，也不會被點擊延遲拖到。
dom.tapPad.addEventListener('pointerdown', (event) => {
  event.preventDefault();
  addStep();
});

// 搖動偵測：追蹤加速度大小的平滑基準線，超過門檻算一步。
// 用遲滯 + 不應期，避免一次晃動被算成好幾步。
let baselineMagnitude = 9.8;
let lastStepAt = 0;
let armed = true;

function onDeviceMotion(event) {
  const acceleration = event.accelerationIncludingGravity || event.acceleration;
  if (!acceleration) return;

  const magnitude = Math.hypot(acceleration.x || 0, acceleration.y || 0, acceleration.z || 0);
  baselineMagnitude += (magnitude - baselineMagnitude) * 0.1;
  const deviation = magnitude - baselineMagnitude;
  const now = performance.now();

  if (armed && deviation > SHAKE_THRESHOLD && now - lastStepAt > SHAKE_REFRACTORY_MS) {
    addStep();
    lastStepAt = now;
    armed = false;
  }

  // 回到基準線附近才重新上膛
  if (deviation < SHAKE_THRESHOLD * 0.4) armed = true;
}

function enableMotion() {
  window.addEventListener('devicemotion', onDeviceMotion);
  dom.motionButton.hidden = true;
  dom.motionNote.textContent = '';
}

function setupMotion() {
  if (typeof DeviceMotionEvent === 'undefined') {
    dom.motionNote.textContent = '這台裝置沒有動作感測器，請用連打。';
    return;
  }

  // iOS 13 以後必須由使用者手勢觸發授權，而且頁面一定要是 HTTPS
  const needsPermission = typeof DeviceMotionEvent.requestPermission === 'function';
  if (!needsPermission) {
    enableMotion();
    return;
  }

  if (location.protocol !== 'https:') {
    dom.motionNote.textContent = '目前不是 HTTPS 連線，iOS 不會開放動作感測器，請用連打。';
    return;
  }

  dom.motionButton.hidden = false;
  dom.motionButton.addEventListener('click', async () => {
    try {
      const result = await DeviceMotionEvent.requestPermission();
      if (result === 'granted') {
        enableMotion();
      } else {
        dom.motionNote.textContent = '你拒絕了動作感測器權限，請改用連打。';
        dom.motionButton.hidden = true;
      }
    } catch (error) {
      dom.motionNote.textContent = '無法取得動作感測器權限，請改用連打。';
      dom.motionButton.hidden = true;
    }
  });
}

// ---------------------------------------------------------------- 主迴圈

setInterval(() => {
  if (state.pendingSteps > 0 && isValidLane(state.driveLane) && state.phase === 'racing') {
    send({ t: 'step', pid: state.playerId, lane: state.driveLane, n: state.pendingSteps });
    state.pendingSteps = 0;
  }
}, SEND_INTERVAL_MS);

setInterval(renderHeader, 250);

// 息屏回來後立刻重連並索取狀態，不要等退避計時器
document.addEventListener('visibilitychange', () => {
  if (document.visibilityState === 'visible') {
    state.reconnectDelay = 500;
    connect();
  }
});

setupMotion();
render();
connect();

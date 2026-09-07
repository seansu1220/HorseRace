'use strict';

/**
 * 手機端。依大螢幕推來的階段切換畫面：進場 → 下注 → 出力 → 結果。
 *
 * 欄位名必須與 Assets/Scripts/Core/Protocol/Messages.cs 完全一致，
 * 協定變更時兩邊要同一個 commit 一起改。
 */

// ---------------------------------------------------------------- 設定

/** 每隔多久把累積的步數送出一次。太密會塞爆連線，太疏會讓力度條一頓一頓。 */
const SEND_INTERVAL_MS = 200;

/** 搖動偵測：超過基準線多少 m/s² 算一步。 */
const SHAKE_THRESHOLD = 3.2;

/** 兩步之間的最短間隔，用來濾掉單次晃動造成的連續觸發。 */
const SHAKE_REFRACTORY_MS = 110;

/** 重連退避上限。 */
const RECONNECT_MAX_MS = 5000;

/** 可選的下注金額。「全部」另外處理。 */
const CHIP_AMOUNTS = [50, 100, 200, 500];

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
  horses: [],

  balance: 0,
  bets: [],
  delta: 0,
  payout: 0,

  chip: CHIP_AMOUNTS[1],
  driveLane: -1,
  driveLevel: 0,

  pendingSteps: 0,
  totalSteps: 0,
  recentSteps: [],

  finishOrder: [],
  leaders: [],
};

localStorage.setItem('pid', state.playerId);

function createId() {
  return 'p' + Math.random().toString(36).slice(2, 10);
}

const dom = {};
for (const id of [
  'phaseLabel', 'countdown', 'balance', 'connection',
  'joinScreen', 'nickInput', 'joinButton',
  'betScreen', 'chipRow', 'betHorses', 'betNote', 'myBets',
  'driveScreen', 'mineChip', 'mineName', 'changeHorse',
  'meterFill', 'meterText', 'stepCount', 'stepRate',
  'tapPad', 'motionButton', 'motionNote',
  'pickScreen', 'pickHorses',
  'resultScreen', 'resultTitle', 'resultDelta', 'resultOrder', 'leaderList',
  'idleScreen', 'idleLeader',
]) {
  dom[id] = document.getElementById(id);
}

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
  if (!state.socket || state.socket.readyState !== WebSocket.OPEN) return;
  try {
    state.socket.send(JSON.stringify(payload));
  } catch (error) {
    /* 送不出去就算了，下一次操作或重連會補上 */
  }
}

function sendJoin() {
  send({ t: 'join', pid: state.playerId, nick: state.nickname });
}

// ---------------------------------------------------------------- 收訊息

function handleMessage(message) {
  switch (message.t) {
    case 'phase': {
      const previousPhase = state.phase;
      state.phase = message.phase || '';
      state.endsAt = message.endsAt || 0;
      state.raceNumber = message.race || 0;
      state.horses = Array.isArray(message.horses) ? message.horses : [];

      if (state.phase !== previousPhase) onPhaseChanged();
      render();
      break;
    }

    case 'wallet':
      state.joined = true;
      state.balance = message.balance | 0;
      state.bets = Array.isArray(message.bets) ? message.bets : [];
      state.delta = message.delta | 0;
      state.payout = message.payout | 0;
      if (message.nick) {
        state.nickname = message.nick;
        localStorage.setItem('nick', state.nickname);
      }
      dom.betNote.textContent = message.reject || '';
      render();
      break;

    case 'drive':
      if (Array.isArray(message.d) && state.driveLane >= 0
          && state.driveLane < message.d.length) {
        state.driveLevel = message.d[state.driveLane];
      }
      break;

    case 'result':
      state.finishOrder = Array.isArray(message.order) ? message.order : [];
      state.leaders = Array.isArray(message.top) ? message.top : [];
      render();
      break;
  }
}

function onPhaseChanged() {
  if (state.phase === 'betting') {
    state.finishOrder = [];
    dom.betNote.textContent = '';
  }

  if (state.phase === 'racing') {
    // 預設推自己押最多的那匹，沒下注就讓他自己挑
    state.driveLane = biggestBetLane();
    state.driveLevel = 0;
    state.totalSteps = 0;
    state.recentSteps = [];
  }
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

// ---------------------------------------------------------------- 畫面

const PHASE_TITLES = {
  idle: '準備下一場',
  betting: '下注中',
  racing: '比賽進行中',
  photo: '衝線',
  settle: '結算',
};

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

function showScreen(name) {
  dom.joinScreen.hidden = name !== 'join';
  dom.betScreen.hidden = name !== 'bet';
  dom.driveScreen.hidden = name !== 'drive';
  dom.pickScreen.hidden = name !== 'pick';
  dom.resultScreen.hidden = name !== 'result';
  dom.idleScreen.hidden = name !== 'idle';
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
      if (state.driveLane < 0) {
        showScreen('pick');
        renderPick();
      } else {
        showScreen('drive');
        renderDrive();
      }
      break;

    case 'photo':
    case 'settle':
      showScreen('result');
      renderResult();
      break;

    default:
      showScreen('idle');
      renderLeaderList(dom.idleLeader);
      break;
  }
}

function renderHeader() {
  dom.phaseLabel.textContent = state.connected
    ? (PHASE_TITLES[state.phase] || '等待大螢幕…')
    : '重新連線中…';

  if (state.endsAt > 0) {
    const remaining = Math.max(0, Math.ceil((state.endsAt - Date.now()) / 1000));
    dom.countdown.textContent = remaining > 0 ? String(remaining) : '';
  } else {
    dom.countdown.textContent = '';
  }

  dom.balance.textContent = state.joined ? `${state.balance} 籌碼` : '';
}

function renderChipRow() {
  if (dom.chipRow.dataset.built === '1') {
    for (const button of dom.chipRow.children) {
      const amount = button.dataset.amount === 'all' ? allInAmount() : Number(button.dataset.amount);
      button.classList.toggle('selected', amount === state.chip && amount > 0);
      button.disabled = amount <= 0 || amount > state.balance;
    }
    return;
  }

  dom.chipRow.textContent = '';
  const options = CHIP_AMOUNTS.map((amount) => ({ label: String(amount), amount }));
  options.push({ label: '全部', amount: 'all' });

  for (const option of options) {
    const button = document.createElement('button');
    button.type = 'button';
    button.className = 'chip-button';
    button.textContent = option.label;
    button.dataset.amount = String(option.amount);
    button.addEventListener('click', () => {
      state.chip = option.amount === 'all' ? allInAmount() : option.amount;
      renderChipRow();
    });
    dom.chipRow.append(button);
  }

  dom.chipRow.dataset.built = '1';
  renderChipRow();
}

function allInAmount() {
  return state.balance;
}

function renderBetting() {
  renderChipRow();

  dom.betHorses.textContent = '';
  for (const horse of state.horses) {
    const staked = stakeOn(horse.id);

    const button = document.createElement('button');
    button.type = 'button';
    button.className = 'horse-button';
    button.disabled = state.chip <= 0 || state.chip > state.balance;

    const chip = document.createElement('span');
    chip.className = 'chip';
    chip.style.background = horse.color || '#888';

    const name = document.createElement('span');
    name.textContent = horse.name || `第 ${horse.id + 1} 號`;

    const stakeLabel = document.createElement('span');
    stakeLabel.className = 'horse-stake';
    stakeLabel.textContent = staked > 0 ? `已押 ${staked}` : '';

    const odds = document.createElement('span');
    odds.className = 'horse-odds';
    odds.textContent = horse.odds > 0 ? `${horse.odds.toFixed(2)} 倍` : '計算中';

    button.append(chip, name, stakeLabel, odds);
    button.addEventListener('click', () => placeBet(horse.id));
    dom.betHorses.append(button);
  }

  dom.myBets.textContent = '';
  if (state.bets.length === 0) {
    const line = document.createElement('div');
    line.textContent = '尚未下注';
    dom.myBets.append(line);
  } else {
    for (const bet of state.bets) {
      const line = document.createElement('div');
      line.textContent = `${horseName(bet.lane)}　${bet.amount} 籌碼`;
      dom.myBets.append(line);
    }
  }
}

function stakeOn(lane) {
  let total = 0;
  for (const bet of state.bets) {
    if (bet.lane === lane) total += bet.amount;
  }
  return total;
}

function placeBet(lane) {
  const amount = state.chip;
  if (amount <= 0 || amount > state.balance) return;

  dom.betNote.textContent = '';
  send({ t: 'bet', pid: state.playerId, nick: state.nickname, lane, amount });
}

function renderPick() {
  dom.pickHorses.textContent = '';
  for (const horse of state.horses) {
    const button = document.createElement('button');
    button.type = 'button';
    button.className = 'horse-button';

    const chip = document.createElement('span');
    chip.className = 'chip';
    chip.style.background = horse.color || '#888';

    const name = document.createElement('span');
    name.textContent = horse.name || `第 ${horse.id + 1} 號`;

    button.append(chip, name);
    button.addEventListener('click', () => {
      state.driveLane = horse.id;
      render();
    });
    dom.pickHorses.append(button);
  }
}

function renderDrive() {
  dom.mineChip.style.background = horseColor(state.driveLane);
  dom.mineName.textContent = horseName(state.driveLane);

  const percent = Math.round(Math.max(0, Math.min(1, state.driveLevel)) * 100);
  dom.meterFill.style.width = percent + '%';
  dom.meterText.textContent = percent + '%';
  dom.stepCount.textContent = String(state.totalSteps);

  const now = performance.now();
  state.recentSteps = state.recentSteps.filter((time) => now - time < 1000);
  dom.stepRate.textContent = state.recentSteps.length.toFixed(1);
}

function renderResult() {
  const title = state.phase === 'photo' ? '衝線！' : '結算';
  dom.resultTitle.textContent = title;

  if (state.phase === 'settle') {
    if (state.delta > 0) {
      dom.resultDelta.textContent = `+${state.delta}`;
      dom.resultDelta.className = 'delta win';
    } else if (state.delta < 0) {
      dom.resultDelta.textContent = String(state.delta);
      dom.resultDelta.className = 'delta lose';
    } else {
      dom.resultDelta.textContent = '沒有下注';
      dom.resultDelta.className = 'delta flat';
    }
  } else {
    dom.resultDelta.textContent = '';
    dom.resultDelta.className = 'delta flat';
  }

  dom.resultOrder.textContent = '';
  state.finishOrder.forEach((lane, index) => {
    const item = document.createElement('li');
    if (index === 0) item.className = 'first';

    const rank = document.createElement('span');
    rank.className = 'rank';
    rank.textContent = String(index + 1);

    const chip = document.createElement('span');
    chip.className = 'chip';
    chip.style.background = horseColor(lane);

    const name = document.createElement('span');
    name.textContent = horseName(lane);

    const staked = stakeOn(lane);
    const value = document.createElement('span');
    value.className = 'value';
    value.textContent = staked > 0 ? `你押 ${staked}` : '';

    item.append(rank, chip, name, value);
    dom.resultOrder.append(item);
  });

  renderLeaderList(dom.leaderList);
}

function renderLeaderList(container) {
  container.textContent = '';
  if (state.leaders.length === 0) {
    const item = document.createElement('li');
    item.textContent = '還沒有紀錄';
    container.append(item);
    return;
  }

  state.leaders.forEach((entry, index) => {
    const item = document.createElement('li');
    if (index === 0) item.className = 'first';

    const rank = document.createElement('span');
    rank.className = 'rank';
    rank.textContent = String(index + 1);

    const name = document.createElement('span');
    name.textContent = entry.name;

    const value = document.createElement('span');
    value.className = 'value';
    value.textContent = String(entry.balance);

    item.append(rank, name, value);
    container.append(item);
  });
}

// ---------------------------------------------------------------- 進場

dom.nickInput.value = state.nickname;

function submitJoin() {
  const nickname = dom.nickInput.value.trim();
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

dom.changeHorse.addEventListener('click', () => {
  state.driveLane = -1;
  state.driveLevel = 0;
  render();
});

// ---------------------------------------------------------------- 步數輸入

function addStep() {
  if (state.phase !== 'racing' || state.driveLane < 0) return;
  state.pendingSteps += 1;
  state.totalSteps += 1;
  state.recentSteps.push(performance.now());
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

  const magnitude = Math.hypot(
    acceleration.x || 0, acceleration.y || 0, acceleration.z || 0);

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
  dom.motionNote.textContent = '動作感測器已開啟，用力搖手機就會加速。';
}

function setupMotion() {
  if (typeof DeviceMotionEvent === 'undefined') {
    dom.motionNote.textContent = '這台裝置沒有動作感測器，請用連打按鈕。';
    return;
  }

  // iOS 13 以後必須由使用者手勢觸發授權，而且頁面一定要是 HTTPS
  const needsPermission = typeof DeviceMotionEvent.requestPermission === 'function';
  if (!needsPermission) {
    enableMotion();
    return;
  }

  if (location.protocol !== 'https:') {
    dom.motionNote.textContent =
      '目前不是 HTTPS 連線，iOS 不會開放動作感測器。請用連打按鈕。';
    return;
  }

  dom.motionButton.hidden = false;
  dom.motionButton.addEventListener('click', async () => {
    try {
      const result = await DeviceMotionEvent.requestPermission();
      if (result === 'granted') {
        enableMotion();
      } else {
        dom.motionNote.textContent = '你拒絕了動作感測器權限，請改用連打按鈕。';
        dom.motionButton.hidden = true;
      }
    } catch (error) {
      dom.motionNote.textContent = '無法取得動作感測器權限，請改用連打按鈕。';
      dom.motionButton.hidden = true;
    }
  });
}

// ---------------------------------------------------------------- 主迴圈

setInterval(() => {
  if (state.pendingSteps > 0 && state.driveLane >= 0 && state.phase === 'racing') {
    send({ t: 'step', lane: state.driveLane, n: state.pendingSteps });
    state.pendingSteps = 0;
  }
}, SEND_INTERVAL_MS);

setInterval(() => {
  renderHeader();
  if (state.phase === 'racing' && state.driveLane >= 0) renderDrive();
}, 100);

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

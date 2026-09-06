'use strict';

/**
 * 手機端：把「搖手機」或「連打按鈕」轉成步數，回報給大螢幕。
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

// ---------------------------------------------------------------- 狀態

const state = {
  socket: null,
  connected: false,
  reconnectDelay: 500,
  playerId: localStorage.getItem('pid') || createId(),
  lane: readStoredLane(),
  horses: [],
  phase: '',
  endsAt: 0,
  pendingSteps: 0,
  totalSteps: 0,
  recentSteps: [],
  driveLevel: 0,
};

localStorage.setItem('pid', state.playerId);

const dom = {
  phaseLabel: document.getElementById('phaseLabel'),
  countdown: document.getElementById('countdown'),
  connection: document.getElementById('connection'),
  pickPanel: document.getElementById('pickPanel'),
  drivePanel: document.getElementById('drivePanel'),
  horseList: document.getElementById('horseList'),
  mineChip: document.getElementById('mineChip'),
  mineName: document.getElementById('mineName'),
  changeHorse: document.getElementById('changeHorse'),
  meterFill: document.getElementById('meterFill'),
  meterText: document.getElementById('meterText'),
  stepCount: document.getElementById('stepCount'),
  stepRate: document.getElementById('stepRate'),
  tapPad: document.getElementById('tapPad'),
  motionButton: document.getElementById('motionButton'),
  motionNote: document.getElementById('motionNote'),
};

function createId() {
  return 'p' + Math.random().toString(36).slice(2, 10);
}

function readStoredLane() {
  const raw = localStorage.getItem('lane');
  if (raw === null) return -1;
  const parsed = parseInt(raw, 10);
  return Number.isInteger(parsed) ? parsed : -1;
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
    if (state.lane >= 0) sendJoin();
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
    /* 送不出去就算了，下一次心跳會補上 */
  }
}

function sendJoin() {
  send({ t: 'join', lane: state.lane, pid: state.playerId, nick: '' });
}

// ---------------------------------------------------------------- 收訊息

function handleMessage(message) {
  if (message.t === 'phase') {
    state.phase = message.phase || '';
    state.endsAt = message.endsAt || 0;
    state.horses = Array.isArray(message.horses) ? message.horses : [];
    renderHorseList();
    renderMine();
    return;
  }

  if (message.t === 'drive' && Array.isArray(message.d)) {
    if (state.lane >= 0 && state.lane < message.d.length) {
      state.driveLevel = message.d[state.lane];
    }
  }
}

// ---------------------------------------------------------------- 畫面

const PHASE_TITLES = {
  idle: '準備下一場',
  betting: '下注中',
  racing: '比賽進行中',
  photo: '衝線',
  settle: '結算',
};

function renderPhase() {
  dom.phaseLabel.textContent = state.connected
    ? (PHASE_TITLES[state.phase] || '等待大螢幕…')
    : '重新連線中…';

  if (state.endsAt > 0) {
    const remaining = Math.max(0, Math.ceil((state.endsAt - Date.now()) / 1000));
    dom.countdown.textContent = remaining > 0 ? String(remaining) : '';
  } else {
    dom.countdown.textContent = '';
  }
}

function renderHorseList() {
  if (state.lane >= 0) return;

  dom.horseList.textContent = '';
  for (const horse of state.horses) {
    const button = document.createElement('button');
    button.className = 'horse-button';
    button.type = 'button';

    const chip = document.createElement('span');
    chip.className = 'chip';
    chip.style.background = horse.color || '#888';

    const name = document.createElement('span');
    name.textContent = horse.name || `第 ${horse.id + 1} 號`;

    const odds = document.createElement('span');
    odds.className = 'horse-odds';
    odds.textContent = horse.odds ? `${horse.odds.toFixed(2)} 倍` : '';

    button.append(chip, name, odds);
    button.addEventListener('click', () => selectLane(horse.id));
    dom.horseList.append(button);
  }
}

function renderMine() {
  const picked = state.lane >= 0;
  dom.pickPanel.hidden = picked;
  dom.drivePanel.hidden = !picked;
  if (!picked) return;

  const horse = state.horses.find((entry) => entry.id === state.lane);
  dom.mineChip.style.background = horse ? (horse.color || '#888') : '#888';
  dom.mineName.textContent = horse ? horse.name : `第 ${state.lane + 1} 號`;
}

function renderMeter() {
  const percent = Math.round(Math.max(0, Math.min(1, state.driveLevel)) * 100);
  dom.meterFill.style.width = percent + '%';
  dom.meterText.textContent = percent + '%';
  dom.stepCount.textContent = String(state.totalSteps);

  const now = performance.now();
  state.recentSteps = state.recentSteps.filter((time) => now - time < 1000);
  dom.stepRate.textContent = state.recentSteps.length.toFixed(1);
}

function selectLane(lane) {
  state.lane = lane;
  localStorage.setItem('lane', String(lane));
  renderMine();
  sendJoin();
}

dom.changeHorse.addEventListener('click', () => {
  state.lane = -1;
  localStorage.removeItem('lane');
  state.driveLevel = 0;
  renderHorseList();
  renderMine();
});

// ---------------------------------------------------------------- 步數輸入

function addStep() {
  state.pendingSteps += 1;
  state.totalSteps += 1;
  state.recentSteps.push(performance.now());
}

// 連打。用 pointerdown 而不是 click，反應快一拍，也不會被 300ms 點擊延遲拖到。
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
    dom.motionNote.textContent =
      '這台裝置沒有動作感測器，請用連打按鈕。';
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
      '目前不是 HTTPS 連線，iOS 不會開放動作感測器。請改用連打按鈕，'
      + '或用 cloudflared 取得 https 網址。';
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
  if (state.pendingSteps > 0 && state.lane >= 0) {
    send({ t: 'step', lane: state.lane, n: state.pendingSteps });
    state.pendingSteps = 0;
  }
}, SEND_INTERVAL_MS);

setInterval(() => {
  renderPhase();
  renderMeter();
}, 100);

// 息屏回來後立刻重連並索取狀態，不要等退避計時器
document.addEventListener('visibilitychange', () => {
  if (document.visibilityState === 'visible') {
    state.reconnectDelay = 500;
    connect();
  }
});

setupMotion();
renderMine();
connect();

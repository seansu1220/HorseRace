'use strict';

/**
 * 賽馬遊戲的中繼伺服器。
 *
 * 這裡刻意「不懂遊戲規則」——不知道什麼是賠率、名次、籌碼。
 * 它只做四件事：託管手機網頁、認得誰是大螢幕誰是手機、把訊息轉給對方、
 * 快取最後一則階段訊息供新加入者立刻補上畫面。
 *
 * 所有遊戲邏輯都留在 Unity 端的純 C# Core，這條界線讓伺服器變成可拋棄品：
 * 換部署商、重啟、甚至整台換掉，都不影響玩法。
 *
 * 目前是原型階段，只支援單一房間。房號路由留到 M3 正式版再加。
 */

const crypto = require('crypto');
const http = require('http');
const fs = require('fs');
const path = require('path');
const { WebSocketServer } = require('ws');

const PORT = process.env.PORT || 8080;
const PUBLIC_DIR = path.join(__dirname, '..', 'public');

/**
 * 大螢幕金鑰。有設定時，role=host 的連線必須帶上相同的 key 才會被接受。
 * 開了對外通道或部署到雲端之後，任何人都連得到這台伺服器；
 * 沒有這道檢查，有人把自己標成 host 就能冒充大螢幕對全場手機亂發訊息。
 * 由 Unity 自動啟動時會透過環境變數帶入；手動 npm start 不設定則不檢查（開發用）。
 */
const HOST_KEY = process.env.HOST_KEY || '';

/** 關閉代碼，與 Assets/Scripts/Net/RelayClient.cs 的定義一致。 */
const CLOSE_REPLACED_BY_NEW_HOST = 4000;
const CLOSE_INVALID_HOST_KEY = 4001;

const MIME_TYPES = {
  '.html': 'text/html; charset=utf-8',
  '.js': 'text/javascript; charset=utf-8',
  '.css': 'text/css; charset=utf-8',
  '.json': 'application/json; charset=utf-8',
  '.png': 'image/png',
  '.svg': 'image/svg+xml',
  '.ico': 'image/x-icon',
};

// ---------------------------------------------------------------- 靜態檔案

function serveStatic(req, res) {
  const requested = req.url.split('?')[0];
  const relative = requested === '/' ? 'index.html' : requested.replace(/^\/+/, '');

  // 防目錄穿越：解析後的路徑必須仍在 public 底下
  const resolved = path.resolve(PUBLIC_DIR, relative);
  if (!resolved.startsWith(PUBLIC_DIR)) {
    res.writeHead(403).end('Forbidden');
    return;
  }

  fs.readFile(resolved, (error, content) => {
    if (error) {
      res.writeHead(404, { 'Content-Type': 'text/plain; charset=utf-8' });
      res.end('找不到 ' + relative);
      return;
    }

    res.writeHead(200, {
      'Content-Type': MIME_TYPES[path.extname(resolved)] || 'application/octet-stream',
      'Cache-Control': 'no-cache',
    });
    res.end(content);
  });
}

const server = http.createServer(serveStatic);

// ---------------------------------------------------------------- 連線管理

const wss = new WebSocketServer({ server, path: '/ws' });

/** 大螢幕連線。同時只允許一個，後來的會踢掉前一個（通常是重連）。 */
let host = null;

/** 手機連線集合。 */
const players = new Set();

/** 最後一則階段訊息，供新加入的手機立刻補上畫面。 */
let lastPhaseMessage = null;

function send(socket, payload) {
  if (!socket || socket.readyState !== socket.OPEN) return;
  try {
    socket.send(payload);
  } catch (error) {
    console.warn('[relay] 送出失敗：', error.message);
  }
}

function broadcastToPlayers(payload) {
  for (const player of players) {
    send(player, payload);
  }
}

/**
 * 定向轉發：大螢幕送出的訊息若帶 `to` 欄位，就只送給該玩家。
 * 個人錢包屬於這一類——沒必要把每個人的籌碼廣播給全場。
 * 找不到對應連線時安靜丟棄（玩家可能剛斷線），不視為錯誤。
 */
function sendToPlayer(playerId, payload) {
  for (const player of players) {
    if (player.playerId === playerId) {
      send(player, payload);
      return;
    }
  }
}

function isAuthorizedHost(url) {
  if (!HOST_KEY) return true;
  const given = Buffer.from(url.searchParams.get('key') || '');
  const expected = Buffer.from(HOST_KEY);
  return given.length === expected.length && crypto.timingSafeEqual(given, expected);
}

function describe(socket) {
  return socket === host ? 'host' : 'player';
}

wss.on('connection', (socket, request) => {
  const url = new URL(request.url, 'http://localhost');
  const role = url.searchParams.get('role') === 'host' ? 'host' : 'player';

  if (role === 'host' && !isAuthorizedHost(url)) {
    console.warn('[relay] 拒絕金鑰不符的大螢幕連線');
    try { socket.close(CLOSE_INVALID_HOST_KEY, 'invalid host key'); } catch (_) { /* 忽略 */ }
    return;
  }

  if (role === 'host') {
    if (host && host !== socket) {
      console.log('[relay] 新的大螢幕接手，關閉舊連線');
      try { host.close(CLOSE_REPLACED_BY_NEW_HOST, 'replaced by a new host'); } catch (_) { /* 忽略 */ }
    }
    host = socket;
    console.log('[relay] 大螢幕已連線');
  } else {
    players.add(socket);
    console.log(`[relay] 手機已連線，目前 ${players.size} 台`);
    // 讓新加入者立刻看到目前階段，不用等下一次推播
    if (lastPhaseMessage) send(socket, lastPhaseMessage);
  }

  socket.on('message', (raw) => {
    const payload = raw.toString();

    // 只窺看 t 欄位做路由，其餘內容原封不動轉發。
    // 壞掉的封包只影響送出它的那條連線，絕不能中斷賽事。
    let message = null;
    try {
      message = JSON.parse(payload);
    } catch (error) {
      console.warn(`[relay] 收到無法解析的訊息（${describe(socket)}），已丟棄`);
      return;
    }

    if (socket === host) {
      if (message.t === 'phase') lastPhaseMessage = payload;

      if (message.to) {
        sendToPlayer(message.to, payload);
      } else {
        broadcastToPlayers(payload);
      }
      return;
    }

    // 從 join 訊息認得這條連線屬於哪個玩家，之後大螢幕才有辦法定向回覆。
    // 這是伺服器唯一「看內容」的地方，純粹為了定址，不涉及任何遊戲規則。
    if (message.t === 'join' && typeof message.pid === 'string' && message.pid) {
      socket.playerId = message.pid;
    }

    send(host, payload);
  });

  socket.on('close', () => {
    if (socket === host) {
      host = null;
      console.log('[relay] 大螢幕已斷線');
    } else {
      players.delete(socket);
      console.log(`[relay] 手機已斷線，剩下 ${players.size} 台`);
    }
  });

  socket.on('error', (error) => {
    console.warn(`[relay] 連線錯誤（${describe(socket)}）：`, error.message);
  });
});

// 定期清掉半死連線：手機息屏或離開 Wi-Fi 時不一定會送 close
const HEARTBEAT_MS = 30000;
setInterval(() => {
  for (const socket of wss.clients) {
    if (socket.readyState === socket.OPEN) socket.ping();
  }
}, HEARTBEAT_MS).unref();

server.listen(PORT, () => {
  console.log(`[relay] 已啟動 http://localhost:${PORT}`);
  console.log('[relay] 大螢幕連線位址  ws://localhost:' + PORT + '/ws?role=host');
  console.log('[relay] 手機開啟網頁     http://localhost:' + PORT + '/');
  console.log('[relay] 大螢幕金鑰檢查   ' + (HOST_KEY ? '啟用' : '未啟用（未設定 HOST_KEY）'));
  if (HOST_KEY) return; // 由 Unity 自動啟動，外網通道也由它負責，下面的手動說明用不到
  console.log('');
  console.log('提醒：手機的動作感測器需要 HTTPS 才能使用。');
  console.log('用區網 IP 直接開會拿不到權限，請改用 cloudflared 之類的通道取得 https 網址：');
  console.log('  cloudflared tunnel --url http://localhost:' + PORT);
  console.log('（沒有 HTTPS 時，手機頁上的「連打」按鈕仍然可用。）');
});

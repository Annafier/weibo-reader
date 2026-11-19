'use strict';
// Weibo Reader Windows shell. Mirrors the macOS AppKit/WebKit shell: it hosts
// the engine bridge over stdio JSON lines, serves the shared desktop UI over a
// local loopback server, and opens it in an app-mode WebView2 window. Native
// folder/file pickers, the clipboard and external links go through PowerShell.
// The engine owns all data safety decisions; this shell only routes messages.
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const crypto = require('node:crypto');
const http = require('node:http');
const { spawn, execFile } = require('node:child_process');

const ROOT = path.resolve(__dirname, '..');
const UI_DIR = path.join(ROOT, 'desktop', 'ui');
const APP_VERSION = (() => { try { return require(ROOT + '/package.json').version || ''; } catch { return ''; } })();
const CONFIG_DIR = process.env.WEIBO_WIN_CONFIG || path.join(process.env.APPDATA || path.join(os.homedir(), 'AppData', 'Roaming'), 'selnokk.weibo.reader');
const SESSION_FILE = path.join(CONFIG_DIR, 'shell.json');
const LINE_LIMIT = 4 * 1024 * 1024;
const MIME = { '.html': 'text/html; charset=utf-8', '.js': 'text/javascript; charset=utf-8', '.css': 'text/css; charset=utf-8', '.json': 'application/json; charset=utf-8', '.svg': 'image/svg+xml', '.png': 'image/png', '.jpg': 'image/jpeg', '.jpeg': 'image/jpeg', '.webp': 'image/webp', '.ico': 'image/x-icon', '.woff2': 'font/woff2' };

let workspace = null, engine = null, engineInput = null, engineAlive = false;
let stdoutBuffer = Buffer.alloc(0), stderrBuffer = Buffer.alloc(0);
let uiReady = false, quitting = false, transportFailed = false;
let windowChild = null, server = null, serverPort = 0;
let sawClient = false, lastDisconnect = 0;
const clients = new Set(), queue = [], logs = [];

function errorText(error) { return String(error && (error.stderr || error.stdout || error.message) || error).trim(); }

function log(message, level = 'error') {
  const line = '[' + new Date().toISOString() + '] ' + message;
  logs.push(line); if (logs.length > 600) logs.splice(0, logs.length - 600);
  if (level !== 'silent') console.error(line);
}

// ---------------------------------------------------------------- engine
function startEngine(explicitWorkspace) {
  workspace = path.resolve(explicitWorkspace || defaultWorkspace());
  engineAlive = true;
    engine = spawn(process.execPath, [path.join(ROOT, 'desktop', 'bridge.js'), '--workspace', workspace, '--wait-for-ready'], {
    cwd: ROOT, windowsHide: true,
    env: (() => {
      const env = { ...process.env, NODE_ENV: 'production', NODE_TLS_REJECT_UNAUTHORIZED: '1' };
      // weibo-cli 的 Windows openBrowser 会在 & 处拆断授权 URL，注入 quoting 修复。
      const openFix = path.join(ROOT, 'windows', 'cli-open-fix.cjs');
      if (fs.existsSync(openFix)) env.NODE_OPTIONS = (env.NODE_OPTIONS ? env.NODE_OPTIONS + ' ' : '') + '--require ' + openFix.split('\\').join('/');
      return env;
    })(),
    stdio: ['pipe', 'pipe', 'pipe'],
  });
  engineInput = engine.stdin;
  engine.stdout.on('data', chunk => {
    stdoutBuffer = Buffer.concat([stdoutBuffer, chunk]);
    while (true) {
      const at = stdoutBuffer.indexOf(10);
      if (at < 0) break;
      const line = stdoutBuffer.subarray(0, at); stdoutBuffer = stdoutBuffer.subarray(at + 1);
      if (line.length > LINE_LIMIT) { failTransport('运行引擎消息超过安全长度。已停止后续请求；已有数据保留，请重新打开应用。'); continue; }
      let event; try { event = JSON.parse(line.toString('utf8')); } catch { continue; }
      handleEngineEvent(event);
    }
    if (stdoutBuffer.length > LINE_LIMIT) failTransport('运行引擎消息超过安全长度。已停止后续请求；已有数据保留，请重新打开应用。');
  });
  engine.stderr.on('data', chunk => {
    stderrBuffer = Buffer.concat([stderrBuffer, chunk]);
    while (true) {
      const at = stderrBuffer.indexOf(10);
      if (at < 0) break;
      const line = stderrBuffer.subarray(0, at).toString('utf8').trim(); stderrBuffer = stderrBuffer.subarray(at + 1);
      if (!line) continue;
      log('bridge: ' + line);
      deliver({ type: 'log', message: line, level: 'error' });
    }
  });
  engine.on('exit', () => {
    engineAlive = false; engineInput = null;
    if (quitting) { finishQuit(); return; }
    deliver({ type: 'engineStopped', message: '运行引擎已结束，请重新打开应用。' });
  });
  engine.on('error', error => { log('无法启动运行引擎：' + error.message); deliver({ type: 'engineStopped', message: '无法启动运行环境：' + error.message }); });
  sendToEngine({ action: 'bootstrap', workspace, requestId: crypto.randomUUID() });
}
function sendToEngine(message) { if (engineInput) try { engineInput.write(JSON.stringify(message) + '\n'); } catch {} }
function failTransport(message) {
  if (transportFailed) return;
  transportFailed = true; log(message);
  deliver({ type: 'engineTransportError', message });
}

function handleEngineEvent(event) {
  if (!event || typeof event !== 'object') return;
  if (event.type === 'open' && typeof event.path === 'string') return openPath(event);
  deliver(event);
}

// Mirrors the macOS completeBrowserOpen handoff.
function openPath(event) {
  const finish = (success, message) => {
    const reply = { type: success ? 'browserOpened' : 'browserOpenFailed', id: event.id || '', workspace: event.workspace || workspace };
    if (event.requestId) reply.requestId = event.requestId;
    if (!success && message) reply.message = message;
    deliver(reply);
    if (event.handoffTicket) sendToEngine({ action: 'reportHandoff', token: event.handoffTicket, success, requestId: event.handoffTicket, workspace: reply.workspace });
    else if (success && event.unreadTicket) sendToEngine({ action: 'reportOpened', token: event.unreadTicket, workspace: reply.workspace });
  };
  try {
    if (event.kind === 'folder') {
      if (!fs.existsSync(event.path)) return finish(false, '无法打开文件夹：目录不存在');
      const child = spawn('explorer.exe', [event.path], { detached: true, stdio: 'ignore', windowsHide: true });
      child.on('error', () => finish(false, '无法打开文件夹'));
      setTimeout(() => finish(true), 300).unref();
    } else {
      const child = spawn('cmd.exe', ['/d', '/s', '/c', 'start', '', event.path], { windowsHide: true, stdio: 'ignore' });
      child.on('error', () => finish(false, '无法打开报告'));
      setTimeout(() => finish(true), 300).unref();
    }
  } catch (error) { finish(false, '无法打开报告：' + error.message); }
}

// ---------------------------------------------------------------- delivery
function deliver(event) {
  if (!uiReady) { queue.push(event); return; }
  broadcast(event);
}
function broadcast(event) {
  const frame = 'data: ' + JSON.stringify(event) + '\n\n';
  for (const res of clients) { try { res.write(frame); } catch {} }
}
function flushQueue() { while (queue.length) broadcast(queue.shift()); }

// ---------------------------------------------------------------- external links
// Same allow-list as desktop/ExternalLinks.swift.
function managementExternalURL(raw) {
  if (typeof raw !== 'string' || raw.length >= 200 || /[%\\\s]/.test(raw)) return null;
  let url; try { url = new URL(raw); } catch { return null; }
  if (url.protocol !== 'https:' || url.username || url.password || url.port) return null;
  const simple = !url.search && !url.hash;
  const host = url.hostname.toLowerCase(), p = url.pathname;
  const user = simple && host === 'weibo.cn' && /^\/u\/[1-9][0-9]{4,19}$/.test(p);
  const legacyPost = simple && host === 'www.weibo.com' && /^\/detail\/[1-9][0-9]{4,19}$/.test(p);
  const service = simple && host === 'open.weibo.com' && p === '/cli/plan';
  const items = [...url.searchParams.keys()];
  const validQuery = new Set(items).size === items.length && items.includes('rl') && [...url.searchParams.entries()].every(([name, value]) => (name === 'rl' && ['0', '1'].includes(value)) || (name === 'uid' && /^[1-9][0-9]{4,19}$/.test(value || '')));
  const post = host === 'weibo.cn' && /^\/comment\/[0-9A-Za-z]{1,12}$/.test(p) && validQuery && url.hash === '#cmtfrm';
  const webPost = host === 'weibo.com' && /^\/(?:[1-9][0-9]{4,19}\/[0-9A-Za-z]{1,12}|detail\/[1-9][0-9]{4,19})$/.test(p) && [...url.searchParams.entries()].length === 1 && url.searchParams.get('from') === 'feed' && url.hash === '#comment';
  return user || legacyPost || service || post || webPost ? url.toString() : null;
}

// ---------------------------------------------------------------- native helpers
function powershell(script, input = null, timeout = 15000) {
  return new Promise(resolve => {
    const child = execFile('powershell.exe', ['-NoProfile', '-NonInteractive', '-Command', script], { windowsHide: true, timeout, maxBuffer: 8 * 1024 * 1024, encoding: 'utf8' }, (error, stdout) => {
      if (error && !stdout) return resolve({ ok: false, error: errorText(error) });
      resolve({ ok: true, out: String(stdout || '').replace(/\r?\n$/, '') });
    });
    if (input != null && child.stdin) { child.stdin.write(input); child.stdin.end(); }
  });
}
function psQuote(value) { return "'" + String(value).replace(/'/g, "''") + "'"; }
async function chooseFolder() {
  const { ok, out } = await powershell(
    "Add-Type -AssemblyName System.Windows.Forms | Out-Null; " +
    "$f=New-Object System.Windows.Forms.FolderBrowserDialog; $f.Description=" + psQuote('选择资料目录') +
    "; $f.ShowNewFolderButton=$true; if($f.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK){[Console]::Out.Write($f.SelectedPath)}");
  return ok ? out : '';
}
async function chooseFile(save, defaultName) {
  const kind = save ? 'SaveFileDialog' : 'OpenFileDialog';
  const script = "Add-Type -AssemblyName System.Windows.Forms | Out-Null; " +
    "$f=New-Object System.Windows.Forms." + kind + "; $f.Filter=" + psQuote('WeiboReader 数据包(*.wrdpack)|*.wrdpack') +
    "; $f.AddExtension=$true; $f.FileName=" + psQuote(defaultName || 'WeiboReader-数据.wrdpack') +
    "; if($f.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK){[Console]::Out.Write($f.FileName)}";
  const { ok, out } = await powershell(script);
  return ok ? out : '';
}
async function copyClipboard(text) {
  const { ok } = await powershell("$t=[Console]::In.ReadToEnd(); Set-Clipboard -Value $t", text, 10000);
  return ok;
}

// ---------------------------------------------------------------- http server
const SHIM_SOURCE = fs.readFileSync(path.join(__dirname, 'windows-shim.js'), 'utf8');

function transformIndex(html) {
  // The shared UI keeps a strict CSP (no remote origins, no inline script).
  // The Windows shell serves it through this loopback origin only: the shim is
  // a same-origin script file and EventSource/fetch need 'self' connections.
  let out = html.replace(/connect-src 'none'/, "connect-src 'self'");
  out = out.replace(/<html\b([^>]*)>/i, (match, attrs) => '<html' + attrs + ' data-app-version="' + String(APP_VERSION).replace(/"/g, '') + '">');
  out = out.replace(/<head>/i, '<head><script src="/windows-shim.js"></script>');
  return out;
}
function serveStatic(url, res) {
  let pathname; try { pathname = decodeURIComponent(url.pathname); } catch { res.writeHead(400); return res.end(); }
  if (pathname === '/') pathname = '/desktop/ui/index.html';
  const file = pathname === '/windows-shim.js' ? path.join(__dirname, 'windows-shim.js') : path.resolve(ROOT, '.' + pathname);
  if (!file.startsWith(ROOT + path.sep)) { res.writeHead(403); return res.end(); }
  let bytes; try { bytes = fs.readFileSync(file); } catch { res.writeHead(404, { 'Content-Type': 'text/plain; charset=utf-8' }); return res.end('文件不存在'); }
  const type = MIME[path.extname(file).toLowerCase()] || 'application/octet-stream';
  const body = file === path.join(UI_DIR, 'index.html') ? transformIndex(bytes.toString('utf8')) : bytes;
  res.writeHead(200, { 'Content-Type': type, 'Cache-Control': 'no-store', 'Content-Length': Buffer.byteLength(body) });
  res.end(body);
}
function readBody(req, limit = 512 * 1024) {
  return new Promise(resolve => {
    const chunks = []; let size = 0;
    req.on('data', c => { size += c.length; if (size > limit) { resolve(null); req.destroy(); } else chunks.push(c); });
    req.on('end', () => resolve(Buffer.concat(chunks).toString('utf8')));
    req.on('error', () => resolve(null));
  });
}
function noContent(res) { res.writeHead(204, { 'Cache-Control': 'no-store' }); res.end(); }

async function handleBridgeMessage(message, res) {
  if (!message || typeof message !== 'object' || typeof message.action !== 'string') { res.writeHead(400, { 'Content-Type': 'application/json; charset=utf-8' }); return res.end('{"error":"请求格式无效"}'); }
  const action = message.action;
  if (action === 'ready') {
    // The bridge defers bootstrap until the frontend reports readiness; events
    // buffered before this point are replayed to the first connected window.
    uiReady = true; flushQueue();
    return noContent(res);
  }
  if (action === 'startupListReady') return noContent(res);
  if (action === 'chooseWorkspace') {
    noContent(res);
    const chosen = await chooseFolder();
    if (!chosen) return deliver({ type: 'workspaceCancelled', workspace, requestId: message.requestId || '' });
    return sendToEngine({ action: 'workspace', workspace, originWorkspace: workspace, requestId: message.requestId || crypto.randomUUID(), path: chosen });
  }
  if (action === 'chooseDataExport') {
    noContent(res);
    const chosen = await chooseFile(true, 'WeiboReader-数据-' + Math.floor(Date.now() / 1000) + '.wrdpack');
    if (!chosen) return deliver({ type: 'exchangeCancelled', workspace, requestId: message.requestId || '' });
    return sendToEngine({ action: 'exchangeExport', workspace, path: chosen, selection: message.selection || 'all', requestId: message.requestId || crypto.randomUUID() });
  }
  if (action === 'chooseDataImport') {
    noContent(res);
    const chosen = await chooseFile(false);
    if (!chosen) return deliver({ type: 'exchangeCancelled', workspace, requestId: message.requestId || '' });
    return sendToEngine({ action: 'exchangeInspect', workspace, path: chosen, requestId: message.requestId || crypto.randomUUID() });
  }
  if (action === 'copy') {
    noContent(res);
    const text = typeof message.text === 'string' && message.text.length < 4 * 1024 * 1024 ? message.text : '';
    const done = text ? await copyClipboard(text) : false;
    return deliver({ type: 'copied', success: done, workspace });
  }
  if (action === 'external') {
    noContent(res);
    const target = managementExternalURL(message.url || '');
    if (target) spawn('cmd.exe', ['/d', '/s', '/c', 'start', '', target], { windowsHide: true, stdio: 'ignore' }).on('error', () => deliver({ type: 'error', message: '无法打开微博链接' }));
    else deliver({ type: 'error', message: '链接未通过安全校验，未打开' });
    return;
  }
  if (action === 'feedbackPreview' || action === 'feedbackCopy' || action === 'feedbackExport') return handleFeedback(action, message, res);
  if (transportFailed) return noContent(res);
  sendToEngine(message);
  noContent(res);
}
async function handleFeedback(action, message, res) {
  noContent(res);
  const requestId = message.requestId || '';
  try {
    if (action === 'feedbackPreview') {
      const snapshotId = 'log-' + crypto.randomUUID();
      const body = logs.join('\n');
      deliver({ type: 'feedbackPreview', requestId, snapshotId, preview: body, json: '', text: 'Weibo Reader Windows ' + APP_VERSION + '\n' + body, summary: { lines: logs.length }, truncated: false, workspace });
    } else if (action === 'feedbackCopy') {
      const ok = await copyClipboard(message.text != null ? String(message.text) : logs.join('\n'));
      deliver({ type: 'feedbackResult', requestId, status: ok ? 'copied' : 'failed', workspace });
    } else {
      const chosen = await chooseFile(true, 'WeiboReader-日志-' + Math.floor(Date.now() / 1000) + '.txt');
      if (!chosen) deliver({ type: 'feedbackResult', requestId, status: 'cancelled', workspace });
      else { fs.writeFileSync(chosen, logs.join('\n')); deliver({ type: 'feedbackResult', requestId, status: 'saved', workspace }); }
    }
  } catch (error) {
    deliver({ type: 'feedbackResult', requestId, status: 'failed', message: errorText(error), workspace });
  }
}

function startServer() {
  server = http.createServer((req, res) => {
    const origin = 'http://127.0.0.1:' + serverPort;
    if (req.method === 'GET' && req.url.startsWith('/events')) {
      res.writeHead(200, { 'Content-Type': 'text/event-stream', 'Cache-Control': 'no-store', 'Connection': 'keep-alive', 'X-Accel-Buffering': 'no' });
      res.write(': connected\n\n');
      sawClient = true;
      clients.add(res);
      const heartbeat = setInterval(() => { try { res.write(': ping\n\n'); } catch {} }, 20000);
      res.on('close', () => { clearInterval(heartbeat); clients.delete(res); lastDisconnect = Date.now(); });
      return;
    }
    if (req.method === 'POST' && req.url === '/bridge') return void readBody(req).then(async body => {
      let message; try { message = JSON.parse(body || ''); } catch { res.writeHead(400); return res.end(); }
      try { await handleBridgeMessage(message, res); } catch (error) { res.writeHead(500); res.end(); }
    });
    if (req.method === 'GET') return serveStatic(new URL(req.url, origin), res);
    res.writeHead(405); res.end();
  });
  server.listen(0, '127.0.0.1', () => {
    serverPort = server.address().port;
    console.log('Weibo Reader Windows：本地入口 http://127.0.0.1:' + serverPort);
    saveSession();
    openWindow();
    // Window lifetime is tracked through its live SSE connection: when every
    // window is gone (EventSource disconnected) and none returns, quit like a
    // desktop app. Launcher processes are unreliable signals on Windows.
    const openedAt = Date.now();
    const watchdog = setInterval(() => {
      if (sawClient && clients.size) return;
      if (sawClient && Date.now() - lastDisconnect < 15000) return;
      // Safety net: a launched window that never established its connection.
      if (!sawClient && Date.now() - openedAt < 90000) return;
      clearInterval(watchdog);
      log('所有窗口已关闭，正在退出', 'silent');
      shutdown();
    }, 5000);
  });
  server.on('error', error => { console.error('本地入口启动失败：' + error.message); process.exit(1); });
}

// ---------------------------------------------------------------- window
function registeredExe(name) {
  // Installations live on any drive; the App Paths registry key is authoritative.
  try {
    const { execFileSync } = require('node:child_process');
    const out = execFileSync('reg.exe', ['query', 'HKLM\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\App Paths\\' + name, '/ve'], { encoding: 'utf8', timeout: 5000, windowsHide: true, stdio: ['ignore', 'pipe', 'ignore'] });
    const match = out.match(/REG_SZ\s+(\S.*)/);
    const value = match ? match[1].trim() : '';
    return value && fs.existsSync(value) ? value : null;
  } catch { return null; }
}
function findChromiumBrowser() {
  const registered = registeredExe('msedge.exe') || registeredExe('chrome.exe') || registeredExe('brave.exe');
  if (registered) return registered;
  const candidates = [];
  const add = exe => { if (exe && fs.existsSync(exe)) candidates.push(exe); };
  add(process.env['ProgramFiles'] ? path.join(process.env['ProgramFiles'], 'Microsoft', 'Edge', 'Application', 'msedge.exe') : null);
  add(process.env['ProgramFiles(x86)'] ? path.join(process.env['ProgramFiles(x86)'], 'Microsoft', 'Edge', 'Application', 'msedge.exe') : null);
  add(process.env.LOCALAPPDATA ? path.join(process.env.LOCALAPPDATA, 'Microsoft', 'Edge', 'Application', 'msedge.exe') : null);
  add(process.env['ProgramFiles'] ? path.join(process.env['ProgramFiles'], 'Google', 'Chrome', 'Application', 'chrome.exe') : null);
  add(process.env['ProgramFiles(x86)'] ? path.join(process.env['ProgramFiles(x86)'], 'Google', 'Chrome', 'Application', 'chrome.exe') : null);
  add(process.env.LOCALAPPDATA ? path.join(process.env.LOCALAPPDATA, 'Google', 'Chrome', 'Application', 'chrome.exe') : null);
  add(process.env['ProgramFiles'] ? path.join(process.env['ProgramFiles'], 'BraveSoftware', 'Brave-Browser', 'Application', 'brave.exe') : null);
  return candidates[0] || null;
}
function findFirefox() {
  const registered = registeredExe('firefox.exe');
  if (registered) return registered;
  for (const base of [process.env['ProgramFiles'], process.env['ProgramFiles(x86)'], process.env.LOCALAPPDATA]) {
    if (!base) continue;
    const exe = path.join(base, 'Mozilla Firefox', 'firefox.exe');
    if (fs.existsSync(exe)) return exe;
  }
  return null;
}
function openWindow(explicitUrl) {
  const url = explicitUrl || 'http://127.0.0.1:' + serverPort + '/';
  if (process.env.WEIBO_WIN_HEADLESS === '1') { console.log('headless：跳过窗口启动，' + url); return; }
  const chromium = findChromiumBrowser();
  if (chromium) {
    // App mode keeps a dedicated chromeless window bound to this session only.
    const profile = path.join(os.tmpdir(), 'weibo-reader-window-' + process.pid);
    windowChild = spawn(chromium, ['--app=' + url, '--user-data-dir=' + profile, '--no-first-run', '--no-default-browser-check', '--window-size=1240,850'], { stdio: 'ignore', windowsHide: true });
    windowChild.on('error', () => { windowChild = null; openFirefox(url); });
    console.log('窗口：' + path.basename(chromium) + '（应用模式）');
    return;
  }
  openFirefox(url);
}
function openFirefox(url) {
  const firefox = findFirefox();
  if (firefox) {
    // A dedicated profile keeps this window separate from the user's browser.
    const profile = path.join(os.tmpdir(), 'weibo-reader-ff-' + process.pid);
    windowChild = spawn(firefox, ['--new-instance', '--profile', profile, '--new-window', url, '--width', '1240', '--height', '850'], { stdio: 'ignore', windowsHide: true });
    windowChild.on('error', () => { windowChild = null; openDefaultBrowser(url); });
    console.log('窗口：Firefox（独立资料）');
    return;
  }
  openDefaultBrowser(url);
}
function openDefaultBrowser(url) {
  console.log('未找到可用的应用窗口内核，改用系统默认浏览器打开：' + url);
  spawn('cmd.exe', ['/d', '/s', '/c', 'start', '', url], { windowsHide: true, stdio: 'ignore' }).on('error', () => {});
}

// ---------------------------------------------------------------- session
function defaultWorkspace() {
  try { return fs.readFileSync(path.join(CONFIG_DIR, 'workspace.txt'), 'utf8').trim() || CONFIG_DIR; } catch { return CONFIG_DIR; }
}
function existingSession() {
  try {
    const session = JSON.parse(fs.readFileSync(SESSION_FILE, 'utf8'));
    if (session && Number.isSafeInteger(session.pid) && session.pid > 0 && Number.isSafeInteger(session.port) && session.pid !== process.pid) {
      try { process.kill(session.pid, 0); return session; } catch {}
    }
  } catch {}
  return null;
}
function saveSession() {
  fs.mkdirSync(CONFIG_DIR, { recursive: true });
  fs.writeFileSync(SESSION_FILE, JSON.stringify({ pid: process.pid, port: serverPort }));
}

function shutdown() {
  if (quitting) return;
  quitting = true;
  if (engineAlive) {
    sendToEngine({ action: 'shutdown' });
    const killer = setTimeout(() => { try { spawn('taskkill', ['/pid', String(engine.pid), '/T', '/F'], { windowsHide: true, stdio: 'ignore' }); } catch {} }, 5000);
    engine.once('exit', () => { clearTimeout(killer); finishQuit(); });
    setTimeout(() => { if (quitting) finishQuit(); }, 6000).unref();
  } else finishQuit();
}
function finishQuit() {
  for (const res of clients) { try { res.end(); } catch {} }
  try { server && server.close(); } catch {}
  try { if (fs.existsSync(SESSION_FILE)) { const session = JSON.parse(fs.readFileSync(SESSION_FILE, 'utf8')); if (session.pid === process.pid) fs.unlinkSync(SESSION_FILE); } } catch {}
  process.exit(0);
}

// ---------------------------------------------------------------- main
function main() {
  const argv = process.argv.slice(2), at = argv.indexOf('--workspace');
  const explicit = at >= 0 ? argv[at + 1] : null;
  fs.mkdirSync(CONFIG_DIR, { recursive: true });
  if (explicit) fs.writeFileSync(path.join(CONFIG_DIR, 'workspace.txt'), path.resolve(explicit));
  const session = existingSession();
  if (session) {
    console.log('已有 Weibo Reader 实例（PID ' + session.pid + '），打开新窗口加入。');
    openWindow('http://127.0.0.1:' + session.port + '/');
    // The window joins the existing session; its own engine keeps running.
    setTimeout(() => process.exit(0), 1500).unref();
    return;
  }
  startEngine(explicit);
  startServer();
  process.on('SIGINT', shutdown);
  process.on('SIGTERM', shutdown);
  process.on('SIGHUP', shutdown);
}
if (require.main === module) main();
module.exports = { managementExternalURL, transformIndex, startServer };

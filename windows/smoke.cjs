'use strict';
// Headless smoke test for the Windows shell: page service, CSP transform,
// engine handshake and event streaming. Run: node windows/smoke.cjs
const fs = require('node:fs'), os = require('node:os'), path = require('node:path'), { spawn } = require('node:child_process');

const ROOT = path.resolve(__dirname, '..');
const workspace = fs.mkdtempSync(path.join(os.tmpdir(), 'weibo-smoke-ws-'));
fs.writeFileSync(path.join(workspace, 'data.marker'), '');
const configDir = fs.mkdtempSync(path.join(os.tmpdir(), 'weibo-smoke-config-'));
const shell = spawn(process.execPath, [path.join(__dirname, 'win-reader.js'), '--workspace', workspace], {
  cwd: ROOT, env: { ...process.env, WEIBO_WIN_HEADLESS: '1', WEIBO_WIN_CONFIG: configDir }, encoding: 'utf8',
});
let url = '';
const ready = new Promise(resolve => {
  const timer = setInterval(() => {
    // The port line is printed to stdout by the shell.
  }, 100);
  shell.stdout.setEncoding('utf8');
  shell.stdout.on('data', chunk => {
    const match = String(chunk).match(/http:\/\/127\.0\.0\.1:(\d+)/);
    if (match && !url) { url = 'http://127.0.0.1:' + match[1] + '/'; clearInterval(timer); resolve(); }
  });
});
const timeout = new Promise((_, reject) => setTimeout(() => reject(Error('shell did not report a port in 20s')), 20000));

async function main() {
  await Promise.race([ready, timeout]);
  console.log('shell URL:', url);
  const fail = message => { console.error('SMOKE FAIL:', message); shell.kill(); process.exit(1); };

  const index = await (await fetch(url)).text();
  if (!index.includes('/windows-shim.js')) return fail('index.html missing shim injection');
  if (!index.includes("connect-src 'self'")) return fail('index.html CSP not relaxed');
  if (!index.includes('src="app.js"')) return fail('index.html missing app script');

  const shim = await fetch(url + 'windows-shim.js');
  if (!shim.ok) return fail('windows-shim.js not served');
  const app = await fetch(url + 'desktop/ui/app.js');
  if (!app.ok) return fail('desktop UI script not served');
  const lib = await fetch(url + 'lib/post-links.js');
  if (!lib.ok) return fail('shared lib script not served');

  // The engine handshake: bootstrap runs at shell start; the UI 'ready' call
  // releases queued events; a forwarded 'library' request must answer on SSE.
  const events = [];
  const controller = new AbortController();
  const stream = fetch(url + 'events', { signal: controller.signal }).then(r => r.body.getReader());
  const reader = await stream;
  const seen = promiseOfEvent('library');
  (async () => { try { for (; ;) { const { value, done } = await reader.read(); if (done) break; const text = new TextDecoder().decode(value); for (const m of text.matchAll(/data: (.+)\n/g)) { try { events.push(JSON.parse(m[1])); } catch {} } } } catch {} })();

  await fetch(url + 'bridge', { method: 'POST', body: JSON.stringify({ action: 'ready' }) });
  await fetch(url + 'bridge', { method: 'POST', body: JSON.stringify({ action: 'library', workspace }) });
  const event = await Promise.race([seen, new Promise((_, reject) => setTimeout(() => reject(Error('no library event in 30s')), 30000))]);
  console.log('received engine event:', event.type, 'workspace=', event.workspace);
  if (event.workspace !== workspace) return fail('event workspace mismatch');

  controller.abort();
  console.log('SMOKE PASS');
  shell.kill();
  process.exit(0);

  function promiseOfEvent(type) {
    return new Promise(resolve => {
      const timer = setInterval(() => {
        const found = events.find(e => e.type === type);
        if (found) { clearInterval(timer); resolve(found); }
      }, 50);
      setTimeout(() => clearInterval(timer), 31000);
    });
  }
}
main().catch(error => { console.error('SMOKE FAIL:', error.message); shell.kill(); process.exit(1); });

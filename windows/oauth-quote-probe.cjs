'use strict';
// Probe: reproduce weibo-cli oauth.js openBrowser quoting on Windows.
const { execFile } = require('child_process');
const url = 'https://open.weibo.com/cli/oauth/authorize?client_id=123&redirect_uri=http%3A%2F%2F127.0.0.1%3A4567%2Fcallback&code_challenge=abcDEF123';
execFile('cmd', ['/c', 'echo', '""', url], (err, stdout, stderr) => {
  console.log('--- CLI pattern: execFile(cmd,["/c","echo","\"\"",url]) ---');
  console.log('stdout:', JSON.stringify(stdout.trim()));
  console.log('stderr:', JSON.stringify((stderr || '').trim().slice(0, 120)));
  console.log('URL intact:', stdout.includes('code_challenge=abcDEF123'));
  // Fixed pattern: single string command with /s, quotes pre-balanced
  execFile('cmd', ['/d', '/s', '/c', 'start "" "' + url + '"'], (err2, stdout2) => {
    console.log('--- fixed pattern: /d /s /c "start \"\" URL" (echo substituted) ---');
    execFile('cmd', ['/d', '/s', '/c', 'echo "" "' + url + '"'], (err3, stdout3) => {
      console.log('stdout:', JSON.stringify(stdout3.trim()));
      console.log('URL intact:', stdout3.includes('code_challenge=abcDEF123'));
      process.exit(0);
    });
  });
});

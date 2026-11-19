'use strict';
// Verify the cli-open-fix shim:
// 1. unit: fixStartArgs rewrites the CLI's broken cmd-start pattern into
//    rundll32 FileProtocolHandler (keeps the full authorize URL).
// 2. child: with NODE_OPTIONS=--require the preload loads and rewrites the same.
const { execFileSync } = require('node:child_process'), path = require('node:path');
const ROOT = path.resolve(process.argv[2] || '.');
const shim = path.join(ROOT, 'windows', 'cli-open-fix.cjs');
const url = 'https://open.weibo.com/cli/oauth/authorize?client_id=123&redirect_uri=http%3A%2F%2F127.0.0.1%3A4567%2Fcallback&code_challenge=abcDEF123';

const fix = require(shim);
const rewritten = fix.fixStartArgs('cmd', ['/c', 'start', '""', url]);
if (!rewritten || rewritten.file !== 'rundll32.exe' || rewritten.args[0] !== 'url.dll,FileProtocolHandler' || rewritten.args[1] !== url) {
  console.error('UNIT FAIL: ' + JSON.stringify(rewritten));
  process.exit(1);
}
console.log('unit rewrite: ok');

const probeScript = [
  'const shim=require(' + JSON.stringify(shim.split('\\').join('/')) + ');',
  'const url=' + JSON.stringify(url) + ';',
  'const args=shim.fixStartArgs("cmd",["/c","start","\\"\\"",url]);',
  'const ok=!!args&&args.file==="rundll32.exe"&&args.args[1]===url;',
  'console.log("CHILD-REWRITE:"+(ok?"yes":"no"));',
  'if(!ok)process.exit(1);',
].join('\n');
const env = { ...process.env, NODE_OPTIONS: '--require ' + shim.split('\\').join('/') };
const out = execFileSync(process.execPath, ['-e', probeScript], { env, encoding: 'utf8' });
console.log(out.trim());
if (out.indexOf('CHILD-REWRITE:yes') < 0) { console.error('CHILD VERIFY FAIL'); process.exit(1); }
console.log('SHIM VERIFY PASS');

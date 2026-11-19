'use strict';
// Fix for @weibo-ai/weibo-cli oauth.js openBrowser on Windows: its
// execFile('cmd', ['/c','start','""',url]) lets cmd.exe split the URL at every
// '&', so the authorize page only receives client_id and the login fails with
// "Validation failed: query.redirect_uri: Required, query.code_challenge: Required".
// Loaded via NODE_OPTIONS=--require in the engine environment, this rewrites
// that exact pattern into a single-string command whose quotes survive.
function fixStartArgs(file, args) {
    // Rewrite to rundll32 (no cmd, no quoting): FileProtocolHandler receives the
    // URL as one argv element, so '&' cannot split it.
    if (process.platform === 'win32' && file === 'cmd' && Array.isArray(args)
        && args[0] === '/c' && args[1] === 'start' && args[2] === '""'
        && typeof args[3] === 'string' && args[3].indexOf('http') === 0) {
        return { file: 'rundll32.exe', args: ['url.dll,FileProtocolHandler', args[3]] };
    }
    return null;
}
const childProcess = require('child_process');
const originalExecFile = childProcess.execFile;
const patchedExecFile = function patchedExecFile(file, args, options, callback) {
    try {
        var fixed = fixStartArgs(file, args);
        if (fixed) {
            var rest = Array.prototype.slice.call(arguments, 2);
            return childProcess.execFile.call(this, fixed.file, fixed.args, rest[0], rest[1]);
        }
    } catch (error) { /* fall through to the original call */ }
    return originalExecFile.apply(this, arguments);
};
// Keep util.promisify / callback forms working: copy own properties and prototype.
Object.setPrototypeOf(patchedExecFile, originalExecFile);
for (var key of Object.keys(originalExecFile)) {
    try { patchedExecFile[key] = originalExecFile[key]; } catch {}
}
childProcess.execFile = patchedExecFile;
if (childProcess.execFileSync) {
    var originalExecFileSync = childProcess.execFileSync;
    childProcess.execFileSync = function execFileSync(file, args) {
        try {
            var fixedSync = fixStartArgs(file, args);
            if (fixedSync) {
                var rest = Array.prototype.slice.call(arguments, 2);
                return originalExecFileSync.call(this, fixedSync.file, fixedSync.args, rest[0], rest[1]);
            }
        } catch (error) { /* fall through */ }
        return originalExecFileSync.apply(this, arguments);
    };
}
module.exports = { fixStartArgs };

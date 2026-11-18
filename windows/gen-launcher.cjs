'use strict';
// Generates the one-click launcher as pure-ASCII CRLF batch (encoding-safe for cmd.exe).
// NOTE: never put a literal ")" inside an echo within a parenthesized block;
// cmd would close the block early. Keep texts paren-free.
const fs=require('fs');
const lines=[
'@echo off',
'title Weibo Reader',
'cd /d "%~dp0"',
'',
'rem One-click launcher: check environment, install deps, start the native UI.',
'where node >nul 2>nul',
'if errorlevel 1 (',
'    echo [Weibo Reader] Node.js not found. Install Node.js 22.5+ from https://nodejs.org/',
'    pause',
'    exit /b 1',
')',
'',
'if not exist node_modules (',
'    echo [Weibo Reader] First run: installing dependencies...',
'    call npm install',
'    if errorlevel 1 (',
'        echo [Weibo Reader] npm install failed. Check your network and try again.',
'        pause',
'        exit /b 1',
'    )',
')',
'',
'if not exist "windows\\native\\WeiboReader.exe" (',
'    echo [Weibo Reader] First run: building the native UI, about 10 seconds...',
'    call "windows\\native\\build.cmd"',
'    if errorlevel 1 (',
'        echo [Weibo Reader] Native UI build failed. Falling back to the web shell.',
'        node windows/win-reader.js %*',
'        if errorlevel 1 pause',
'        exit /b %errorlevel%',
'    )',
')',
'',
'echo [Weibo Reader] Starting...',
'"windows\\native\\WeiboReader.exe" %*',
'if errorlevel 1 (',
'    echo [Weibo Reader] Exited with an error. See windows\\native\\ui.log',
'    pause',
')',
];
fs.writeFileSync('一键启动.cmd',lines.join('\r\n')+'\r\n','utf8');
console.log('written',fs.statSync('一键启动.cmd').size,'bytes');

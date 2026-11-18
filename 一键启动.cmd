@echo off
chcp 65001 >nul
title Weibo Reader
cd /d "%~dp0"

rem Weibo Reader 一键启动：检查环境 -> 安装依赖 -> 启动 Windows 壳
where node >nul 2>nul
if errorlevel 1 (
    echo [Weibo Reader] 未找到 Node.js，请先安装 Node.js 22.5 或更新版本：https://nodejs.org/
    pause
    exit /b 1
)

if not exist node_modules (
    echo [Weibo Reader] 首次运行，正在安装依赖（约几秒钟）...
    call npm install
    if errorlevel 1 (
        echo [Weibo Reader] 依赖安装失败，请检查网络后重试。
        pause
        exit /b 1
    )
)

echo [Weibo Reader] 正在启动...
node windows/win-reader.js %*
if errorlevel 1 (
    echo [Weibo Reader] 启动失败，详见上方日志。
    pause
)

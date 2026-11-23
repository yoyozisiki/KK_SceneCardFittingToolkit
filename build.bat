@echo off
rem 双击即可编译并部署（等价于 powershell -File build.ps1）。
rem 可用参数：-Rebuild 强制重编、-SkipDeploy 只编译不拷贝。
chcp 65001 >nul
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1" %*
if errorlevel 1 pause


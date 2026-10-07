@echo off
chcp 65001 >nul
set PYTHONIOENCODING=utf-8
"%USERPROFILE%\.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe" "%~dp0typo_review.py"
pause

@echo off
rem All launcher logic lives in injector\launch.ps1, which updates can replace; this file they cannot.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0injector\launch.ps1" %*

@echo off
REM Wrapper batch script to run PowerShell scripts bypassing execution policy
REM Usage: RunPowerShell.cmd <script-path> [arguments...]

setlocal enabledelayedexpansion

set "SCRIPT_PATH=%~1"
shift

REM Build the arguments list
set "ARGS="
:build_args
if not "%~1"=="" (
    set "ARGS=!ARGS! %~1"
    shift
    goto build_args
)

REM Run PowerShell with execution policy set to Bypass for this process
powershell -Command "Set-ExecutionPolicy -ExecutionPolicy Bypass -Scope Process -Force; & '!SCRIPT_PATH!' !ARGS!"

exit /b %errorlevel%

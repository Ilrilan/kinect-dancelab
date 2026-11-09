@echo off
setlocal EnableExtensions
cd /d "%~dp0"
title DanceLab bootstrap

set "REPO=https://github.com/Ilrilan/kinect-dancelab.git"
set "BRANCH=main"

echo ===========================================================
echo   DanceLab bootstrap
echo   folder: %CD%
echo ===========================================================
echo.

rem --- make freshly installed tools visible without reopening the shell ---
call :addpath "%ProgramFiles%\Git\cmd"
call :addpath "%ProgramFiles%\nodejs"
call :addpath "%APPDATA%\npm"
call :addpath "%ProgramFiles%\dotnet"


rem ============================ 1. git =============================
echo [1/6] git
where git >nul 2>&1
if not errorlevel 1 goto :git_ok
echo        not found, installing
call :winget Git.Git
call :addpath "%ProgramFiles%\Git\cmd"
where git >nul 2>&1
if not errorlevel 1 goto :git_ok
echo        ERROR: git is not on PATH.
echo        Install from https://git-scm.com/download/win and run this script again.
goto :fail
:git_ok
git --version
echo.


rem =========================== 2. node =============================
echo [2/6] node and npm
where npm >nul 2>&1
if not errorlevel 1 goto :node_ok
echo        not found, installing Node.js LTS
call :winget OpenJS.NodeJS.LTS
call :addpath "%ProgramFiles%\nodejs"
call :addpath "%APPDATA%\npm"
where npm >nul 2>&1
if not errorlevel 1 goto :node_ok
echo        ERROR: npm is not on PATH.
echo        Install Node.js LTS from https://nodejs.org and run this script again.
goto :fail
:node_ok
call node --version
call npm --version
echo.


rem ====================== 3. Claude Code CLI ========================
echo [3/6] Claude Code CLI
where claude >nul 2>&1
if not errorlevel 1 goto :claude_present
echo        installing @anthropic-ai/claude-code
call npm install -g @anthropic-ai/claude-code
call :addpath "%APPDATA%\npm"
goto :claude_check
:claude_present
echo        already installed, skipping install
:claude_check
where claude >nul 2>&1
if not errorlevel 1 goto :claude_ok
echo        ERROR: claude is not on PATH after install.
echo        Check "npm root -g" and that the npm global bin folder is in PATH.
goto :fail
:claude_ok
call claude --version
echo.


rem ===================== 4. project toolchain =======================
echo [4/6] project toolchain
set "NEEDRESTART="

py --version >nul 2>&1
if not errorlevel 1 goto :py_ok
python --version >nul 2>&1
if not errorlevel 1 goto :py_ok
echo        python: not found, installing for the AIST++ importer
call :winget Python.Python.3.12
set "NEEDRESTART=1"
goto :py_done
:py_ok
echo        python: present
:py_done

where dotnet >nul 2>&1
if not errorlevel 1 goto :dotnet_ok
echo        dotnet: not found, installing SDK for Studio and Player
call :winget Microsoft.DotNet.SDK.8
call :addpath "%ProgramFiles%\dotnet"
set "NEEDRESTART=1"
goto :dotnet_done
:dotnet_ok
echo        dotnet: present
:dotnet_done

if defined NEEDRESTART echo        note: reopen the terminal before using the newly installed toolchain
echo.


rem ======================= 5. repo and remote =======================
echo [5/6] repository
git rev-parse --git-dir >nul 2>&1
if not errorlevel 1 goto :repo_exists
echo        git init
git init -b %BRANCH%
goto :repo_ready
:repo_exists
echo        already a git repo
:repo_ready

git config user.email >nul 2>&1
if not errorlevel 1 goto :ident_ok
echo        no git identity configured, setting it for this repo only
git config user.email "ilrilan2@gmail.com"
git config user.name "Ilrilan"
:ident_ok

git remote get-url origin >nul 2>&1
if errorlevel 1 goto :remote_add
git remote set-url origin "%REPO%"
goto :remote_ready
:remote_add
git remote add origin "%REPO%"
:remote_ready
echo        remote origin: %REPO%

git add -A
git diff --cached --quiet
if not errorlevel 1 goto :nothing_to_commit
git commit -m "spec: DanceLab specification and repo scaffolding"
goto :commit_done
:nothing_to_commit
echo        nothing to commit
:commit_done

echo        fetching origin
git fetch origin
if errorlevel 1 goto :fetch_failed
git rev-parse --verify --quiet origin/%BRANCH% >nul
if errorlevel 1 goto :no_remote_branch
echo        merging origin/%BRANCH%
git merge --allow-unrelated-histories --no-edit origin/%BRANCH%
if errorlevel 1 goto :merge_conflict
goto :repo_done
:no_remote_branch
echo        remote has no %BRANCH% branch yet, nothing to merge
goto :repo_done
:fetch_failed
echo        could not fetch. Remote may be empty or credentials were not provided. Continuing.
:repo_done
echo.
git --no-pager log --oneline -5
echo.
echo        to publish:  git push -u origin %BRANCH%
echo.


rem ============================ 6. launch ===========================
echo [6/6] starting Claude Code in %CD%
echo ===========================================================
echo.
call claude
goto :done


rem =========================== subroutines ==========================
:addpath
if exist "%~1" set "PATH=%PATH%;%~1"
exit /b 0

:winget
where winget >nul 2>&1
if errorlevel 1 goto :winget_missing
winget install --id %~1 --exact --source winget --accept-source-agreements --accept-package-agreements --disable-interactivity
exit /b 0
:winget_missing
echo        winget is unavailable, cannot auto-install %~1
exit /b 1

:merge_conflict
echo.
echo        MERGE CONFLICT. Resolve it, then run: claude
goto :fail

:fail
echo.
echo Bootstrap stopped.
endlocal
pause
exit /b 1

:done
endlocal
exit /b 0

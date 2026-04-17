@echo off
set "steam_master_ipc_name_override=Console"
cd /d "%~dp0"
"C:\Program Files\nodejs\node.exe" "C:\Program Files\nodejs\node_modules\npm\bin\npm-cli.js" install

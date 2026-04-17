@echo off
cd /d "%~dp0"

echo [build] Running tsc -b ...
"C:\Program Files\nodejs\node.exe" --require ./sanitize-env.cjs node_modules\typescript\bin\tsc -b
if %ERRORLEVEL% neq 0 (
    echo [build] tsc failed
    exit /b 1
)

echo [build] Running vite build ...
"C:\Program Files\nodejs\node.exe" --require ./sanitize-env.cjs node_modules\vite\bin\vite.js build
if %ERRORLEVEL% neq 0 (
    echo [build] vite build failed
    exit /b 1
)

echo [build] Done!

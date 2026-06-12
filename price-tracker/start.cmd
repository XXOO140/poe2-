@echo off
echo ========================================
echo   PoE2 物价追踪器
echo ========================================
echo.

REM 检查 Python
python --version >nul 2>&1
if errorlevel 1 (
    echo [错误] 未找到 Python，请安装 Python 3.8+
    pause
    exit /b 1
)

REM 安装依赖
echo [1/2] 安装依赖...
pip install flask -q

REM 启动应用
echo [2/2] 启动应用...
echo.
echo 访问地址: http://localhost:5000
echo 按 Ctrl+C 停止
echo ========================================
echo.

python app.py

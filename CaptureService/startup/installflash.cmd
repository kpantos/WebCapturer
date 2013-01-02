if "%EMULATED%"=="true" goto :EOF
@echo "Installing Flash"
%~dp0install_flash_player_64bit.exe -install
exit /b 0
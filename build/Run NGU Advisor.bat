@setlocal enableextensions
pushd "%~dp0"

set "ADVISORDATA=%userprofile%\AppData\LocalLow\NGUAdvisor"
if not exist "%ADVISORDATA%" mkdir "%ADVISORDATA%"
>"%ADVISORDATA%\install.txt" echo %~dp0
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\injector\update.ps1" -Apply

.\injector\smi.exe inject -p NGUIdle -a .\injector\NGUAdvisor.dll -n NGUAdvisor -c Loader -m Init

popd

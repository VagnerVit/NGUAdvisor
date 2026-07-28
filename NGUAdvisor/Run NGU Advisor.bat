@setlocal enableextensions
pushd "%~dp0"

.\injector\smi.exe inject -p NGUIdle -a .\injector\NGUAdvisor.dll -n NGUAdvisor -c Loader -m Init

popd
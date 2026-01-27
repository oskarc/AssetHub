@echo off
echo Test output > "d:\projects\AssetHub\test-output.txt"
docker ps -a >> "d:\projects\AssetHub\test-output.txt" 2>&1
pause

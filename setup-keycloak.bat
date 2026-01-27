@echo off
setlocal enabledelayedexpansion

set KC_URL=http://keycloak:8080
set ADMIN_USER=admin
set ADMIN_PASS=admin123

echo === Keycloak Setup ===

REM Get admin token
echo Getting admin token...
for /f "tokens=*" %%i in ('curl -s -X POST "%KC_URL%/realms/master/protocol/openid-connect/token" -H "Content-Type: application/x-www-form-urlencoded" -d "grant_type=password&client_id=admin-cli&username=%ADMIN_USER%&password=%ADMIN_PASS%" ^| jq -r .access_token') do set TOKEN=%%i

if "%TOKEN%"=="" (
  echo ERROR: Failed to get token
  exit /b 1
)

echo Token obtained: %TOKEN:~0,20%...

REM Create realm
echo Creating media realm...
curl -s -X POST "%KC_URL%/admin/realms" -H "Content-Type: application/json" -H "Authorization: Bearer %TOKEN%" -d "{\"realm\":\"media\",\"enabled\":true}"

echo Realm creation complete

REM Get client list to see if already exists
echo Checking for existing client...
curl -s -X GET "%KC_URL%/admin/realms/media/clients?clientId=assethub-app" -H "Authorization: Bearer %TOKEN%"

pause

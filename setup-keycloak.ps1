# Keycloak Setup Script - Creates realm, client, and users
$KC_URL = "http://keycloak:8080"
$ADMIN_USER = "admin"
$ADMIN_PASS = "admin123"

Write-Host "=== Keycloak Setup ===" -ForegroundColor Cyan

# 1. Get admin token
Write-Host "1. Getting admin token..." -ForegroundColor Yellow
$tokenResponse = curl.exe -s -X POST "$KC_URL/realms/master/protocol/openid-connect/token" `
  -H "Content-Type: application/x-www-form-urlencoded" `
  -d "grant_type=password&client_id=admin-cli&username=$ADMIN_USER&password=$ADMIN_PASS" | ConvertFrom-Json

if (-not $tokenResponse.access_token) {
  Write-Host "ERROR: Failed to get admin token" -ForegroundColor Red
  exit 1
}

$TOKEN = $tokenResponse.access_token
Write-Host "   ✓ Admin token obtained" -ForegroundColor Green

# 2. Create realm
Write-Host "2. Creating media realm..." -ForegroundColor Yellow
$realmPayload = @{
  realm = "media"
  enabled = $true
} | ConvertTo-Json

curl.exe -s -X POST "$KC_URL/admin/realms" `
  -H "Content-Type: application/json" `
  -H "Authorization: Bearer $TOKEN" `
  -d $realmPayload | Out-Null

Write-Host "   ✓ Media realm created" -ForegroundColor Green

# 3. Create confidential client
Write-Host "3. Creating assethub-app client..." -ForegroundColor Yellow
$clientPayload = @{
  clientId = "assethub-app"
  enabled = $true
  clientAuthenticatorType = "client-secret"
  publicClient = $false
  protocol = "openid-connect"
  redirectUris = @("http://keycloak:8080/signin-oidc")
  standardFlowEnabled = $true
  implicitFlowEnabled = $false
  directAccessGrantsEnabled = $false
  serviceAccountsEnabled = $false
} | ConvertTo-Json

$clientResponse = curl.exe -s -X POST "$KC_URL/admin/realms/media/clients" `
  -H "Content-Type: application/json" `
  -H "Authorization: Bearer $TOKEN" `
  -d $clientPayload

$clientId = ($clientResponse | ConvertFrom-Json).id
Write-Host "   ✓ Client created with ID: $clientId" -ForegroundColor Green

# 4. Get client secret
Write-Host "4. Retrieving client secret..." -ForegroundColor Yellow
$secretResponse = curl.exe -s -X GET "$KC_URL/admin/realms/media/clients/$clientId/client-secret" `
  -H "Authorization: Bearer $TOKEN" | ConvertFrom-Json

$CLIENT_SECRET = $secretResponse.value
Write-Host "   ✓ Client secret: $CLIENT_SECRET" -ForegroundColor Green

# 5. Create testuser
Write-Host "5. Creating testuser..." -ForegroundColor Yellow
$testUserPayload = @{
  username = "testuser"
  email = "test@example.com"
  enabled = $true
  firstName = "Test"
  lastName = "User"
} | ConvertTo-Json

curl.exe -s -X POST "$KC_URL/admin/realms/media/users" `
  -H "Content-Type: application/json" `
  -H "Authorization: Bearer $TOKEN" `
  -d $testUserPayload | Out-Null

# Get testuser ID
$testUserId = (curl.exe -s -X GET "$KC_URL/admin/realms/media/users?username=testuser" `
  -H "Authorization: Bearer $TOKEN" | ConvertFrom-Json)[0].id

# Set testuser password
$passwordPayload = @{
  type = "password"
  value = "testuser123"
  temporary = $false
} | ConvertTo-Json

curl.exe -s -X PUT "$KC_URL/admin/realms/media/users/$testUserId/reset-password" `
  -H "Content-Type: application/json" `
  -H "Authorization: Bearer $TOKEN" `
  -d $passwordPayload | Out-Null

Write-Host "   ✓ testuser created (password: testuser123)" -ForegroundColor Green

# 6. Create mediaadmin user
Write-Host "6. Creating mediaadmin user..." -ForegroundColor Yellow
$adminUserPayload = @{
  username = "mediaadmin"
  email = "admin@media.local"
  enabled = $true
  firstName = "Media"
  lastName = "Admin"
} | ConvertTo-Json

curl.exe -s -X POST "$KC_URL/admin/realms/media/users" `
  -H "Content-Type: application/json" `
  -H "Authorization: Bearer $TOKEN" `
  -d $adminUserPayload | Out-Null

# Get mediaadmin ID
$adminUserId = (curl.exe -s -X GET "$KC_URL/admin/realms/media/users?username=mediaadmin" `
  -H "Authorization: Bearer $TOKEN" | ConvertFrom-Json)[0].id

# Set mediaadmin password
$adminPasswordPayload = @{
  type = "password"
  value = "mediaadmin123"
  temporary = $false
} | ConvertTo-Json

curl.exe -s -X PUT "$KC_URL/admin/realms/media/users/$adminUserId/reset-password" `
  -H "Content-Type: application/json" `
  -H "Authorization: Bearer $TOKEN" `
  -d $adminPasswordPayload | Out-Null

Write-Host "   ✓ mediaadmin created (password: mediaadmin123)" -ForegroundColor Green

Write-Host ""
Write-Host "=== Setup Complete ===" -ForegroundColor Cyan
Write-Host "Client Secret: $CLIENT_SECRET" -ForegroundColor Yellow
Write-Host "Client UUID: $clientId" -ForegroundColor Yellow

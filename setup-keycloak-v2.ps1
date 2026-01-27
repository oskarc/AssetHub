# Keycloak Setup Script - Creates realm, client, and users
$KC_URL = "http://keycloak:8080"
$ADMIN_USER = "admin"
$ADMIN_PASS = "admin123"

$REALM = "media"
$CLIENT_ID = "assethub-app"
$REDIRECT_URI = "http://localhost:7252/signin-oidc"

$logFile = Join-Path $PSScriptRoot "keycloak-setup-output.txt"
"=== Keycloak Setup ===" | Tee-Object -FilePath $logFile -Append

# 1. Get admin token
"1. Getting admin token..." | Tee-Object -FilePath $logFile -Append
try {
  $tokenResponse = curl.exe -s -X POST "$KC_URL/realms/master/protocol/openid-connect/token" `
    -H "Content-Type: application/x-www-form-urlencoded" `
    -d "grant_type=password&client_id=admin-cli&username=$ADMIN_USER&password=$ADMIN_PASS" | ConvertFrom-Json
  
  if (-not $tokenResponse.access_token) {
    "ERROR: Failed to get admin token" | Tee-Object -FilePath $logFile -Append
    exit 1
  }
  
  $TOKEN = $tokenResponse.access_token
  "   ✓ Admin token obtained" | Tee-Object -FilePath $logFile -Append
} catch {
  "ERROR: Exception getting token: $_" | Tee-Object -FilePath $logFile -Append
  exit 1
}

# 2. Create realm
"2. Creating media realm..." | Tee-Object -FilePath $logFile -Append
$realmPayload = @{
  realm = $REALM
  enabled = $true
} | ConvertTo-Json

try {
  $realmResponse = curl.exe -s -X POST "$KC_URL/admin/realms" `
    -H "Content-Type: application/json" `
    -H "Authorization: Bearer $TOKEN" `
    -d $realmPayload
  
  "   ✓ Media realm created" | Tee-Object -FilePath $logFile -Append
} catch {
  "ERROR: Failed to create realm: $_" | Tee-Object -FilePath $logFile -Append
}

# 3. Create confidential client
"3. Creating assethub-app client..." | Tee-Object -FilePath $logFile -Append
$clientPayload = @{
  clientId = $CLIENT_ID
  enabled = $true
  clientAuthenticatorType = "client-secret"
  publicClient = $false
  protocol = "openid-connect"
  redirectUris = @($REDIRECT_URI)
  standardFlowEnabled = $true
  implicitFlowEnabled = $false
  directAccessGrantsEnabled = $false
  serviceAccountsEnabled = $false
} | ConvertTo-Json

try {
  curl.exe -s -X POST "$KC_URL/admin/realms/$REALM/clients" `
    -H "Content-Type: application/json" `
    -H "Authorization: Bearer $TOKEN" `
    -d $clientPayload | Out-Null

  # Keycloak returns 201 with empty body for create; fetch created client by clientId
  $clientId = (curl.exe -s -X GET "$KC_URL/admin/realms/$REALM/clients?clientId=$CLIENT_ID" `
    -H "Authorization: Bearer $TOKEN" | ConvertFrom-Json)[0].id

  if (-not $clientId) {
    "ERROR: Client created but could not resolve internal client id" | Tee-Object -FilePath $logFile -Append
    exit 1
  }

  "   ✓ Client created with internal ID: $clientId" | Tee-Object -FilePath $logFile -Append
} catch {
  "ERROR: Failed to create client: $_" | Tee-Object -FilePath $logFile -Append
  exit 1
}

# 4. Get client secret
"4. Retrieving client secret..." | Tee-Object -FilePath $logFile -Append
try {
  $secretResponse = curl.exe -s -X GET "$KC_URL/admin/realms/$REALM/clients/$clientId/client-secret" `
    -H "Authorization: Bearer $TOKEN" | ConvertFrom-Json
  
  $CLIENT_SECRET = $secretResponse.value
  "   ✓ Client secret: $CLIENT_SECRET" | Tee-Object -FilePath $logFile -Append
} catch {
  "ERROR: Failed to get client secret: $_" | Tee-Object -FilePath $logFile -Append
}

# 4b. Create realm roles
"4b. Creating realm roles..." | Tee-Object -FilePath $logFile -Append
foreach ($roleName in @("viewer", "admin")) {
  $rolePayload = @{ name = $roleName } | ConvertTo-Json
  try {
    curl.exe -s -X POST "$KC_URL/admin/realms/$REALM/roles" `
      -H "Content-Type: application/json" `
      -H "Authorization: Bearer $TOKEN" `
      -d $rolePayload | Out-Null
    "   ✓ Role ensured: $roleName" | Tee-Object -FilePath $logFile -Append
  } catch {
    "ERROR: Failed to create role $roleName: $_" | Tee-Object -FilePath $logFile -Append
  }
}

# 5. Create testuser
"5. Creating testuser..." | Tee-Object -FilePath $logFile -Append
$testUserPayload = @{
  username = "testuser"
  email = "test@example.com"
  enabled = $true
  firstName = "Test"
  lastName = "User"
} | ConvertTo-Json

try {
  curl.exe -s -X POST "$KC_URL/admin/realms/$REALM/users" `
    -H "Content-Type: application/json" `
    -H "Authorization: Bearer $TOKEN" `
    -d $testUserPayload | Out-Null
  
  # Get testuser ID
  $testUserId = (curl.exe -s -X GET "$KC_URL/admin/realms/$REALM/users?username=testuser" `
    -H "Authorization: Bearer $TOKEN" | ConvertFrom-Json)[0].id
  
  # Set testuser password
  $passwordPayload = @{
    type = "password"
    value = "testuser123"
    temporary = $false
  } | ConvertTo-Json
  
  curl.exe -s -X PUT "$KC_URL/admin/realms/$REALM/users/$testUserId/reset-password" `
    -H "Content-Type: application/json" `
    -H "Authorization: Bearer $TOKEN" `
    -d $passwordPayload | Out-Null

  # Assign viewer role
  $viewerRole = curl.exe -s -X GET "$KC_URL/admin/realms/$REALM/roles/viewer" -H "Authorization: Bearer $TOKEN" | ConvertFrom-Json
  $viewerRoleAssignPayload = @(@{ id = $viewerRole.id; name = $viewerRole.name }) | ConvertTo-Json
  curl.exe -s -X POST "$KC_URL/admin/realms/$REALM/users/$testUserId/role-mappings/realm" `
    -H "Content-Type: application/json" `
    -H "Authorization: Bearer $TOKEN" `
    -d $viewerRoleAssignPayload | Out-Null
  
  "   ✓ testuser created (password: testuser123)" | Tee-Object -FilePath $logFile -Append
} catch {
  "ERROR: Failed to create testuser: $_" | Tee-Object -FilePath $logFile -Append
}

# 6. Create mediaadmin user
"6. Creating mediaadmin user..." | Tee-Object -FilePath $logFile -Append
$adminUserPayload = @{
  username = "mediaadmin"
  email = "admin@media.local"
  enabled = $true
  firstName = "Media"
  lastName = "Admin"
} | ConvertTo-Json

try {
  curl.exe -s -X POST "$KC_URL/admin/realms/$REALM/users" `
    -H "Content-Type: application/json" `
    -H "Authorization: Bearer $TOKEN" `
    -d $adminUserPayload | Out-Null
  
  # Get mediaadmin ID
  $adminUserId = (curl.exe -s -X GET "$KC_URL/admin/realms/$REALM/users?username=mediaadmin" `
    -H "Authorization: Bearer $TOKEN" | ConvertFrom-Json)[0].id
  
  # Set mediaadmin password
  $adminPasswordPayload = @{
    type = "password"
    value = "mediaadmin123"
    temporary = $false
  } | ConvertTo-Json
  
  curl.exe -s -X PUT "$KC_URL/admin/realms/$REALM/users/$adminUserId/reset-password" `
    -H "Content-Type: application/json" `
    -H "Authorization: Bearer $TOKEN" `
    -d $adminPasswordPayload | Out-Null

  # Assign admin role
  $adminRole = curl.exe -s -X GET "$KC_URL/admin/realms/$REALM/roles/admin" -H "Authorization: Bearer $TOKEN" | ConvertFrom-Json
  $adminRoleAssignPayload = @(@{ id = $adminRole.id; name = $adminRole.name }) | ConvertTo-Json
  curl.exe -s -X POST "$KC_URL/admin/realms/$REALM/users/$adminUserId/role-mappings/realm" `
    -H "Content-Type: application/json" `
    -H "Authorization: Bearer $TOKEN" `
    -d $adminRoleAssignPayload | Out-Null
  
  "   ✓ mediaadmin created (password: mediaadmin123)" | Tee-Object -FilePath $logFile -Append
} catch {
  "ERROR: Failed to create mediaadmin: $_" | Tee-Object -FilePath $logFile -Append
}

"" | Tee-Object -FilePath $logFile -Append
"=== Setup Complete ===" | Tee-Object -FilePath $logFile -Append
"Client Secret: $CLIENT_SECRET" | Tee-Object -FilePath $logFile -Append
"Client UUID: $clientId" | Tee-Object -FilePath $logFile -Append

"Redirect URI: $REDIRECT_URI" | Tee-Object -FilePath $logFile -Append

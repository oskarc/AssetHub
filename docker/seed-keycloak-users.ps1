# seed-keycloak-users.ps1
# Creates the 40 seed users in the Keycloak "media" realm via the Admin REST API.
# Safe to re-run — skips users that already exist.

param(
    [string]$KeycloakUrl = "https://keycloak.assethub.local:8443",
    [string]$Realm = "media",
    [string]$AdminUser = "admin",
    [string]$AdminPass = "admin123",
    [string]$SeedPassword = "SeedUser123!"
)

# Allow self-signed certs in dev
if (-not ([System.Management.Automation.PSTypeName]'TrustAll').Type) {
    Add-Type @"
using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
public class TrustAll {
    public static void Enable() {
        ServicePointManager.ServerCertificateValidationCallback =
            (object sender, X509Certificate cert, X509Chain chain, SslPolicyErrors errors) => true;
    }
}
"@
}
[TrustAll]::Enable()

# Get admin token
Write-Host "Authenticating as '$AdminUser'..."
$tokenBody = @{
    grant_type    = "password"
    client_id     = "admin-cli"
    username      = $AdminUser
    password      = $AdminPass
}
$tokenResp = Invoke-RestMethod -Uri "$KeycloakUrl/realms/master/protocol/openid-connect/token" `
    -Method Post -Body $tokenBody -ContentType "application/x-www-form-urlencoded"
$token = $tokenResp.access_token
$headers = @{ Authorization = "Bearer $token" }

Write-Host "Authenticated. Fetching existing users..."

# Get existing usernames
$existing = Invoke-RestMethod -Uri "$KeycloakUrl/admin/realms/$Realm/users?max=500" `
    -Headers $headers
$existingNames = $existing | ForEach-Object { $_.username }
Write-Host "Found $($existingNames.Count) existing users."

# Seed user definitions
$users = @(
    @{ username="anna.lindberg";       firstName="Anna";      lastName="Lindberg";    email="anna.lindberg@example.com";       role="viewer" }
    @{ username="erik.johansson";      firstName="Erik";      lastName="Johansson";   email="erik.johansson@example.com";      role="viewer" }
    @{ username="maria.svensson";      firstName="Maria";     lastName="Svensson";    email="maria.svensson@example.com";      role="viewer" }
    @{ username="karl.nilsson";        firstName="Karl";      lastName="Nilsson";     email="karl.nilsson@example.com";        role="viewer" }
    @{ username="sofia.andersson";     firstName="Sofia";     lastName="Andersson";   email="sofia.andersson@example.com";     role="viewer" }
    @{ username="lars.pettersson";     firstName="Lars";      lastName="Pettersson";  email="lars.pettersson@example.com";     role="viewer" }
    @{ username="eva.gustafsson";      firstName="Eva";       lastName="Gustafsson";  email="eva.gustafsson@example.com";      role="viewer" }
    @{ username="oscar.larsson";       firstName="Oscar";     lastName="Larsson";     email="oscar.larsson@example.com";       role="viewer" }
    @{ username="hanna.eriksson";      firstName="Hanna";     lastName="Eriksson";    email="hanna.eriksson@example.com";      role="viewer" }
    @{ username="anders.olsson";       firstName="Anders";    lastName="Olsson";      email="anders.olsson@example.com";       role="viewer" }
    @{ username="emma.persson";        firstName="Emma";      lastName="Persson";     email="emma.persson@example.com";        role="viewer" }
    @{ username="nils.magnusson";      firstName="Nils";      lastName="Magnusson";   email="nils.magnusson@example.com";      role="viewer" }
    @{ username="ida.berglund";        firstName="Ida";       lastName="Berglund";    email="ida.berglund@example.com";        role="viewer" }
    @{ username="johan.holm";          firstName="Johan";     lastName="Holm";        email="johan.holm@example.com";          role="viewer" }
    @{ username="lena.berg";           firstName="Lena";      lastName="Berg";        email="lena.berg@example.com";           role="viewer" }

    @{ username="viktor.strand";       firstName="Viktor";    lastName="Strand";      email="viktor.strand@example.com";       role="contributor" }
    @{ username="klara.lund";          firstName="Klara";     lastName="Lund";        email="klara.lund@example.com";          role="contributor" }
    @{ username="gustav.lindqvist";    firstName="Gustav";    lastName="Lindqvist";   email="gustav.lindqvist@example.com";    role="contributor" }
    @{ username="sara.hedlund";        firstName="Sara";      lastName="Hedlund";     email="sara.hedlund@example.com";        role="contributor" }
    @{ username="david.sandberg";      firstName="David";     lastName="Sandberg";    email="david.sandberg@example.com";      role="contributor" }
    @{ username="frida.nystrom";       firstName="Frida";     lastName="Nyström";     email="frida.nystrom@example.com";       role="contributor" }
    @{ username="axel.ekman";          firstName="Axel";      lastName="Ekman";       email="axel.ekman@example.com";          role="contributor" }
    @{ username="maja.dahlgren";       firstName="Maja";      lastName="Dahlgren";    email="maja.dahlgren@example.com";       role="contributor" }
    @{ username="simon.forsberg";      firstName="Simon";     lastName="Forsberg";    email="simon.forsberg@example.com";      role="contributor" }
    @{ username="alva.holmberg";       firstName="Alva";      lastName="Holmberg";    email="alva.holmberg@example.com";       role="contributor" }
    @{ username="lucas.engstrom";      firstName="Lucas";     lastName="Engström";    email="lucas.engstrom@example.com";      role="contributor" }
    @{ username="elin.fransson";       firstName="Elin";      lastName="Fransson";    email="elin.fransson@example.com";       role="contributor" }

    @{ username="hugo.wikstrom";       firstName="Hugo";      lastName="Wikström";    email="hugo.wikstrom@example.com";       role="manager" }
    @{ username="wilma.sjoblom";       firstName="Wilma";     lastName="Sjöblom";     email="wilma.sjoblom@example.com";       role="manager" }
    @{ username="oliver.sundberg";     firstName="Oliver";    lastName="Sundberg";    email="oliver.sundberg@example.com";     role="manager" }
    @{ username="astrid.bjork";        firstName="Astrid";    lastName="Björk";       email="astrid.bjork@example.com";        role="manager" }
    @{ username="isak.wallin";         firstName="Isak";      lastName="Wallin";      email="isak.wallin@example.com";         role="manager" }
    @{ username="ella.sjostrom";       firstName="Ella";      lastName="Sjöström";    email="ella.sjostrom@example.com";       role="manager" }
    @{ username="leo.nordstrom";       firstName="Leo";       lastName="Nordström";   email="leo.nordstrom@example.com";       role="manager" }
    @{ username="saga.aberg";          firstName="Saga";      lastName="Åberg";       email="saga.aberg@example.com";          role="manager" }

    @{ username="filip.linden";        firstName="Filip";     lastName="Lindén";      email="filip.linden@example.com";        role="admin" }
    @{ username="ebba.nordin";         firstName="Ebba";      lastName="Nordin";      email="ebba.nordin@example.com";         role="admin" }
    @{ username="william.soderberg";   firstName="William";   lastName="Söderberg";   email="william.soderberg@example.com";   role="admin" }
    @{ username="agnes.viklund";       firstName="Agnes";     lastName="Viklund";     email="agnes.viklund@example.com";       role="admin" }
    @{ username="alexander.engberg";   firstName="Alexander"; lastName="Engberg";     email="alexander.engberg@example.com";   role="admin" }
)

# Get available realm roles
$roles = Invoke-RestMethod -Uri "$KeycloakUrl/admin/realms/$Realm/roles" `
    -Headers $headers
$roleMap = @{}
foreach ($r in $roles) { $roleMap[$r.name] = $r }

$created = 0
$skipped = 0
$failed = 0
$rolesFixed = 0

foreach ($u in $users) {
    $roleName = $u.role
    $userId = $null

    if ($existingNames -contains $u.username) {
        # User exists — check if role needs assigning
        try {
            $existingUser = (Invoke-RestMethod -Uri "$KeycloakUrl/admin/realms/$Realm/users?username=$($u.username)&exact=true" -Headers $headers)[0]
            $userId = $existingUser.id
            $currentRoles = Invoke-RestMethod -Uri "$KeycloakUrl/admin/realms/$Realm/users/$userId/role-mappings/realm" -Headers $headers
            $currentRoleNames = $currentRoles | ForEach-Object { $_.name }

            if ($currentRoleNames -contains $roleName) {
                Write-Host "  SKIP  $($u.username) (exists, has $roleName)" -ForegroundColor DarkGray
                $skipped++
                continue
            }

            # Assign the missing role
            if ($roleMap.ContainsKey($roleName)) {
                $rolePayload = ConvertTo-Json -Depth 2 -InputObject @(@{
                    id   = $roleMap[$roleName].id
                    name = $roleName
                })

                Invoke-RestMethod -Uri "$KeycloakUrl/admin/realms/$Realm/users/$userId/role-mappings/realm" `
                    -Method Post -Headers $headers -Body $rolePayload -ContentType "application/json" | Out-Null
                Write-Host "  ROLE  $($u.username) <- $roleName" -ForegroundColor Yellow
                $rolesFixed++
            }
        }
        catch {
            Write-Host "  FAIL  $($u.username) (role fix): $($_.Exception.Message)" -ForegroundColor Red
            $failed++
        }
        continue
    }

    $body = @{
        username      = $u.username
        enabled       = $true
        emailVerified = $true
        firstName     = $u.firstName
        lastName      = $u.lastName
        email         = $u.email
        credentials   = @(@{ type = "password"; value = $SeedPassword; temporary = $false })
    } | ConvertTo-Json -Depth 3

    try {
        $resp = Invoke-WebRequest -Uri "$KeycloakUrl/admin/realms/$Realm/users" `
            -Method Post -Headers $headers -Body $body -ContentType "application/json" `
            -UseBasicParsing

        # Get the user ID from the Location header
        $userId = ($resp.Headers["Location"] -split "/")[-1]

        # Assign realm role
        $roleName = $u.role
        if ($roleMap.ContainsKey($roleName)) {
            $rolePayload = ConvertTo-Json -Depth 2 -InputObject @(@{
                id   = $roleMap[$roleName].id
                name = $roleName
            })

            Invoke-RestMethod -Uri "$KeycloakUrl/admin/realms/$Realm/users/$userId/role-mappings/realm" `
                -Method Post -Headers $headers -Body $rolePayload -ContentType "application/json" | Out-Null
        }

        Write-Host "  OK    $($u.username) [$roleName]" -ForegroundColor Green
        $created++
    }
    catch {
        Write-Host "  FAIL  $($u.username): $($_.Exception.Message)" -ForegroundColor Red
        $failed++
    }
}

Write-Host ""
Write-Host "Done! Created: $created, Roles fixed: $rolesFixed, Skipped: $skipped, Failed: $failed" -ForegroundColor Cyan

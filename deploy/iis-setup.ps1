<#
.SYNOPSIS
    Configures IIS for the FileBridge Admin site: gMSA app pool, Windows auth (Negotiate/Kerberos) plus
    Anonymous, HTTPS binding, and useAppPoolCredentials for Kerberos delegation.
.DESCRIPTION
    Run as Administrator on the Admin node, after publishing the site to $SitePath.
    Assumes: IIS + ASP.NET Core Hosting Bundle are installed, a certificate for the site's hostname is
    already in the local machine store, and an SPN (HTTP/filebridge.agency.gov) is registered against the
    gMSA (setspn -S HTTP/filebridge.agency.gov AGENCY\svc-filebridge-admin$).
#>
param(
    [string]$SiteName    = "FileBridge",
    [string]$SitePath    = "D:\FileBridge\Admin",
    [string]$AppPoolName = "FileBridgeAppPool",
    [string]$GmsaAccount = "AGENCY\svc-filebridge-admin$",
    [string]$HostName    = "filebridge.agency.gov",
    [string]$CertThumbprint = ""
)

$ErrorActionPreference = "Stop"
Import-Module WebAdministration

if (-not (Test-Path "IIS:\AppPools\$AppPoolName")) {
    Write-Host "Creating app pool $AppPoolName..."
    New-WebAppPool -Name $AppPoolName | Out-Null
}
Set-ItemProperty "IIS:\AppPools\$AppPoolName" -Name "managedRuntimeVersion" -Value ""   # No Managed Code, per the ASP.NET Core Module
Set-ItemProperty "IIS:\AppPools\$AppPoolName" -Name "processModel.identityType" -Value 3 # 3 = SpecificUser slot used for gMSA
Set-ItemProperty "IIS:\AppPools\$AppPoolName" -Name "processModel.userName" -Value $GmsaAccount
Set-ItemProperty "IIS:\AppPools\$AppPoolName" -Name "processModel.password" -Value ""    # gMSA: no password
Set-ItemProperty "IIS:\AppPools\$AppPoolName" -Name "processModel.idleTimeout" -Value ([TimeSpan]::Zero)

if (-not (Test-Path "IIS:\Sites\$SiteName")) {
    Write-Host "Creating site $SiteName at $SitePath..."
    New-Website -Name $SiteName -PhysicalPath $SitePath -ApplicationPool $AppPoolName -Port 443 -HostHeader $HostName -Ssl | Out-Null
}

if ($CertThumbprint) {
    Write-Host "Binding certificate $CertThumbprint to *:443:$HostName..."
    $binding = "0.0.0.0!443!$HostName"
    netsh http delete sslcert hostnameport="${HostName}:443" 2>$null | Out-Null
    netsh http add sslcert hostnameport="${HostName}:443" certhash=$CertThumbprint appid="{00000000-0000-0000-0000-000000000000}" certstorename=MY | Out-Null
}

Write-Host "Enabling Anonymous + Windows authentication (Negotiate) with app pool credentials..."
Set-WebConfigurationProperty -Filter "/system.webServer/security/authentication/anonymousAuthentication" -Name Enabled -Value $true -PSPath "IIS:\" -Location $SiteName
Set-WebConfigurationProperty -Filter "/system.webServer/security/authentication/windowsAuthentication" -Name Enabled -Value $true -PSPath "IIS:\" -Location $SiteName
Set-WebConfigurationProperty -Filter "/system.webServer/security/authentication/windowsAuthentication" -Name useAppPoolCredentials -Value $true -PSPath "IIS:\" -Location $SiteName
Set-WebConfigurationProperty -Filter "/system.webServer/security/authentication/windowsAuthentication/providers" -Name "." -Value @{value='Negotiate'} -PSPath "IIS:\" -Location $SiteName

Write-Host @"
IIS configured. Remaining manual steps:
 1. Register the SPN once against the gMSA (from a domain-joined admin workstation):
      setspn -S HTTP/$HostName $GmsaAccount
 2. Grant the gMSA 'Log on as a service' locally if not already covered by gMSA policy.
 3. Set the certificate thumbprint for Data Protection key protection
    (DataProtection:CertificateThumbprint in appsettings.json).
"@

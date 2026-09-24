<#
.SYNOPSIS
    Installs the FileBridge Worker as a Windows Service running under a gMSA.
.DESCRIPTION
    Run as Administrator on each Worker node. Assumes the gMSA is already created in AD
    (New-ADServiceAccount) and installed on this host (Install-ADServiceAccount).
#>
param(
    [string]$InstallPath   = "D:\FileBridge\Worker",
    [string]$PublishSource = ".\publish\worker",
    [string]$ServiceName   = "FileBridge Worker",
    [string]$GmsaAccount   = "AGENCY\svc-filebridge-worker$",
    [string]$StagingRoot   = "D:\FileBridge\Staging",
    [string]$LogsRoot      = "D:\FileBridge\Logs",
    [string]$QuarantineRoot = "\\fileserver\FileBridge$\Quarantine"
)

$ErrorActionPreference = "Stop"

Write-Host "Creating folders..."
foreach ($p in @($InstallPath, $StagingRoot, $LogsRoot)) {
    New-Item -ItemType Directory -Force -Path $p | Out-Null
}

Write-Host "Copying published output from $PublishSource to $InstallPath..."
Copy-Item -Path "$PublishSource\*" -Destination $InstallPath -Recurse -Force

Write-Host "Granting $GmsaAccount modify rights on $InstallPath, $StagingRoot, $LogsRoot..."
foreach ($p in @($InstallPath, $StagingRoot, $LogsRoot)) {
    $acl = Get-Acl $p
    $rule = New-Object System.Security.AccessControl.FileSystemAccessRule($GmsaAccount, "Modify", "ContainerInherit,ObjectInherit", "None", "Allow")
    $acl.AddAccessRule($rule)
    Set-Acl -Path $p -AclObject $acl
}
Write-Host "NOTE: also grant $GmsaAccount Modify on $QuarantineRoot (a UNC share) using your normal share/NTFS process."

$exe = Join-Path $InstallPath "FileBridge.Worker.exe"
if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) {
    Write-Host "Service already exists; stopping for update..."
    Stop-Service -Name $ServiceName -Force
    sc.exe delete $ServiceName | Out-Null
    Start-Sleep -Seconds 2
}

Write-Host "Creating service '$ServiceName' under $GmsaAccount..."
New-Service -Name $ServiceName -BinaryPathName $exe -DisplayName $ServiceName `
    -Description "FileBridge transfer engine (Quartz clustered worker)." -StartupType Automatic -Credential (Get-Credential -UserName $GmsaAccount -Message "gMSA password is not required; press Enter" -ErrorAction SilentlyContinue)

# gMSA services don't need a password; sc.exe config is the reliable way to set one with no prompt.
sc.exe config $ServiceName obj= $GmsaAccount password= "" | Out-Null

Write-Host "Configuring failure recovery: restart after 30s, backoff, restart again after 60s..."
sc.exe failure $ServiceName reset= 86400 actions= restart/30000/restart/60000/restart/120000 | Out-Null

Write-Host "Starting service..."
Start-Service -Name $ServiceName

Write-Host "Done. Check $LogsRoot\worker-*.log and the Windows Event Log if it does not come up healthy."

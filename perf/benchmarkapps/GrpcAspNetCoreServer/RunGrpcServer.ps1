Param
(
  [string]$protocol = "h2c",
  [string]$log_level = "None",
  [bool]$enable_cert_auth = $false,
  [bool]$publish_aot = $false
)

# Command line example:
# .\RunGrpcServer.ps1 -publish_aot $true

Write-Host "Protocol: $protocol" -ForegroundColor Cyan
Write-Host "Log level: $log_level" -ForegroundColor Cyan
Write-Host "Enable cert auth: $enable_cert_auth" -ForegroundColor Cyan
Write-Host "Publish AOT: $publish_aot" -ForegroundColor Cyan
Write-Host

dotnet publish -r win-x64 -c Release --self-contained --output bin\Publish -p:PublishAot=$publish_aot
if ($LASTEXITCODE -ne 0)
{
  exit;
}

.\bin\Publish\GrpcAspNetCoreServer.exe --protocol $protocol --logLevel $log_level --enableCertAuth $enable_cert_auth
Write-Host

Write-Host "Done" -ForegroundColor Cyan
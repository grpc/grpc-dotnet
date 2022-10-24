Param
(
  [parameter(mandatory)][string]$url,
  [parameter(mandatory)][string]$scenario,
  [int]$connections = 1,
  [int]$streams = 1,
  [string]$protocol = "h2c",
  [string]$log_level = "None",
  [bool]$publish_aot = $false
)

# Command line example:
# .\RunGrpcClient.ps1 -url http://localhost:5000 -scenario unary -publish_aot $true

Write-Host "URL: $url" -ForegroundColor Cyan
Write-Host "Connections: $connections" -ForegroundColor Cyan
Write-Host "Streams: $streams" -ForegroundColor Cyan
Write-Host "Scenario: $scenario" -ForegroundColor Cyan
Write-Host "Protocol: $protocol" -ForegroundColor Cyan
Write-Host "Log level: $log_level" -ForegroundColor Cyan
Write-Host "Publish AOT: $publish_aot" -ForegroundColor Cyan
Write-Host

dotnet publish -r win-x64 -c Release --self-contained --output bin\Publish -p:PublishAot=$publish_aot
if ($LASTEXITCODE -ne 0)
{
  exit;
}

.\bin\Publish\GrpcClient.exe -u $url -s $scenario -c $connections --streams $streams -p $protocol
Write-Host

Write-Host "Done" -ForegroundColor Cyan
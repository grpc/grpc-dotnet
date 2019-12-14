Param
(
    [bool]$use_tls = $false
)

$allTests =
  "grpcweb_unary",
  "grpcweb_server_streaming",
  "grpcweb_unary_abort",
  "grpcweb_server_streaming_abort"

Write-Host "Running $($allTests.Count) tests" -ForegroundColor Cyan
Write-Host "Use TLS: $use_tls" -ForegroundColor Cyan
Write-Host

foreach ($test in $allTests)
{
  foreach ($grpcWebMode in "GrpcWeb","GrpcWebText")
  {
    Write-Host "Running $grpcWebMode $test" -ForegroundColor Cyan

    dotnet run --use_tls $use_tls --server_port 8080 --client_type httpclient --grpc_web_mode $grpcWebMode --test_case $test

    Write-Host
  }
}

Write-Host "Done" -ForegroundColor Cyan
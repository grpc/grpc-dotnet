Param
(
    [bool]$use_tls = $false,
    [bool]$use_winhttp = $false,
    [string]$framework = "net5.0",
    [string]$grpc_web_mode = "None",
    [int]$server_port = 50052
)

$allTests =
  "empty_unary",
  "large_unary",
  "client_streaming",
  "server_streaming",
  "ping_pong",
  "empty_stream",

  #"compute_engine_creds",
  #"jwt_token_creds",
  #"oauth2_auth_token",
  #"per_rpc_creds",

  "cancel_after_begin",
  "cancel_after_first_response",
  "timeout_on_sleeping_server",
  "custom_metadata",
  "status_code_and_message",
  "special_status_message",
  "unimplemented_service",
  "unimplemented_method",
  "client_compressed_unary",
  "client_compressed_streaming",
  "server_compressed_unary",
  "server_compressed_streaming"

Write-Host "Running $($allTests.Count) tests" -ForegroundColor Cyan
Write-Host "Use TLS: $use_tls" -ForegroundColor Cyan
Write-Host "Use WinHttp: $use_winhttp" -ForegroundColor Cyan
Write-Host "Framework: $framework" -ForegroundColor Cyan
Write-Host "gRPC-Web mode: $grpc_web_mode" -ForegroundColor Cyan
Write-Host

foreach ($test in $allTests)
{
  Write-Host "Running $test" -ForegroundColor Cyan

  dotnet run --framework $framework --use_tls $use_tls --server_host localhost --server_port $server_port --client_type httpclient --test_case $test --use_winhttp $use_winhttp --grpc_web_mode $grpc_web_mode

  Write-Host
}

Write-Host "Done" -ForegroundColor Cyan
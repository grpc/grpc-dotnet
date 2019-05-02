$allTests =
  "empty_unary",
  "large_unary",
  "client_streaming",
  "server_streaming",
  #"ping_pong",
  "empty_stream",

  #"compute_engine_creds",
  #"jwt_token_creds",
  #"oauth2_auth_token",
  #"per_rpc_creds",

  "cancel_after_begin",
  #"cancel_after_first_response",
  "timeout_on_sleeping_server",
  "custom_metadata",
  "status_code_and_message",
  "unimplemented_service",
  "unimplemented_method"
  #,
  #"client_compressed_unary",
  #"client_compressed_streaming"

Write-Host "Running $($allTests.Count) tests" -ForegroundColor Cyan
Write-Host

foreach ($test in $allTests)
{
  Write-Host "Running $test" -ForegroundColor Cyan
  dotnet run --use_tls false --server_port 50052 --client_type httpclient --test_case $test
  Write-Host
}

Write-Host "Done" -ForegroundColor Cyan
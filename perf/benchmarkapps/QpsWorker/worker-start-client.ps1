# 1. Invoke the bidirection streaming method WorkerService/RunClient using grpcurl.
# 2. Send a setup message to processes standard input.
# 3. Leave standard input so the stream and client stay open.

# Setup payload. This payload is for demonstration purposes only.
# To get actual values, see https://github.com/grpc/grpc/tree/2a0d6234cb2ccebb265c035ffd09ecc9a347b4bf/tools/run_tests/performance#approach-1-use-grpc-oss-benchmarks-framework-recommended
$setup = @"
  {
    "setup": {
      "serverTargets": [
        "localhost:5001"
      ],
      "coreList": [],
      "channelArgs": [],
      "clientType": "ASYNC_CLIENT",
      "securityParams": {},
      "clientChannels": 20,
      "rpcType": "UNARY",
      "outstandingRpcsPerChannel": 50,
      "histogramParams": {
        "resolution": 0.01,
        "maxPossible": 60000000000.0
      },
      "loadParams": {
        "closedLoop": {}
      },
      "payloadConfig": {
        "simpleParams": {
          "reqSize": 50,
          "respSize": 50
        }
      }
    }
  }
"@;

# Stats mark payload.
$mark = @"
{
  "mark": {}
}
"@;

$psi = New-Object System.Diagnostics.ProcessStartInfo;
$psi.FileName = "grpcurl.exe";
$psi.Arguments = "-plaintext -d @ localhost:5000 grpc.testing.WorkerService/RunClient";
$psi.UseShellExecute = $false;
$psi.RedirectStandardInput = $true;

Write-Output "Starting process"
$p = [System.Diagnostics.Process]::Start($psi);
$p.StandardInput.WriteLine($setup);

while ($true) {
    Start-Sleep -Seconds 5
    $p.StandardInput.WriteLine($mark);
}

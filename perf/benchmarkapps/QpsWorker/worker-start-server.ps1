# 1. Invoke the bidirection streaming method WorkerService/RunServer using grpcurl.
# 2. Send a setup message to processes standard input.
# 3. Leave standard input so the stream and server stay open.

# Setup payload.
$setup = @"
  {
    "setup": {
      "serverType": "ASYNC_SERVER",
      "port": 5001,
      "coreList": [],
      "channelArgs": [],
      "securityParams": {}
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
$psi.Arguments = "-plaintext -d @ localhost:5000 grpc.testing.WorkerService/RunServer";
$psi.UseShellExecute = $false;
$psi.RedirectStandardInput = $true;

Write-Output "Starting process"
$p = [System.Diagnostics.Process]::Start($psi);
$p.StandardInput.WriteLine($setup);

while ($true) {
    Start-Sleep -Seconds 5
    $p.StandardInput.WriteLine($mark);
}

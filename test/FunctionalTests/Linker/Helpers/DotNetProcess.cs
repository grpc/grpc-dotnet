#region Copyright notice and license

// Copyright 2019 The gRPC Authors
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

#endregion

using System.Diagnostics;
using System.Text;

namespace Grpc.AspNetCore.FunctionalTests.Linker.Helpers;

public class DotNetProcess : IDisposable
{
    private readonly TaskCompletionSource<object?> _exitedTcs;
    private readonly StringBuilder _output;
    private readonly object _outputLock = new object();

    protected Process Process { get; }

    public DotNetProcess()
    {
        _output = new StringBuilder();

        Process = new Process();
        Process.StartInfo = new ProcessStartInfo
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            FileName = @"dotnet"
        };
        Process.EnableRaisingEvents = true;
        Process.Exited += Process_Exited;
        Process.OutputDataReceived += Process_OutputDataReceived;
        Process.ErrorDataReceived += Process_ErrorDataReceived;

        _exitedTcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public Task WaitForExitAsync() => _exitedTcs.Task;
    public int ExitCode => Process.ExitCode;
    public bool HasExited => Process.HasExited;

    public void Start(string fileName, string? arguments)
    {
        Process.StartInfo.FileName = fileName;
        Process.StartInfo.Arguments = arguments ?? string.Empty;
        Process.Start();

        Process.BeginOutputReadLine();
        Process.BeginErrorReadLine();
    }

    public string GetOutput()
    {
        lock (_outputLock)
        {
            return _output.ToString();
        }
    }

    private void Process_OutputDataReceived(object sender, DataReceivedEventArgs e)
    {
        var data = e.Data;
        if (data != null)
        {
            lock (_outputLock)
            {
                _output.AppendLine(data);
            }
        }
    }

    private void Process_ErrorDataReceived(object sender, DataReceivedEventArgs e)
    {
        var data = e.Data;
        if (data != null)
        {
            lock (_outputLock)
            {
                _output.AppendLine("ERROR: " + data);
            }
        }
    }

    private void Process_Exited(object? sender, EventArgs e)
    {
        _exitedTcs.TrySetResult(null);
    }

    public void Dispose()
    {
        try
        {
            if (!Process.HasExited)
            {
                Process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Ignore error
        }

        Process.Dispose();
    }
}

using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ArcGISClaude.Engine
{
    internal sealed class CodexProcess : IDisposable
    {
        private Process _process;

        public async Task RunAsync(AppPaths paths, JObject request, Action<JObject> onEvent)
        {
            var start = new ProcessStartInfo
            {
                FileName = "node.exe",
                WorkingDirectory = paths.WorkspaceDir,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardInputEncoding = new UTF8Encoding(false),
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            start.ArgumentList.Add(Path.Combine(paths.AddinDir, "Engine", "chatgpt.mjs"));
            start.Environment.Remove("NODE_OPTIONS");
            start.Environment.Remove("NODE_PATH");
            var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start Node.js.");
            _process = process;
            if ((string)request["action"] == "turn")
            {
                request["bridgePort"] = paths.BridgePort;
                request["bridgeToken"] = paths.BridgeToken;
            }
            var stderr = process.StandardError.ReadToEndAsync();
            await process.StandardInput.WriteAsync(request.ToString(Formatting.None));
            process.StandardInput.Close();

            string line;
            while ((line = await process.StandardOutput.ReadLineAsync()) != null)
                onEvent(JObject.Parse(line));

            await process.WaitForExitAsync();
            if (process.ExitCode != 0)
            {
                var details = await stderr;
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(details)
                    ? "The ChatGPT operation failed."
                    : details.Trim());
            }
        }

        public void Dispose()
        {
            if (_process == null) return;
            try { if (!_process.HasExited) _process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            _process.Dispose();
            _process = null;
        }
    }
}

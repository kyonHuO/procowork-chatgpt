using System;
using System.IO;

using ArcGIS.Desktop.Framework;
using ArcGIS.Desktop.Framework.Contracts;
using ArcGISClaude.Bridge;

namespace ArcGISClaude
{
    /// <summary>
    /// Add-in module. Acts as the service locator + lifecycle owner for the
    /// shared infrastructure: filesystem paths and the always-on execution
    /// <see cref="BridgeService"/> that serves Codex's live-project tool calls.
    /// The Codex engine process itself is owned per-session by the chat
    /// dock pane view model.
    /// </summary>
    internal class Module1 : Module
    {
        private static Module1 _this;

        /// <summary>Singleton accessor.</summary>
        public static Module1 Current =>
            _this ??= (Module1)FrameworkApplication.FindModule("ArcGISClaude_Module");

        public AppPaths Paths { get; private set; }
        private BridgeService _bridge;

        protected override bool Initialize()
        {
            Paths = AppPaths.Create();

            // The bridge is fully automatic: it comes up with the add-in and lives the
            // whole session, so the user never starts/stops it and it can't go stale.
            // Start it first so its loopback port is known before passing it to the local engine.
            _bridge = new BridgeService(Paths);
            _bridge.Start();

            Paths.EnsureWorkspace(_bridge.Port, _bridge.Token);

            return true;
        }

        protected override void Uninitialize()
        {
            // Kill the chat pane's Codex engine FIRST, then the bridge — otherwise
            // the bridge is torn down under a still-live client mid-call. The pane VM
            // cannot observe Pro shutdown itself (the SDK has no DockPane dispose
            // hook), so it is reached from here.
            try { ChatGptDockPaneViewModel.ShutdownInstance(); } catch { }
            // Stops the listener; the engine's next request gets connection-refused
            // the moment Pro shuts down.
            try { _bridge?.Dispose(); } catch { }
            base.Uninitialize();
        }

        /// <summary>Called by Pro before shutdown; allow it.</summary>
        protected override bool CanUnload() => true;
    }

    /// <summary>
    /// Resolves and seeds the filesystem locations the add-in uses.
    /// </summary>
    internal sealed class AppPaths
    {
        /// <summary>Folder the add-in DLL + shipped Python live in.</summary>
        public string AddinDir { get; private set; }

        /// <summary>Shipped Python toolbox (the ArcPy write path).</summary>
        public string PythonDir { get; private set; }

        /// <summary>Working directory the Codex engine runs in.</summary>
        public string WorkspaceDir { get; private set; }

        /// <summary>Diagnostic log folder under %USERPROFILE%\.arcgis_claude.</summary>
        public string LogDir { get; private set; }

        public int BridgePort { get; private set; }
        public string BridgeToken { get; private set; }
        public string RunScriptToolbox => Path.Combine(PythonDir, "RunScript.pyt");

        public static AppPaths Create()
        {
            var addinDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            var p = new AppPaths
            {
                AddinDir = addinDir,
                PythonDir = Path.Combine(addinDir, "Python"),
                LogDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".procowork_chatgpt"),
                // MyDocuments (not UserProfile\Documents) so a redirected Documents
                // folder — e.g. OneDrive or a Parallels Mac home — resolves correctly.
                WorkspaceDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "ArcGIS", "ProCoworkChatGPT")
            };
            return p;
        }

        public void EnsureWorkspace(int bridgePort, string bridgeToken)
        {
            Directory.CreateDirectory(WorkspaceDir);

            // The shipped template is authoritative: re-seed every template file
            // (AGENTS.md plus the reference/ docs it points at) whenever the workspace
            // copy differs, so template fixes reach existing installs.
            // (Customizations belong in the repo template, not the workspace copy —
            // edits made directly to the workspace copy are overwritten here.)
            // Copy-only: files the engine or user created in the workspace are left alone.
            var templateDir = Path.Combine(AddinDir, "Workspace");
            if (Directory.Exists(templateDir))
                foreach (var template in Directory.EnumerateFiles(templateDir, "*", SearchOption.AllDirectories))
                {
                    var target = Path.Combine(WorkspaceDir, Path.GetRelativePath(templateDir, template));
                    if (File.Exists(target) && File.ReadAllText(template) == File.ReadAllText(target))
                        continue;
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    File.Copy(template, target, overwrite: true);
                }

            BridgePort = bridgePort;
            BridgeToken = bridgeToken;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using ArcGIS.Desktop.Framework;
using ArcGIS.Desktop.Framework.Contracts;
using ArcGISClaude.Engine;
using ArcGISClaude.UI;
using Newtonsoft.Json.Linq;
using RelayCommand = ArcGISClaude.UI.RelayCommand;

namespace ArcGISClaude
{
    internal sealed class ChatGptDockPaneViewModel : DockPane
    {
        private const string DockPaneId = "ArcGISClaude_ChatDockPane";
        private static ChatGptDockPaneViewModel _instance;
        private readonly Dictionary<string, ToolCallVm> _tools = new();
        private CodexProcess _process;
        private string _threadId;
        private string _model;
        private bool _sharing;
        private string _input = "";
        private bool _isTurnActive;
        private bool _showToolOutputs;
        private string _statusText = "Checking ChatGPT connection…";

        public ObservableCollection<ChatItemVm> Items { get; } = new();
        public RelayCommand SendCommand { get; }
        public RelayCommand StopCommand { get; }
        public RelayCommand SignInCommand { get; }
        public RelayCommand DisconnectCommand { get; }

        public string Input
        {
            get => _input;
            set { SetProperty(ref _input, value); SendCommand.RaiseCanExecuteChanged(); }
        }

        public bool IsTurnActive
        {
            get => _isTurnActive;
            private set { SetProperty(ref _isTurnActive, value); RefreshCommands(); }
        }

        public bool ShowToolOutputs
        {
            get => _showToolOutputs;
            set => SetProperty(ref _showToolOutputs, value);
        }

        public string StatusText
        {
            get => _statusText;
            private set => SetProperty(ref _statusText, value);
        }

        public ChatGptDockPaneViewModel()
        {
            _instance = this;
            SendCommand = new RelayCommand(() => _ = SendAsync(),
                () => _sharing && !IsTurnActive && !string.IsNullOrWhiteSpace(Input));
            SignInCommand = new RelayCommand(() => _ = ChangeSessionAsync("signIn"), () => !IsTurnActive && !_sharing);
            DisconnectCommand = new RelayCommand(() => _ = ChangeSessionAsync("disconnect"), () => !IsTurnActive && _sharing);
            StopCommand = new RelayCommand(Stop, () => IsTurnActive);
            _ = RunAsync(new JObject { ["action"] = "status" });
        }

        internal static void Show() => FrameworkApplication.DockPaneManager.Find(DockPaneId)?.Activate();

        internal static void ShutdownInstance() => _instance?.Stop();

        private void RefreshCommands()
        {
            SendCommand.RaiseCanExecuteChanged();
            SignInCommand.RaiseCanExecuteChanged();
            DisconnectCommand.RaiseCanExecuteChanged();
            StopCommand.RaiseCanExecuteChanged();
        }

        private void Stop()
        {
            var process = _process;
            _process = null;
            process?.Dispose();
            _tools.Clear();
            IsTurnActive = false;
            SetConnectionStatus();
        }

        private async Task ChangeSessionAsync(string action)
        {
            if (action == "signIn") StatusText = "Continue in your browser…";
            await RunAsync(new JObject { ["action"] = action });
            if (action == "disconnect") await RunAsync(new JObject { ["action"] = "status" });
        }

        private async Task SendAsync()
        {
            var text = Input.Trim();
            Input = "";
            _tools.Clear();
            Items.Add(new UserMessageVm(text));
            StatusText = "Codex is working…";
            await RunAsync(new JObject
            {
                ["action"] = "turn", ["text"] = text, ["threadId"] = _threadId, ["model"] = _model,
            });
        }

        private async Task RunAsync(JObject request)
        {
            using var process = new CodexProcess();
            _process = process;
            IsTurnActive = true;
            string error = null;
            try
            {
                await process.RunAsync(Module1.Current.Paths, request, ev =>
                {
                    if (_process != process) return;
                    if ((string)ev["type"] == "error") error = (string)ev["message"];
                    else HandleEvent(ev);
                });
            }
            catch (Exception ex)
            {
                if (_process != process) return;
                Items.Add(new SystemNoticeVm(error ?? ex.Message));
                if ((string)request["action"] == "turn")
                {
                    _threadId = null;
                    _model = null;
                }
            }
            finally
            {
                if (_process == process)
                {
                    _process = null;
                    IsTurnActive = false;
                    SetConnectionStatus();
                }
            }
        }

        private void HandleEvent(JObject ev)
        {
            switch ((string)ev["type"])
            {
                case "browser":
                    var uri = new Uri((string)ev["url"]);
                    if (uri.Scheme != "https" || uri.Host != "auth.openai.com")
                        throw new InvalidOperationException("Unexpected ChatGPT sign-in address.");
                    Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
                    break;
                case "session":
                    _sharing = (bool?)ev["session"]?["sharing"] ?? false;
                    if (!_sharing) { _threadId = null; _model = null; }
                    var issue = (string)ev["session"]?["error"]?["message"];
                    if (!string.IsNullOrEmpty(issue)) Items.Add(new SystemNoticeVm(issue));
                    break;
                case "message":
                    Items.Add(new AssistantMessageVm((string)ev["text"]));
                    break;
                case "tool":
                    var tool = new ToolCallVm((string)ev["name"], StreamJsonReader.DescribeToolInput(ev["input"]));
                    _tools[(string)ev["id"]] = tool;
                    Items.Add(tool);
                    break;
                case "toolResult":
                    if (_tools.TryGetValue((string)ev["id"], out var existing))
                    {
                        existing.Result = ev["result"] is JArray content
                            ? string.Join("\n", content.OfType<JObject>().Select(part => (string)part["text"]))
                            : (string)ev["result"];
                        existing.Status = (bool?)ev["failed"] == true ? "error" : "done";
                    }
                    break;
                case "success":
                    _threadId = (string)ev["threadId"];
                    _model = (string)ev["model"];
                    break;
            }
        }

        private void SetConnectionStatus()
        {
            StatusText = _sharing
                ? (_model == null ? "ChatGPT plan connected" : $"ChatGPT plan · {_model}")
                : "Sign in to use your ChatGPT plan.";
        }
    }
}

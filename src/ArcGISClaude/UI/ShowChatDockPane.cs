using ArcGIS.Desktop.Framework.Contracts;

namespace ArcGISClaude
{
    /// <summary>Ribbon button: shows / activates the ProCowork chat dock pane.</summary>
    internal class ShowChatDockPane : Button
    {
        protected override void OnClick() => ChatGptDockPaneViewModel.Show();
    }
}

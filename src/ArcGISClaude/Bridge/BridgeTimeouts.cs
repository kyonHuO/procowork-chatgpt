using System;

namespace ArcGISClaude.Bridge
{
    internal static class BridgeTimeouts
    {
        // The engine's 620-second MCP timeout covers a queued call plus its own GP run.
        public static readonly TimeSpan GpCall = TimeSpan.FromSeconds(290);
    }
}

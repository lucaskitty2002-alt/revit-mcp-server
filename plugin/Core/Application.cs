using System;
using System.IO;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using revit_mcp_plugin.Helpers;
using revit_mcp_plugin.UI;

namespace revit_mcp_plugin.Core
{
    public class Application : IExternalApplication
    {
        private PushButton _connectionButton;
        private string _lastConnectionState;
        private static ExternalEvent _toggleEvent;

        public Result OnStartup(UIControlledApplication application)
        {
            McpLogger.Initialize(Path.GetDirectoryName(typeof(Application).Assembly.Location));
            McpLogger.Info("Application", StudioBrand.Name + " starting");
            _toggleEvent = ExternalEvent.Create(new ConnectionToggleHandler());
            try
            {
                application.RegisterDockablePane(MCPDockablePaneProvider.PaneId,
                    StudioBrand.Name, new MCPDockablePaneProvider());
            }
            catch (Exception ex)
            {
                McpLogger.Warn("Application", "Panel registration: " + ex.Message);
            }

            RibbonPanel panel;
            try
            {
                // A previously created tab may be reused by another loaded module.
                try { application.CreateRibbonTab(StudioBrand.Name); }
                catch (Autodesk.Revit.Exceptions.ArgumentException) { }
                panel = application.CreateRibbonPanel(StudioBrand.Name, "Forbindelse & AI");
            }
            catch (Exception ex)
            {
                McpLogger.Warn("Application", "Using Add-Ins ribbon fallback: " + ex.Message);
                panel = application.CreateRibbonPanel(StudioBrand.Name);
            }
            string assembly = typeof(Application).Assembly.Location;
            _connectionButton = panel.AddItem(new PushButtonData("ID_EXCMD_TOGGLE_REVIT_MCP",
                "MCP\nslukket", assembly, typeof(MCPServiceConnection).FullName)) as PushButton;
            AddButton(panel, assembly, "ID_EXCMD_TOGGLE_MCP_PANEL", "Åbn\nStudio",
                typeof(ToggleMCPPanel), "Åbn VUE Studio med dit originale Codex-vindue.");
            AddButton(panel, assembly, "ID_EXCMD_MCP_SETTINGS", "Indstil-\nlinger",
                typeof(Settings), "Forbindelse, Codex og Revit-værktøjer.");
            AddButton(panel, assembly, "ID_EXCMD_STUDIO_ABOUT", "Om\nStudio",
                typeof(AboutStudio), "Version, kildekode og kredit til open source-projekterne.");
            UpdateConnectionButton();
            // Ribbon APIs stay on Revit's UI thread. This makes no model calls or inference requests.
            application.Idling += OnIdling;
            return Result.Succeeded;
        }

        /// <summary>Schedules the modeless panel's power action in Revit's valid API context.</summary>
        public static bool RequestConnectionToggle()
        {
            var externalEvent = _toggleEvent;
            return externalEvent != null && externalEvent.Raise() != ExternalEventRequest.Denied;
        }

        private sealed class ConnectionToggleHandler : IExternalEventHandler
        {
            public void Execute(UIApplication app)
            {
                var service = SocketService.Instance;
                try
                {
                    if (service.IsRunning) service.Stop();
                    else { service.Initialize(app); service.Start(); }
                }
                catch (Exception ex)
                {
                    service.RecordFailure(ex.Message);
                    McpLogger.Warn("Application", "MCP toggle: " + ex.Message);
                }
            }
            public string GetName() => StudioBrand.Name + " connection toggle";
        }

        private static void AddButton(RibbonPanel panel, string assembly, string id, string label,
            Type command, string tooltip)
        {
            var button = panel.AddItem(new PushButtonData(id, label, assembly, command.FullName)) as PushButton;
            if (button == null) return;
            button.ToolTip = tooltip;
            button.Image = StudioBrand.Logo;
            button.LargeImage = StudioBrand.Logo;
        }

        private void OnIdling(object sender, IdlingEventArgs e) => UpdateConnectionButton();

        private void UpdateConnectionButton()
        {
            if (_connectionButton == null) return;
            var service = SocketService.Instance;
            var state = ConnectionPresentation.From(service.IsRunning, service.LastError, service.Port);
            if (_lastConnectionState == state.StateKey) return;
            _lastConnectionState = state.StateKey;
            _connectionButton.ItemText = state.Label.Replace(" ", "\n");
            _connectionButton.ToolTip = state.Detail + "\n" + state.Action;
            _connectionButton.LongDescription = "Status viser Revit-serverens faktiske tilstand. " +
                "Tændt betyder, at serveren lytter; det garanterer ikke, at Codex er forbundet. " +
                "Tænd/sluk ændrer ikke din Codex-chat eller konto.";
            _connectionButton.Image = state.HasError ? StudioBrand.ErrorIcon : state.IsOn ? StudioBrand.OnIcon : StudioBrand.OffIcon;
            _connectionButton.LargeImage = _connectionButton.Image;
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            application.Idling -= OnIdling;
            _toggleEvent?.Dispose();
            _toggleEvent = null;
            try { MCPDockablePanel.Instance?.Shutdown(); }
            catch (Exception ex) { McpLogger.Warn("Application", "Panel shutdown: " + ex.Message); }
            try { SocketService.Instance.Stop(); }
            catch (Exception ex) { McpLogger.Warn("Application", "Server shutdown: " + ex.Message); }
            _connectionButton = null;
            return Result.Succeeded;
        }
    }
}

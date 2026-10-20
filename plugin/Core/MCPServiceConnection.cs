using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;

namespace revit_mcp_plugin.Core
{
    [Transaction(TransactionMode.Manual)]
    public class MCPServiceConnection : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                // Obtain socket service.
                SocketService service = SocketService.Instance;

                if (service.IsRunning)
                {
                    service.Stop();
                }
                else
                {
                    service.Initialize(commandData.Application);
                    service.Start();
                    if (!service.IsRunning)
                    {
                        message = service.LastError ?? "MCP kunne ikke starte. Se pluginets log.";
                        return Result.Failed;
                    }
                }

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                SocketService.Instance.RecordFailure(ex.Message);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}

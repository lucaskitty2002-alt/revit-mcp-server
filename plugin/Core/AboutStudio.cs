using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace revit_mcp_plugin.Core
{
    [Transaction(TransactionMode.Manual)]
    public class AboutStudio : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var dialog = new TaskDialog("Om " + StudioBrand.Name)
            {
                MainInstruction = StudioBrand.Name + " · AI til dit Revit-arbejde",
                MainContent = "Udviklet til Lucas og Rasmus' VUE-projekt.\n\n" +
                    "Bygger videre på revit-mcp / mcp-servers-for-revit (MIT): Roman Zarkhin, " +
                    "projektets bidragydere, sparx-fire (Bobby Galli) og LuDattilo (Luigi Dattilo).\n\n" +
                    "BIMwright / rvt-mcp (Apache-2.0) har inspireret den tydelige tænd/sluk-visning. " +
                    "Der er ikke kopieret BIMwright-kode i denne udgave.\n\n" +
                    "Codex og dets kontoforbrug styres af din OpenAI-konto. Revit og Autodesk er " +
                    "Autodesks varemærker. VUE Studio er et uafhængigt projekt.",
                FooterText = "Version " + typeof(AboutStudio).Assembly.GetName().Version +
                    " · <a href=\"" + StudioBrand.Repository + "\">Kildekode og licenser</a>",
                CommonButtons = TaskDialogCommonButtons.Close
            };
            dialog.Show();
            return Result.Succeeded;
        }
    }
}

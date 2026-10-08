using System.Reflection;
using Autodesk.Revit.UI;
using Dss.McpHost;

namespace QSNormEngine.Revit;

public sealed class App : IExternalApplication
{
    public Result OnStartup(UIControlledApplication application)
    {
        const string tab = "DSSTech";
        try { application.CreateRibbonTab(tab); } catch { /* tab exists */ }

        var normPanel = application.GetRibbonPanels(tab).FirstOrDefault(x => x.Name == "Cost & Norm")
                        ?? application.CreateRibbonPanel(tab, "Cost & Norm");
        var boqPanel = application.GetRibbonPanels(tab).FirstOrDefault(x => x.Name == "BOQ")
                       ?? application.CreateRibbonPanel(tab, "BOQ");

        var asm = Assembly.GetExecutingAssembly().Location;

        // Cost & Norm: Setup → Norm Selector (bind trước, chọn mã sau)
        normPanel.AddItem(CreateButton("QSSetupParameters", "Setup\nParameters", asm, typeof(SetupParametersCommand).FullName!, "icon-params",
            "Bind Shared Parameters QS_* từ Config/QS_SharedParameters.txt"));
        normPanel.AddItem(CreateButton("QSNormSelector", "Norm\nSelector", asm, typeof(NormSelectorCommand).FullName!, "icon-norm",
            "Phân tích selection → Work Item → mã định mức → VL/NC/M."));

        // BOQ: Formwork Takeoff → BOQ Extract
        boqPanel.AddItem(CreateButton("QSFormworkTakeoff", "Formwork\nTakeoff", asm, typeof(FormworkTakeoffCommand).FullName!, "icon-boq",
            "Đo ván khuôn (cột/vách/dầm/sàn) → lưới kết quả → Ghi QS_Formwork* sau xác nhận."));
        boqPanel.AddItem(CreateButton("QSBoqExtract", "BOQ\nExtract", asm, typeof(BoqExtractCommand).FullName!, "icon-boq",
            "Trích BOQ từ selection, gom theo mã và xuất audit/G8."));
        boqPanel.AddItem(CreateButton("QSQuantitySchedule", "Quantity\nSchedule", asm, typeof(QuantityScheduleCommand).FullName!, "icon-boq",
            "Tạo/cập nhật ViewSchedule QS-BIM Khối lượng cấu kiện (đa category, idempotent)."));

        var mcpPanel = application.GetRibbonPanels(tab).FirstOrDefault(x => x.Name == "MCP")
                       ?? application.CreateRibbonPanel(tab, "MCP");
        mcpPanel.AddItem(CreateButton("DSSTechMcpToggle", "MCP\nOn/Off", asm, typeof(McpToggleCommand).FullName!, "icon-norm",
            "Bật/tắt MCP Named Pipe + HTTP localhost (AI)."));
        mcpPanel.AddItem(CreateButton("DSSTechMcpClients", "MCP\nClients", asm, typeof(McpClientsCommand).FullName!, "icon-params",
            "Hiện URL/token ChatGPT và đường dẫn sidecar Cursor/Claude."));

        var cadPanel = application.GetRibbonPanels(tab).FirstOrDefault(x => x.Name == "CAD")
                       ?? application.CreateRibbonPanel(tab, "CAD");
        cadPanel.AddItem(CreateButton("DSSTechCadLink", "Link\nmặt bằng", asm, typeof(CadLinkHelpCommand).FullName!, "icon-boq",
            "Hướng dẫn link DWG/DXF Origin-to-Origin / Shared Coordinates."));
        cadPanel.AddItem(CreateButton("DSSTechCadDatum", "Trục &\ntầng", asm, typeof(CadDatumCommand).FullName!, "icon-boq",
            "Dựng Grid từ layer G-GRID nếu thiếu (skipIfExists)."));
        cadPanel.AddItem(CreateButton("DSSTechCadPreflight", "CAD\nPreflight", asm, typeof(CadPreflightCommand).FullName!, "icon-boq",
            "Kiểm tra CAD link đã chọn theo CAD_INPUT_SPEC (fail-fast, không tạo)."));
        cadPanel.AddItem(CreateButton("DSSTechCadStructure", "CAD\nKết cấu", asm, typeof(CadStructureCommand).FullName!, "icon-boq",
            "CAD kết cấu → Preview → Confirm → create_batch."));
        cadPanel.AddItem(CreateButton("DSSTechCadArch", "CAD\nKiến trúc", asm, typeof(CadArchCommand).FullName!, "icon-boq",
            "CAD kiến trúc → Preview → Confirm → create_batch."));
        cadPanel.AddItem(CreateButton("DSSTechCadMepf", "CAD\nMEPF", asm, typeof(CadMepfCommand).FullName!, "icon-boq",
            "CAD MEPF → Preview → Confirm → create_batch."));

        McpHostService.Instance.Start(application);
        McpStartupNotice.ShowIfNeeded();

        return Result.Succeeded;
    }

    private static PushButtonData CreateButton(string id, string label, string asm, string className, string iconBase, string toolTip)
    {
        var data = new PushButtonData(id, label, asm, className) { ToolTip = toolTip };
        data.LargeImage = RibbonIcons.Load(iconBase, 32);
        data.Image = RibbonIcons.Load(iconBase, 16);
        return data;
    }

    public Result OnShutdown(UIControlledApplication application)
    {
        McpHostService.Instance.Stop();
        return Result.Succeeded;
    }
}

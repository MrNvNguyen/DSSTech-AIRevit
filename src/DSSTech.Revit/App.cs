using System.Reflection;
using Autodesk.Revit.UI;
using Dss.McpHost;

namespace DSSTech.Revit;

public sealed class App : IExternalApplication
{
    public Result OnStartup(UIControlledApplication application)
    {
        const string tab = "DSSTech";
        try { application.CreateRibbonTab(tab); } catch { /* tab exists */ }

        var asm = Assembly.GetExecutingAssembly().Location;

        ControlCenterHost.Install();
        try
        {
            application.RegisterDockablePane(
                new DockablePaneId(ControlCenterPane.Id),
                "DSSTech",
                new ControlCenterPaneProvider());
        }
        catch (Exception ex)
        {
            DssTechLog.Write($"control center pane: {ex.Message}");
        }

        var startPanel = Panel(application, tab, "Bắt đầu");
        startPanel.AddItem(CreateButton("DSSTechControlCenter", "Control\nCenter", asm, typeof(ControlCenterCommand).FullName!, "icon-control",
            "Mở Control Center ghim bên phải: tham số, QC, Excel, CAD, AI."));
        startPanel.AddItem(CreateButton("DSSTechAccount", "Tài\nkhoản", asm, typeof(AccountCommand).FullName!, "icon-account",
            "Phiên bản, HWID và kích hoạt key trên máy này."));

        var cadPanel = Panel(application, tab, "CAD");
        cadPanel.AddItem(CreateButton("DSSTechCadLink", "Link\nmặt bằng", asm, typeof(CadLinkHelpCommand).FullName!, "icon-link",
            "Link DWG/DXF — người dùng Move/Rotate link khớp model trước khi ModelRevit."));
        cadPanel.AddItem(CreateButton("DSSTechCadDatum", "Trục &\ntầng", asm, typeof(CadDatumCommand).FullName!, "icon-grid",
            "Dựng Grid từ layer G-GRID nếu thiếu (skipIfExists)."));
        cadPanel.AddItem(CreateButton("DSSTechModelRevit", "Model\nRevit", asm, typeof(ModelRevitCommand).FullName!, "icon-model",
            "Chỉnh loại, family, level và offset rồi dựng từ CAD link."));
        cadPanel.AddItem(CreateButton("DSSTechCadPreflight", "CAD\nPreflight", asm, typeof(CadPreflightCommand).FullName!, "icon-preflight",
            "Kiểm tra CAD link đã chọn theo CAD_INPUT_SPEC (fail-fast, không tạo)."));
        cadPanel.AddItem(CreateButton("DSSTechCadStructure", "CAD\nKết cấu", asm, typeof(CadStructureCommand).FullName!, "icon-structure",
            "CAD kết cấu → Preview → Confirm → create_batch."));
        cadPanel.AddItem(CreateButton("DSSTechCadArch", "CAD\nKiến trúc", asm, typeof(CadArchCommand).FullName!, "icon-arch",
            "CAD kiến trúc → Preview → Confirm → create_batch."));
        cadPanel.AddItem(CreateButton("DSSTechCadMepf", "CAD\nMEPF", asm, typeof(CadMepfCommand).FullName!, "icon-mepf",
            "CAD MEPF → Preview → Confirm → create_batch."));

        var qtyPanel = Panel(application, tab, "Khối lượng");
        qtyPanel.AddItem(CreateButton("QSSetupParameters", "Setup\nParameters", asm, typeof(SetupParametersCommand).FullName!, "icon-params",
            "Bind Shared Parameters QS_* từ Config/QS_SharedParameters.txt"));
        qtyPanel.AddItem(CreateButton("QSNormSelector", "Norm\nSelector", asm, typeof(NormSelectorCommand).FullName!, "icon-norm",
            "Phân tích selection → Work Item → mã định mức → VL/NC/M."));
        qtyPanel.AddItem(CreateButton("QSFormworkTakeoff", "Formwork\nTakeoff", asm, typeof(FormworkTakeoffCommand).FullName!, "icon-formwork",
            "Đo ván khuôn (cột/vách/dầm/sàn) → lưới kết quả → Ghi QS_Formwork* sau xác nhận."));
        qtyPanel.AddItem(CreateButton("QSBoqExtract", "BOQ\nExtract", asm, typeof(BoqExtractCommand).FullName!, "icon-boq",
            "Trích BOQ từ selection, gom theo mã và xuất audit/G8."));
        qtyPanel.AddItem(CreateButton("QSQuantitySchedule", "Quantity\nSchedule", asm, typeof(QuantityScheduleCommand).FullName!, "icon-schedule",
            "Tạo/cập nhật ViewSchedule QS-BIM Khối lượng cấu kiện (đa category, idempotent)."));

        var aiPanel = Panel(application, tab, "AI");
        aiPanel.AddItem(CreateButton("DSSTechMcpToggle", "MCP\nOn/Off", asm, typeof(McpToggleCommand).FullName!, "icon-mcp",
            "Bật/tắt MCP Named Pipe + HTTP localhost (AI)."));
        aiPanel.AddItem(CreateButton("DSSTechMcpClients", "MCP\nClients", asm, typeof(McpClientsCommand).FullName!, "icon-clients",
            "Hiện URL/token ChatGPT và đường dẫn sidecar Cursor/Claude."));

        McpHostService.Instance.Start(application);
        McpStartupNotice.ShowIfNeeded();

        return Result.Succeeded;
    }

    private static RibbonPanel Panel(UIControlledApplication application, string tab, string name) =>
        application.GetRibbonPanels(tab).FirstOrDefault(x => x.Name == name)
        ?? application.CreateRibbonPanel(tab, name);

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

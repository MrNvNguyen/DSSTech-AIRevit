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

        // Quy ước nhãn song ngữ (xem docs/RIBBON_UI.md):
        //  - Nút to: 2 dòng "Tiếng Việt\nEnglish".
        //  - Nút nhỏ xếp chồng / mục trong danh sách xổ: chỉ tiếng Việt (1 dòng), tiếng Anh nằm trong tooltip.
        //  - Tooltip: ToolTip = tiếng Việt, LongDescription = tiếng Anh (hiện khi giữ chuột lâu hơn).

        // ── Panel 1: Thiết lập ─────────────────────────────────────────────
        var setup = Panel(application, tab, "Thiết lập · Setup");
        setup.AddItem(Large(asm, "DSSTechControlCenter", "Bảng điều khiển", "Control Center", typeof(ControlCenterCommand), "icon-control",
            "Mở Control Center ghim bên phải: tham số, QC, Excel, CAD, AI.",
            "Open the docked Control Center: parameters, QC, Excel, CAD, AI.", "control-center"));
        setup.AddStackedItems(
            Small(asm, "QSSetupParameters", "Tham số QS", "QS Parameters", typeof(SetupParametersCommand), "icon-params",
                "Gắn Shared Parameters QS_* từ Config/QS_SharedParameters.txt vào dự án.",
                "Bind QS_* shared parameters from Config/QS_SharedParameters.txt.", "tham-so"),
            Small(asm, "DSSTechAccount", "Tài khoản", "Account", typeof(AccountCommand), "icon-account",
                "Phiên bản, mã máy (HWID) và kích hoạt key trên máy này.",
                "Version, machine ID (HWID) and license activation.", "tai-khoan"),
            Small(asm, "DSSTechHelp", "Hướng dẫn", "Help", typeof(HelpCommand), "icon-help",
                "Mở hướng dẫn sử dụng DSSTech. Mẹo: rê chuột lên bất kỳ nút nào rồi nhấn F1.",
                "Open the DSSTech user guide. Tip: hover any button and press F1.", ""));

        // ── Panel 2: CAD → BIM (đánh số theo thứ tự làm việc) ──────────────
        var cad = Panel(application, tab, "CAD → BIM");
        cad.AddStackedItems(
            Small(asm, "DSSTechCadLink", "Link mặt bằng", "Link CAD plan", typeof(CadLinkHelpCommand), "icon-link",
                "Bước 1 — Link DWG/DXF, Move/Rotate link cho khớp model trước khi dựng.",
                "Step 1 — Link the DWG/DXF and align it to the model before building.", "link-mat-bang"),
            Small(asm, "DSSTechCadDatum", "Trục & tầng", "Grids & Levels", typeof(CadDatumCommand), "icon-grid",
                "Bước 2 — Dựng lưới trục từ layer G-GRID nếu chưa có (bỏ qua trục đã tồn tại).",
                "Step 2 — Create grids from layer G-GRID (existing grids are skipped).", "truc-tang"));
        cad.AddItem(Large(asm, "DSSTechModelRevit", "Dựng mô hình", "Model Revit", typeof(ModelRevitCommand), "icon-model",
            "Bước 3 — Chọn loại, family, level, offset rồi dựng cấu kiện từ CAD link.",
            "Step 3 — Pick types, families, level and offset, then build from the CAD link.", "dung-mo-hinh"));

        var wizardData = new PulldownButtonData("DSSTechBatchWizard", "Dựng theo lô\nBatch Wizard")
        {
            ToolTip = "Dựng hàng loạt theo bộ môn: Kiểm tra → Xem trước → Xác nhận → Tạo.",
            LongDescription = "Batch build by discipline: Check → Preview → Confirm → Create.",
            LargeImage = RibbonIcons.Load("icon-wizard", 32),
            Image = RibbonIcons.Load("icon-wizard", 16)
        };
        SetHelp(wizardData, "dung-theo-lo");
        if (cad.AddItem(wizardData) is PulldownButton wizard)
        {
            wizard.AddPushButton(Item(asm, "DSSTechCadPreflight", "Kiểm tra CAD · Preflight", typeof(CadPreflightCommand), "icon-preflight",
                "Kiểm tra CAD link đã chọn theo CAD_INPUT_SPEC (chỉ báo lỗi, không tạo gì).",
                "Check the selected CAD link against CAD_INPUT_SPEC (report only, creates nothing).", "dung-theo-lo"));
            wizard.AddSeparator();
            wizard.AddPushButton(Item(asm, "DSSTechCadStructure", "Kết cấu · Structure", typeof(CadStructureCommand), "icon-structure",
                "CAD kết cấu → Xem trước → Xác nhận → Tạo hàng loạt.",
                "Structural CAD → Preview → Confirm → Batch create.", "dung-theo-lo"));
            wizard.AddPushButton(Item(asm, "DSSTechCadArch", "Kiến trúc · Architecture", typeof(CadArchCommand), "icon-arch",
                "CAD kiến trúc → Xem trước → Xác nhận → Tạo hàng loạt.",
                "Architectural CAD → Preview → Confirm → Batch create.", "dung-theo-lo"));
            wizard.AddPushButton(Item(asm, "DSSTechCadMepf", "Cơ điện · MEPF", typeof(CadMepfCommand), "icon-mepf",
                "CAD MEPF → Xem trước → Xác nhận → Tạo hàng loạt.",
                "MEPF CAD → Preview → Confirm → Batch create.", "dung-theo-lo"));
        }

        // ── Panel 3: Khối lượng ────────────────────────────────────────────
        var qty = Panel(application, tab, "Khối lượng · Quantity");
        qty.AddItem(Large(asm, "QSNormSelector", "Chọn định mức", "Norm Selector", typeof(NormSelectorCommand), "icon-norm",
            "Phân tích cấu kiện đang chọn → công việc → mã định mức → vật liệu / nhân công / máy.",
            "Analyse the selection → work item → norm code → material / labour / machine.", "dinh-muc"));
        qty.AddItem(Large(asm, "QSFormworkTakeoff", "Ván khuôn", "Formwork", typeof(FormworkTakeoffCommand), "icon-formwork",
            "Đo ván khuôn cột / vách / dầm / sàn → bảng kết quả → ghi QS_Formwork* sau khi xác nhận.",
            "Measure formwork of columns / walls / beams / slabs → review → write QS_Formwork* on confirm.", "van-khuon"));
        qty.AddItem(Large(asm, "QSBoqExtract", "Xuất BOQ", "BOQ Export", typeof(BoqExtractCommand), "icon-boq",
            "Trích BOQ từ cấu kiện đang chọn, gom theo mã và xuất file kiểm tra / G8.",
            "Extract the BOQ from the selection, group by code and export audit / G8 files.", "xuat-boq"));
        qty.AddItem(Large(asm, "QSQuantitySchedule", "Bảng khối lượng", "Qty Schedule", typeof(QuantityScheduleCommand), "icon-schedule",
            "Tạo / cập nhật bảng thống kê 'QS-BIM Khối lượng cấu kiện' (nhiều loại cấu kiện, chạy lại không bị trùng).",
            "Create / update the 'QS-BIM element quantities' schedule (multi-category, safe to re-run).", "bang-khoi-luong"));

        // ── Panel 4: Trợ lý AI ─────────────────────────────────────────────
        var ai = Panel(application, tab, "Trợ lý AI · AI");
        RibbonState.AiButton = ai.AddItem(Large(asm, "DSSTechMcpToggle", "AI: Đang bật", "AI: On", typeof(McpToggleCommand), "icon-mcp",
            "", "", "tro-ly-ai")) as PushButton;
        ai.AddItem(Large(asm, "DSSTechMcpClients", "Kết nối AI", "AI Clients", typeof(McpClientsCommand), "icon-clients",
            "Hiện địa chỉ / mã token cho ChatGPT và đường dẫn cấu hình Cursor / Claude.",
            "Show ChatGPT URL / token and the Cursor / Claude config path.", "tro-ly-ai"));

        // MCP is optional: a pipe/port failure must not fail the whole add-in load (QS tools still work).
        try
        {
            McpHostService.Instance.Start(application);
            McpStartupNotice.ShowIfNeeded();
        }
        catch (Exception ex)
        {
            DssTechLog.Write($"MCP start failed: {ex}");
        }
        // Refresh either way so the AI toggle shows Off after a failed start.
        RibbonState.RefreshAi();

        return Result.Succeeded;
    }

    private static RibbonPanel Panel(UIControlledApplication application, string tab, string name) =>
        application.GetRibbonPanels(tab).FirstOrDefault(x => x.Name == name)
        ?? application.CreateRibbonPanel(tab, name);

    /// <summary>Nút to: nhãn 2 dòng Việt / Anh.</summary>
    private static PushButtonData Large(string asm, string id, string vi, string en, Type command, string icon,
        string tipVi, string tipEn, string helpAnchor) =>
        Build(asm, id, $"{vi}\n{en}", command, icon, tipVi, tipEn, helpAnchor);

    /// <summary>Nút nhỏ xếp chồng: chỉ tiếng Việt trên nút, tiếng Anh đưa vào tooltip để không tràn.</summary>
    private static PushButtonData Small(string asm, string id, string vi, string en, Type command, string icon,
        string tipVi, string tipEn, string helpAnchor) =>
        Build(asm, id, vi, command, icon, $"{vi} ({en}) — {tipVi}", tipEn, helpAnchor);

    /// <summary>Mục trong danh sách xổ xuống: 1 dòng "Việt · English" (danh sách rộng nên không tràn).</summary>
    private static PushButtonData Item(string asm, string id, string label, Type command, string icon,
        string tipVi, string tipEn, string helpAnchor) =>
        Build(asm, id, label, command, icon, tipVi, tipEn, helpAnchor);

    private static PushButtonData Build(string asm, string id, string label, Type command, string icon,
        string tipVi, string tipEn, string helpAnchor)
    {
        var data = new PushButtonData(id, label, asm, command.FullName!)
        {
            LargeImage = RibbonIcons.Load(icon, 32),
            Image = RibbonIcons.Load(icon, 16)
        };
        if (!string.IsNullOrEmpty(tipVi))
            data.ToolTip = tipVi;
        if (!string.IsNullOrEmpty(tipEn))
            data.LongDescription = tipEn;
        SetHelp(data, helpAnchor);
        return data;
    }

    /// <summary>F1 trên nút → mở Help/HuongDan.html (đúng mục nếu trình duyệt giữ được #anchor).</summary>
    private static void SetHelp(RibbonItemData data, string anchor)
    {
        try
        {
            var url = HelpCommand.HelpUri(anchor);
            if (url is not null)
                data.SetContextualHelp(new ContextualHelp(ContextualHelpType.Url, url));
        }
        catch (Exception ex)
        {
            DssTechLog.Write($"ribbon help {data.Name}: {ex.Message}");
        }
    }

    public Result OnShutdown(UIControlledApplication application)
    {
        McpHostService.Instance.Stop();
        return Result.Succeeded;
    }
}

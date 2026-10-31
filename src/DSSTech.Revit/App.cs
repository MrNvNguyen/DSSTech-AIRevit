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

        // ── Panel 2: Kiến trúc (Trục, Level là nền cho các bộ môn) ─────────
        var arch = Panel(application, tab, "Kiến trúc · Architecture");
        arch.AddItem(Large(asm, "DSSTechCadDatum", "Trục", "Grids", typeof(CadDatumCommand), "icon-part-grid",
            "Dựng lưới trục từ layer G-GRID nếu chưa có (bỏ qua trục đã tồn tại).",
            "Create grids from layer G-GRID (existing grids are skipped).", "truc-tang"));
        arch.AddItem(Large(asm, "DSSTechModelLevel", "Level", "Levels", typeof(ModelLevelCommand), "icon-level",
            "Dựng Level. Chức năng đang được bổ sung.",
            "Create levels. Coming soon.", "truc-tang"));
        arch.AddSeparator();
        arch.AddItem(Part(asm, "DSSTechModelArchWall", "Tường kiến trúc", "Arch Wall", typeof(ModelArchitecturalWallCommand), "icon-part-archwall"));
        arch.AddItem(Part(asm, "DSSTechModelDoor", "Cửa", "Door", typeof(ModelDoorCommand), "icon-part-door"));
        arch.AddItem(Part(asm, "DSSTechModelWindow", "Cửa sổ", "Window", typeof(ModelWindowCommand), "icon-part-window"));
        arch.AddStackedItems(
            SmallPart(asm, "DSSTechModelRoom", "Phòng", "Room", typeof(ModelRoomCommand), "icon-part-room"),
            SmallPart(asm, "DSSTechModelCeiling", "Trần", "Ceiling", typeof(ModelCeilingCommand), "icon-part-ceiling"));

        // ── Panel 3: Kết cấu ───────────────────────────────────────────────
        var structure = Panel(application, tab, "Kết cấu · Structure");
        structure.AddItem(Part(asm, "DSSTechModelColumn", "Cột", "Column", typeof(ModelColumnCommand), "icon-part-column"));
        structure.AddItem(Part(asm, "DSSTechModelStructuralWall", "Tường kết cấu", "Structural Wall", typeof(ModelStructuralWallCommand), "icon-part-wall"));
        structure.AddItem(Part(asm, "DSSTechModelBeam", "Dầm", "Beam", typeof(ModelBeamCommand), "icon-part-beam"));
        structure.AddItem(Part(asm, "DSSTechModelFloor", "Sàn", "Floor", typeof(ModelFloorCommand), "icon-part-floor"));

        // ── Panel 4: Móng & cọc ────────────────────────────────────────────
        var foundation = Panel(application, tab, "Móng & cọc · Foundation");
        foundation.AddStackedItems(
            SmallPart(asm, "DSSTechModelPileCap", "Đài cọc", "Pile Cap", typeof(ModelPileCapCommand), "icon-part-pilecap"),
            SmallPart(asm, "DSSTechModelFooting", "Móng đơn", "Isolated Footing", typeof(ModelIsolatedFoundationCommand), "icon-part-footing"),
            SmallPart(asm, "DSSTechModelStrip", "Móng băng", "Strip Footing", typeof(ModelStripFoundationCommand), "icon-part-strip"));
        foundation.AddStackedItems(
            SmallPart(asm, "DSSTechModelRaft", "Móng bè", "Raft", typeof(ModelRaftFoundationCommand), "icon-part-raft"),
            SmallPart(asm, "DSSTechModelPile", "Cọc", "Pile", typeof(ModelPileCommand), "icon-part-pile"));

        // ── Panel 5: MEPF ──────────────────────────────────────────────────
        var mepf = Panel(application, tab, "MEPF");
        mepf.AddStackedItems(
            SmallPart(asm, "DSSTechModelPipe", "Ống nước", "Pipe", typeof(ModelPipeCommand), "icon-part-pipe"),
            SmallPart(asm, "DSSTechModelDuct", "Ống gió", "Duct", typeof(ModelDuctCommand), "icon-part-duct"));
        mepf.AddStackedItems(
            SmallPart(asm, "DSSTechModelCableTray", "Máng cáp", "Cable Tray", typeof(ModelCableTrayCommand), "icon-part-tray"),
            SmallPart(asm, "DSSTechModelEquipment", "Thiết bị", "Equipment", typeof(ModelEquipmentCommand), "icon-part-equipment"));

        // ── Panel 6: Khối lượng ────────────────────────────────────────────
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

        // ── Panel 7: Trợ lý AI ─────────────────────────────────────────────
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

        // Giao diện sáng / tối của Revit (2024+): đổi bộ icon cho hợp nền, kể cả khi người dùng đổi theme lúc đang mở.
        RibbonState.Application = application;
        try
        {
            application.ThemeChanged += (_, _) => RibbonState.RefreshTheme();
        }
        catch (Exception ex)
        {
            DssTechLog.Write($"ribbon theme hook: {ex.Message}");
        }

        return Result.Succeeded;
    }

    private static RibbonPanel Panel(UIControlledApplication application, string tab, string name) =>
        application.GetRibbonPanels(tab).FirstOrDefault(x => x.Name == name)
        ?? application.CreateRibbonPanel(tab, name);

    /// <summary>Nút module: mở cửa sổ Dựng mô hình với loại cấu kiện chọn sẵn.</summary>
    private static PushButtonData Part(string asm, string id, string vi, string en, Type command, string icon) =>
        Large(asm, id, vi, en, command, icon, $"Dựng {vi.ToLowerInvariant()} từ CAD link: chọn family, level, offset rồi dựng.",
            $"Build {en.ToLowerInvariant()} from the CAD link: pick family, level and offset.", "dung-mo-hinh");

    private static PushButtonData SmallPart(string asm, string id, string vi, string en, Type command, string icon) =>
        Small(asm, id, vi, en, command, icon, "Dựng từ CAD link: chọn family, level, offset rồi dựng.",
            $"Build {en.ToLowerInvariant()} from the CAD link: pick family, level and offset.", "dung-mo-hinh");

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
        RibbonState.IconById[id] = icon;
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

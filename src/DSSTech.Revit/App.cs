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
        setup.AddItem(Large(asm, "DSSTechAbout", "Giới thiệu", "About", typeof(AboutCommand), "icon-help",
            "Phiên bản, bản quyền trên máy này (người dùng, hạn, mã máy); mở Tài khoản và Hướng dẫn.",
            "Version and this machine's license (holder, expiry, machine code); opens Account and Help.", ""));
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
        arch.AddItem(Large(asm, "DSSTechCadDatum", "Trục", "Grids", typeof(ModelRevit.CadDatumCommand), "icon-part-grid",
            "Chọn CAD link: tự đọc lưới trục (nét trục + vòng tròn tên trục, mọi layer), khoảng cách chẵn 5 mm, kèm kích thước trục.",
            "Pick the CAD link: grids read from grid lines with named bubbles on any layer, even 5 mm spacing, with grid dimensions.", "truc-tang"));
        arch.AddItem(Large(asm, "DSSTechModelLevel", "Level", "Levels", typeof(ModelRevit.ModelLevelCommand), "icon-level",
            "Bảng khai báo tầng: số tầng và chiều cao từng tầng; tạo/cập nhật level và mặt bằng.",
            "Storey table: number of storeys and each height; creates or updates levels and floor plans.", "truc-tang"));
        arch.AddSeparator();
        arch.AddItem(Part(asm, "DSSTechModelArchWall", "Tường", "Wall", typeof(ModelArchitecturalWallCommand), "icon-part-archwall"));
        arch.AddItem(Part(asm, "DSSTechModelDoor", "Cửa", "Door", typeof(ModelDoorCommand), "icon-part-door"));
        arch.AddItem(Part(asm, "DSSTechModelWindow", "Cửa sổ", "Window", typeof(ModelWindowCommand), "icon-part-window"));
        // Walls after they are built: fit to structure, lintels, openings.
        arch.AddStackedItems(
            Small(asm, "DSSTechWallFit", "Khớp kết cấu", "Fit to Structure", typeof(ModelRevit.ModelWallFitCommand), "icon-part-wall",
                "Tường thô cắt tại mặt cột/vách và hạ đỉnh tới đáy dầm/sàn (kết cấu trong model và file link).",
                "Rough walls cut at column/wall faces and topped at the beam/slab underside (this model and linked structure).", "dung-mo-hinh"),
            Small(asm, "DSSTechModelLintel", "Lanh tô", "Lintel", typeof(ModelRevit.ModelLintelCommand), "icon-part-beam",
                "Lanh tô và bổ trụ theo cửa đã dựng trên tường xây của tầng đang mở.",
                "Lintels and stiffener columns for the doors and windows on masonry walls of this level.", "dung-mo-hinh"),
            Small(asm, "DSSTechModelWallOpening", "Lỗ mở", "Wall Openings", typeof(ModelRevit.ModelWallOpeningCommand), "icon-part-window",
                "Đục lỗ tường tại ống gió xuyên tường và tủ PCCC âm tường (model này và file link MEP).",
                "Cut wall openings for ducts crossing walls and recessed fire-hose cabinets (this model and linked MEP).", "dung-mo-hinh"));
        // Rooms and what finishes them.
        arch.AddStackedItems(
            SmallPart(asm, "DSSTechModelRoom", "Phòng", "Room", typeof(ModelRoomCommand), "icon-part-room"),
            Small(asm, "DSSTechModelFinishWall", "Hoàn thiện", "Room Finishes", typeof(ModelRevit.ModelFinishWallCommand), "icon-part-archwall",
                "Hoàn thiện phòng: tường hoàn thiện (hoặc chân tường) và sàn hoàn thiện theo phòng đang chọn hoặc cả tầng.",
                "Room finishes: finish walls (or skirting) and finish floors for the selected rooms or the whole level.", "dung-mo-hinh"),
            SmallPart(asm, "DSSTechModelCeiling", "Trần", "Ceiling", typeof(ModelCeilingCommand), "icon-part-ceiling"));

        // ── Panel 3: Kết cấu ───────────────────────────────────────────────
        var structure = Panel(application, tab, "Kết cấu · Structure");
        structure.AddItem(Part(asm, "DSSTechModelColumn", "Cột", "Column", typeof(ModelColumnCommand), "icon-part-column"));
        structure.AddItem(Part(asm, "DSSTechModelStructuralWall", "Vách", "RC Wall", typeof(ModelStructuralWallCommand), "icon-part-wall"));
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
        mepf.AddItem(Large(asm, "DSSTechMepSystems", "Hệ thống", "Systems", typeof(ModelRevit.MepSystemSetupCommand), "icon-mep-systems",
            "Tạo / đồng bộ system type ống gió, ống nước theo quy định màu; báo hệ đã có hoặc tương tự để không tạo trùng.",
            "Create or sync duct and pipe system types from the colour rules; flags existing or similar systems to avoid duplicates.", "dung-mo-hinh"));
        // Each discipline drops down to the kind of run; each then asks for the CAD link, the system and its layers.
        const string mepTip = " Chọn CAD, chọn system và tick layer của hệ (CAD có nhiều hệ); size theo chữ ghi cạnh nét, dưới đáy dầm/sàn và uốn né dầm.";
        const string mepTipEn = " Pick the CAD link, the system and its layers; size from labels, under beams and slabs with drops.";
        Menu(mepf.AddItem(Pulldown("DSSTechMepMech", "ĐHKK\nHVAC", "icon-mep-mech",
                "ĐHKK (HVAC) — ống gió, ống nước ĐHKK, thoát nước ngưng." + mepTip, "Ducts, mechanical pipes, condensate drains." + mepTipEn)),
            Item(asm, "DSSTechMepDuct", "Ống gió · Duct", typeof(ModelRevit.MepDuctCommand), "icon-part-duct",
                "Cấp, hồi, gió tươi, thải (Duct)." + mepTip, "Supply, return, fresh air, exhaust ducts." + mepTipEn, "dung-mo-hinh"),
            Item(asm, "DSSTechMepMechPipe", "Ống nước ĐHKK · HVAC Pipe", typeof(ModelRevit.MepMechPipeCommand), "icon-mep-chw",
                "Nước Chiller, tháp giải nhiệt, gas (Pipe)." + mepTip, "Chilled, condenser water and refrigerant pipes." + mepTipEn, "dung-mo-hinh"),
            Item(asm, "DSSTechMepCondensate", "Thoát nước ngưng · Condensate", typeof(ModelRevit.MepCondensateCommand), "icon-mep-condensate",
                "Ống thoát nước ngưng có độ dốc về điểm xả." + mepTip, "Condensate drains falling to their outlet." + mepTipEn, "dung-mo-hinh"),
            Item(asm, "DSSTechHvacDevices", "Thiết bị ĐHKK · Air Devices", typeof(ModelRevit.HvacDevicesCommand), "icon-mep-terminal",
                "Miệng gió, FCU, box gió từ block CAD: theo cao độ trần phía trên (hoặc cao độ chỉ định), rồi tự nối ống mềm tới ống gió cùng hệ.",
                "Grilles, fan coils and air boxes from CAD blocks at the ceiling (or a typed height), joined to the ducts by flex duct.", "dung-mo-hinh"),
            Item(asm, "DSSTechFlexConnect", "Nối ống mềm · Flex Connect", typeof(ModelRevit.FlexConnectCommand), "icon-mep-flex",
                "Nối ống mềm từ miệng gió / FCU / box đang chọn (hoặc mọi thiết bị chưa nối của tầng) tới ống gió cùng hệ gần nhất.",
                "Flex duct from the selected (or every unconnected) air devices of the level to the nearest duct of their system.", "dung-mo-hinh"));
        mepf.AddItem(Large(asm, "DSSTechMepElec", "Điện", "Electrical", typeof(ModelRevit.MepElectricalCommand), "icon-mep-elec",
            "Máng / thang cáp, hệ ghi vào Service Type." + mepTip,
            "Cable trays, system as Service Type." + mepTipEn, "dung-mo-hinh"));
        var plumbing = mepf.AddItem(Pulldown("DSSTechMepPlumb", "Nước\nPlumbing", "icon-mep-water",
            "Nước (Plumbing) — cấp nước dưới đáy dầm/sàn; thoát nước có độ dốc về điểm xả và nối vào ống có sẵn.",
            "Water supply under beams and slabs; drainage falling to its outlet and joined to the pipe there."));
        var fire = mepf.AddItem(Pulldown("DSSTechMepFire", "PCCC\nFire", "icon-mep-fire",
            "PCCC (Fire Protection) — sprinkler (Pipe) và tăng áp / hút khói (Duct).",
            "Sprinkler pipes and pressurisation / smoke exhaust ducts."));
        Menu(plumbing,
            Item(asm, "DSSTechMepSupply", "Cấp nước · Water Supply", typeof(ModelRevit.MepPlumbingCommand), "icon-mep-supply",
                "Nước lạnh, nóng, hồi nóng (Pipe)." + mepTip, "Cold, hot and hot return water." + mepTipEn, "dung-mo-hinh"),
            Item(asm, "DSSTechMepDrain", "Thoát nước · Drainage", typeof(ModelRevit.MepDrainCommand), "icon-mep-drain",
                "Thải đen, xám, bếp, mưa, thông hơi: độ dốc về điểm xả." + mepTip, "Soil, waste, kitchen, rain, vent: falling to the outlet." + mepTipEn, "dung-mo-hinh"));
        Menu(fire,
            Item(asm, "DSSTechMepSprinkler", "Sprinkler / chữa cháy · Sprinkler", typeof(ModelRevit.MepFireCommand), "icon-mep-sprinkler",
                "Sprinkler, họng vách tường, màn ngăn (Pipe)." + mepTip, "Sprinkler, hose and drencher pipes." + mepTipEn, "dung-mo-hinh"),
            Item(asm, "DSSTechMepSmoke", "Tăng áp / hút khói · Smoke Duct", typeof(ModelRevit.MepSmokeDuctCommand), "icon-mep-smoke",
                "Ống gió tạo áp, hút khói (Duct)." + mepTip, "Pressurisation and smoke exhaust ducts." + mepTipEn, "dung-mo-hinh"));
        Devices(mepf, asm, "DSSTechWallDevices", "Thiết bị tường", "Wall Devices", "icon-dev-wall",
            "Ổ cắm, công tắc, đèn ốp tường, điều hòa cục bộ từ block CAD: đặt lên mặt tường gần nhất ở cao độ lắp.",
            "Sockets, switches, wall lights and split AC units from CAD blocks, on the nearest wall face at mounting height.",
            Item(asm, "DSSTechWallDevicesAll", "Tất cả thiết bị tường", typeof(ModelRevit.WallDevicesCommand), "icon-dev-wall",
                "Mọi block thiết bị tường trong CAD link, mỗi block gán một family.", "Every wall device block in the CAD link.", "dung-mo-hinh"),
            Item(asm, "DSSTechSocket", "Ổ cắm", typeof(ModelRevit.SocketCommand), "icon-dev-socket",
                "Ổ cắm trên mặt tường, mặc định cao 400 mm.", "Sockets on the wall face, 400 mm by default.", "dung-mo-hinh"),
            Item(asm, "DSSTechSwitch", "Công tắc", typeof(ModelRevit.SwitchCommand), "icon-dev-switch",
                "Công tắc trên mặt tường, mặc định cao 1250 mm.", "Switches on the wall face, 1250 mm by default.", "dung-mo-hinh"),
            Item(asm, "DSSTechWallLight", "Đèn ốp tường", typeof(ModelRevit.WallLightCommand), "icon-dev-walllight",
                "Đèn ốp tường, mặc định cao 2200 mm.", "Wall lights, 2200 mm by default.", "dung-mo-hinh"),
            Item(asm, "DSSTechSplitAc", "Điều hòa cục bộ", typeof(ModelRevit.SplitAcCommand), "icon-dev-splitac",
                "Dàn lạnh treo tường, mặc định cao 2500 mm.", "Wall-mounted indoor units, 2500 mm by default.", "dung-mo-hinh"));
        Devices(mepf, asm, "DSSTechCeilingDevices", "Thiết bị trần", "Ceiling Devices", "icon-dev-ceiling",
            "Đèn trần, đầu phun sprinkler, đầu báo cháy từ block CAD: theo cao độ trần phía trên từng thiết bị.",
            "Ceiling lights, sprinklers and fire detectors from CAD blocks, at the ceiling over each one.",
            Item(asm, "DSSTechCeilingDevicesAll", "Tất cả thiết bị trần", typeof(ModelRevit.CeilingDevicesCommand), "icon-dev-ceiling",
                "Mọi block thiết bị trần trong CAD link, mỗi block gán một family.", "Every ceiling device block in the CAD link.", "dung-mo-hinh"),
            Item(asm, "DSSTechCeilingLight", "Đèn trần", typeof(ModelRevit.CeilingLightCommand), "icon-dev-ceilinglight",
                "Đèn gắn vào mặt trần phía trên.", "Lights on the ceiling above.", "dung-mo-hinh"),
            Item(asm, "DSSTechSprinkler", "Đầu phun sprinkler", typeof(ModelRevit.SprinklerCommand), "icon-dev-sprinkler",
                "Đầu phun theo cao độ trần phía trên.", "Sprinkler heads at the ceiling above.", "dung-mo-hinh"),
            Item(asm, "DSSTechFireAlarm", "Đầu báo cháy", typeof(ModelRevit.FireAlarmCommand), "icon-dev-firealarm",
                "Đầu báo khói / nhiệt gắn vào mặt trần.", "Smoke / heat detectors on the ceiling.", "dung-mo-hinh"));

        // ── Combine: phối hợp hệ thống sau khi dựng các bộ môn ─────────────
        var combine = Panel(application, tab, "Combine");
        combine.AddItem(Build(asm, "DSSTechCombineAvoid", "Combine", typeof(ModelRevit.MepAvoidCommand), "icon-combine",
            "Chọn ống gió, ống nước, máng cáp ở khu vực va chạm; chọn loại phải né và đi dưới / đi trên: tự đổi tuyến bằng cút.",
            "Select the runs at a clash; pick which kind gives way and whether it passes under or over: it detours with elbows.", "dung-mo-hinh"));
        combine.AddItem(Build(asm, "DSSTechClashCheck", "Clash Check", typeof(ModelRevit.ClashCheckCommand), "icon-clash",
            "Kiểm tra va chạm nhóm A (model này) với nhóm B (model này và file link), rồi xem, sửa, tạo view, xuất BCF / Navisworks / Excel.",
            "Clash set A (this model) against set B (this model and links); review, fix, make views, export BCF / Navisworks / Excel.", "dung-mo-hinh"));
        combine.AddItem(Large(asm, "DSSTechDrawingCompare", "So sánh", "Compare", typeof(ModelRevit.DrawingCompareCommand), "icon-compare",
            "So mô hình với bản vẽ (link hoặc bản cập nhật chưa link): thiếu, thừa, lệch, khác size; hoặc so bản vẽ cũ với bản mới. Đám mây, tô màu, dời theo bản vẽ, Excel.",
            "Model against the drawing (linked or a new revision), or old drawing against new: missing, extra, moved, changed; clouds, colours, moves, Excel.", "dung-mo-hinh"));
        combine.AddStackedItems(
            Small(asm, "DSSTechMepRaise", "Nâng sát dầm", "Raise to Structure", typeof(ModelRevit.MepRaiseCommand), "icon-raise",
                "Nâng ống / máng đang chọn (hoặc cả một loại trên tầng) lên sát đáy dầm/sàn − 50 mm; mạng nối liền nâng cùng mức.",
                "Raise the selected runs (or one kind on the level) to the beam/slab underside less 50 mm, each network as one.", "dung-mo-hinh"),
            Small(asm, "DSSTechClashResults", "Xung đột", "Clash Results", typeof(ModelRevit.ClashResultsCommand), "icon-clash-results",
                "Mở lại kết quả check clash gần nhất: xem, sửa, tạo view 3D, xuất BCF, viewpoint Navisworks, Excel.",
                "Reopen the last clash results: review, fix, 3D views, BCF, Navisworks viewpoints, Excel.", "dung-mo-hinh"));

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

        // ── Tài sản (FM / vận hành): Form BIM Excel + catalogue PDF → TS_* trên Type ─
        var assets = Panel(application, tab, "Tài sản · Assets");
        assets.AddItem(Large(asm, "DSSTechAssetInfo", "Tài sản", "Asset Data", typeof(AssetInfo.AssetInfoImportCommand), "icon-asset",
            "Nhập thông tin tài sản từ Form BIM (Excel) và catalogue PDF vào tham số Type TS_*: xem trước, khớp Type, chỉ ghi sau khi xác nhận.",
            "Import asset data from the BIM form (Excel) and catalogue PDFs into TS_* type parameters: preview, match types, write on confirm.", "tai-san"));

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
            // Without a license the AI gate stays shut; the AI button turns it on once a key is activated.
            if (License.LicenseService.IsValid)
                McpStartupNotice.ShowIfNeeded();
            else
            {
                McpHostService.Instance.Stop();
                application.ControlledApplication.ApplicationInitialized += (_, _) =>
                    TaskDialog.Show("DSSTech", "DSSTech chưa được kích hoạt trên máy này (" + License.LicenseService.Reason + ").\n" +
                                               "Vào tab DSSTech ▸ Thiết lập ▸ Tài khoản: gửi mã máy cho DSSTech để nhận key, dán key rồi bấm Kích hoạt.");
            }
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

    /// <summary>Nút to xổ xuống: mỗi mục là một modul thiết bị riêng.</summary>
    private static void Devices(RibbonPanel panel, string asm, string id, string vi, string en, string icon, string tipVi, string tipEn,
        params PushButtonData[] items)
    {
        RibbonState.IconById[id] = icon;
        var data = new PulldownButtonData(id, $"{vi}\n{en}")
        {
            LargeImage = RibbonIcons.Load(icon, 32),
            Image = RibbonIcons.Load(icon, 16),
            ToolTip = tipVi,
            LongDescription = tipEn
        };
        if (panel.AddItem(data) is PulldownButton pulldown)
        {
            foreach (var item in items)
                pulldown.AddPushButton(item);
        }
    }

    /// <summary>Nút xổ xuống (to, hoặc nhỏ trong cột xếp chồng): các mục thêm bằng <see cref="Menu"/>.</summary>
    private static PulldownButtonData Pulldown(string id, string label, string icon, string tipVi, string tipEn)
    {
        RibbonState.IconById[id] = icon;
        return new PulldownButtonData(id, label)
        {
            LargeImage = RibbonIcons.Load(icon, 32),
            Image = RibbonIcons.Load(icon, 16),
            ToolTip = tipVi,
            LongDescription = tipEn
        };
    }

    private static void Menu(RibbonItem? item, params PushButtonData[] items)
    {
        if (item is PulldownButton pulldown)
        {
            foreach (var data in items)
                pulldown.AddPushButton(data);
        }
    }

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
            Image = RibbonIcons.Load(icon, 16),
            // Bản quyền: mọi nút cần key, trừ Tài khoản (để kích hoạt) và Hướng dẫn.
            AvailabilityClassName = id is "DSSTechAccount" or "DSSTechHelp" or "DSSTechAbout"
                ? typeof(License.AlwaysAvailability).FullName
                : typeof(License.LicensedAvailability).FullName
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

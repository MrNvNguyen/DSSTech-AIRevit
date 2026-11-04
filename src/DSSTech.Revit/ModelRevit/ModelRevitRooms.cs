using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Dss.Cad2Bim.Core;
using Dss.McpHost.Create;

namespace DSSTech.Revit.ModelRevit;

/// <summary>
/// Rooms the way Revit places them, named from the CAD: open passages between walls are closed with room
/// separation lines, every enclosed zone of the level gets a room (Place Rooms), and each room-name text of the
/// drawing names the room it lies in (name, number). Zones with no name on the CAD (shafts, wall cavities) get no
/// room; a name outside any enclosed zone still gets a room there, reported as not enclosed.
/// </summary>
internal static class ModelRevitRooms
{
    public static string Run(Document doc, View view, Level level, CadGeometry geom, IReadOnlyCollection<string> skipMarks)
    {
        var labels = RoomPlanner.Scan(geom.Entities.OfType<CadText>(), skipMarks);
        var walls = ModelRevitHostWalls.At(doc, level.Name);
        var separations = ModelRevitRoomBounds.CloseGaps(doc, view, level.Name, walls);
        foreach (var label in labels)
            DssTechLog.Write($"room label '{label.Name}' {label.Number} at ({label.X:0},{label.Y:0})");

        var placedNew = 0;
        var named = new HashSet<ElementId>();
        var twice = new List<string>();
        var open = new List<string>();
        var dropped = 0;
        using (var tx = new Transaction(doc, "DSSTech rooms from CAD"))
        {
            tx.Start();
            var before = Rooms(doc, level).Select(r => r.Id).ToHashSet();
            var phase = doc.GetElement(view.get_Parameter(BuiltInParameter.VIEW_PHASE)?.AsElementId() ?? ElementId.InvalidElementId) as Phase;
            try
            {
                placedNew = (phase is not null ? doc.Create.NewRooms2(level, phase) : doc.Create.NewRooms2(level)).Count;
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException ex)
            {
                DssTechLog.Write($"place rooms: {ex.Message}");
            }

            doc.Regenerate();
            var rooms = Rooms(doc, level).Where(r => r.Area > 0).ToList();
            var probeZ = level.Elevation + UnitConv.MmToInternal(1000);
            foreach (var label in labels)
            {
                var at = new XYZ(UnitConv.MmToInternal(label.X), UnitConv.MmToInternal(label.Y), probeZ);
                var room = rooms.FirstOrDefault(r => r.IsPointInRoom(at));
                if (room is null)
                {
                    // Named on the CAD but not enclosed by walls: the room still goes there.
                    var loose = doc.Create.NewRoom(level, new UV(at.X, at.Y));
                    Name(loose, label);
                    named.Add(loose.Id);
                    open.Add(Describe(label));
                    continue;
                }

                if (!named.Add(room.Id))
                {
                    // A second name in one zone: the zone holds two rooms on the CAD with no wall between.
                    twice.Add($"{Describe(label)} (cùng vùng với {room.Name})");
                    continue;
                }

                Name(room, label);
            }

            // Zones with no name on the CAD placed by this run: shafts, cavities between walls.
            foreach (var room in Rooms(doc, level).Where(r => !before.Contains(r.Id) && !named.Contains(r.Id)).ToList())
            {
                doc.Delete(room.Id);
                dropped++;
            }

            tx.Commit();
        }

        var summary = new StringBuilder();
        summary.AppendLine($"Level {level.Name}: tên phòng quét được trên CAD {labels.Count} (mọi layer: chữ có từ PHÒNG, WC, BẾP, SẢNH, HÀNH LANG, KHO… hoặc nằm trên layer tên phòng).");
        summary.AppendLine($"Khe hở thẳng hàng giữa hai tường (≤ {RoomPlanner.MaxGapMm:0} mm) khép bằng Room Separation: {separations} nét mới.");
        summary.AppendLine($"Place Rooms: {placedNew} vùng kín mới; gán tên/số theo CAD: {named.Count}; bỏ {dropped} vùng không có tên trên CAD (hộp kỹ thuật, khe tường).");
        summary.AppendLine("Số phòng: mã cạnh tên hoặc đứng đầu tên (101 PHÒNG NGỦ, P.101); không có thì giữ số Revit tự đánh.");
        if (twice.Count > 0)
            summary.AppendLine($"Hai tên trong một vùng (giữa chúng chưa có tường/nét chia): {string.Join("; ", twice)}.");
        if (open.Count > 0)
            summary.AppendLine($"Có tên nhưng chưa khép kín (Not Enclosed): {string.Join("; ", open)}.");
        return summary.ToString();
    }

    private static void Name(Room room, RoomLabel label)
    {
        room.Name = label.Name;
        if (!string.IsNullOrWhiteSpace(label.Number))
            room.Number = label.Number;
    }

    private static string Describe(RoomLabel label) =>
        string.IsNullOrWhiteSpace(label.Number) ? label.Name : $"{label.Number} {label.Name}";

    private static List<Room> Rooms(Document doc, Level level) =>
        new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Rooms).WhereElementIsNotElementType()
            .OfType<Room>()
            .Where(r => r.LevelId == level.Id && r.Location is not null)
            .ToList();
}

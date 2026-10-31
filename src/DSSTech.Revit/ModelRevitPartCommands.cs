using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Dss.Cad2Bim.Core;

namespace DSSTech.Revit;

/// <summary>Nút ribbon theo module: mở cửa sổ Dựng mô hình với ô Loại chọn sẵn, logic dựng giữ nguyên.</summary>
public abstract class ModelRevitPartCommand : IExternalCommand
{
    protected abstract ModelRevitPartKind Part { get; }

    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements) =>
        ModelRevitCommand.Run(commandData.Application, presetPart: Part);
}

[Transaction(TransactionMode.Manual)]
public sealed class ModelColumnCommand : ModelRevitPartCommand
{
    protected override ModelRevitPartKind Part => ModelRevitPartKind.Column;
}

[Transaction(TransactionMode.Manual)]
public sealed class ModelStructuralWallCommand : ModelRevitPartCommand
{
    protected override ModelRevitPartKind Part => ModelRevitPartKind.StructuralWall;
}

[Transaction(TransactionMode.Manual)]
public sealed class ModelBeamCommand : ModelRevitPartCommand
{
    protected override ModelRevitPartKind Part => ModelRevitPartKind.Beam;
}

[Transaction(TransactionMode.Manual)]
public sealed class ModelFloorCommand : ModelRevitPartCommand
{
    protected override ModelRevitPartKind Part => ModelRevitPartKind.Floor;
}

[Transaction(TransactionMode.Manual)]
public sealed class ModelPileCapCommand : ModelRevitPartCommand
{
    protected override ModelRevitPartKind Part => ModelRevitPartKind.PileCap;
}

[Transaction(TransactionMode.Manual)]
public sealed class ModelIsolatedFoundationCommand : ModelRevitPartCommand
{
    protected override ModelRevitPartKind Part => ModelRevitPartKind.IsolatedFoundation;
}

[Transaction(TransactionMode.Manual)]
public sealed class ModelStripFoundationCommand : ModelRevitPartCommand
{
    protected override ModelRevitPartKind Part => ModelRevitPartKind.StripFoundation;
}

[Transaction(TransactionMode.Manual)]
public sealed class ModelRaftFoundationCommand : ModelRevitPartCommand
{
    protected override ModelRevitPartKind Part => ModelRevitPartKind.RaftFoundation;
}

[Transaction(TransactionMode.Manual)]
public sealed class ModelPileCommand : ModelRevitPartCommand
{
    protected override ModelRevitPartKind Part => ModelRevitPartKind.Pile;
}

[Transaction(TransactionMode.Manual)]
public sealed class ModelArchitecturalWallCommand : ModelRevitPartCommand
{
    protected override ModelRevitPartKind Part => ModelRevitPartKind.ArchitecturalWall;
}

[Transaction(TransactionMode.Manual)]
public sealed class ModelDoorCommand : ModelRevitPartCommand
{
    protected override ModelRevitPartKind Part => ModelRevitPartKind.Door;
}

[Transaction(TransactionMode.Manual)]
public sealed class ModelWindowCommand : ModelRevitPartCommand
{
    protected override ModelRevitPartKind Part => ModelRevitPartKind.Window;
}

[Transaction(TransactionMode.Manual)]
public sealed class ModelRoomCommand : ModelRevitPartCommand
{
    protected override ModelRevitPartKind Part => ModelRevitPartKind.Room;
}

[Transaction(TransactionMode.Manual)]
public sealed class ModelCeilingCommand : ModelRevitPartCommand
{
    protected override ModelRevitPartKind Part => ModelRevitPartKind.Ceiling;
}

[Transaction(TransactionMode.Manual)]
public sealed class ModelPipeCommand : ModelRevitPartCommand
{
    protected override ModelRevitPartKind Part => ModelRevitPartKind.Pipe;
}

[Transaction(TransactionMode.Manual)]
public sealed class ModelDuctCommand : ModelRevitPartCommand
{
    protected override ModelRevitPartKind Part => ModelRevitPartKind.Duct;
}

[Transaction(TransactionMode.Manual)]
public sealed class ModelCableTrayCommand : ModelRevitPartCommand
{
    protected override ModelRevitPartKind Part => ModelRevitPartKind.CableTray;
}

[Transaction(TransactionMode.Manual)]
public sealed class ModelEquipmentCommand : ModelRevitPartCommand
{
    protected override ModelRevitPartKind Part => ModelRevitPartKind.Equipment;
}

/// <summary>Module Level: giữ chỗ trên ribbon, logic dựng Level sẽ bổ sung sau.</summary>
[Transaction(TransactionMode.Manual)]
public sealed class ModelLevelCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        TaskDialog.Show("DSSTech", "Module Level chưa có chức năng.");
        return Result.Cancelled;
    }
}

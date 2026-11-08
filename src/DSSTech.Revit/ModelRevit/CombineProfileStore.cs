using System.IO;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using Dss.Cad2Bim.Core;
using Dss.McpHost.Create;

namespace DSSTech.Revit.ModelRevit;

/// <summary>
/// Where the Combine rules live: the built-in default, then the company file
/// (%APPDATA%\DSSTech\combine-profile.json), then the project file beside the .rvt (DSSTech-combine.json).
/// </summary>
internal static class CombineProfileStore
{
    public static string CompanyFile =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DSSTech", "combine-profile.json");

    public static string? ProjectFile(Document doc) =>
        string.IsNullOrEmpty(doc.PathName)
            ? null
            : Path.Combine(Path.GetDirectoryName(doc.PathName) ?? "", "DSSTech-combine.json");

    public static CombineProfile Load(Document doc, List<string>? errors = null)
    {
        var files = new List<string> { CompanyFile };
        if (ProjectFile(doc) is { } project)
            files.Add(project);
        return CombineProfile.Load(files, errors);
    }

    /// <summary>The file edits go to: the project's when it has one, else the company file (made from the default).</summary>
    public static string EditableFile(Document doc)
    {
        var project = ProjectFile(doc);
        if (project is not null && File.Exists(project))
            return project;
        if (!File.Exists(CompanyFile))
            CombineProfile.Default().Save(CompanyFile);
        return CompanyFile;
    }

    /// <summary>The system group a run (or fitting, accessory, sprinkler) belongs to (C1).</summary>
    public static MepSystemCode System(Element e) => Identify(e).System;

    /// <summary>Company system code (SAD, CWS…; LPG, CND), "" when the element carries none.</summary>
    public static string Code(Element e) => Identify(e).Code;

    /// <summary>The group and the company system code, read from the system type, system, abbreviation, type name.</summary>
    public static (MepSystemCode System, string Code) Identify(Element e)
    {
        string category = e switch
        {
            Duct or FlexDuct => "Duct",
            Pipe or FlexPipe => "Pipe",
            CableTray => "CableTray",
            Conduit => "Conduit",
            _ => (e.Category is null ? BuiltInCategory.INVALID : (BuiltInCategory)e.Category.Id.Value) switch
            {
                BuiltInCategory.OST_DuctFitting or BuiltInCategory.OST_DuctAccessory or BuiltInCategory.OST_DuctTerminal => "Duct",
                BuiltInCategory.OST_PipeFitting or BuiltInCategory.OST_PipeAccessory or BuiltInCategory.OST_PlumbingFixtures => "Pipe",
                BuiltInCategory.OST_Sprinklers => "Sprinkler",
                BuiltInCategory.OST_CableTrayFitting => "CableTray",
                BuiltInCategory.OST_ConduitFitting => "Conduit",
                _ => ""
            }
        };
        string? classification = null, systemType = null, systemName = null, abbreviation = null, service = null;
        try
        {
            classification = e.get_Parameter(BuiltInParameter.RBS_SYSTEM_CLASSIFICATION_PARAM)?.AsString();
            systemName = e.get_Parameter(BuiltInParameter.RBS_SYSTEM_NAME_PARAM)?.AsString();
            abbreviation = e.get_Parameter(BuiltInParameter.RBS_SYSTEM_ABBREVIATION_PARAM)?.AsString();
            service = e.get_Parameter(BuiltInParameter.RBS_CTC_SERVICE_TYPE)?.AsString();
            var typeId = e.get_Parameter(BuiltInParameter.RBS_PIPING_SYSTEM_TYPE_PARAM)?.AsElementId()
                         ?? e.get_Parameter(BuiltInParameter.RBS_DUCT_SYSTEM_TYPE_PARAM)?.AsElementId();
            if (typeId is not null && typeId != ElementId.InvalidElementId && e.Document.GetElement(typeId) is MEPSystemType st)
            {
                systemType = st.Name;
                abbreviation ??= st.Abbreviation;
                if (st is PipingSystemType pst)
                    classification ??= pst.SystemClassification.ToString();
            }
        }
        catch (Autodesk.Revit.Exceptions.ApplicationException)
        {
            // Read what there is.
        }

        var typeName = e.Document.GetElement(e.GetTypeId())?.Name;
        var code = MepSystemClassifier.Code(category, abbreviation, systemType, systemName, service, typeName);
        if (category == "Sprinkler")
            return (MepSystemCode.FP, code);
        var system = MepSystemClassifier.Classify(category, classification, systemType, systemName, abbreviation, typeName);
        // A run named by a code of another group (a pipe typed "CWS" in a duct list…) keeps its category's group.
        return (system, code.Length > 0 && MepSystemClassifier.Family(code) != system ? "" : code);
    }

    public static string Label(Element e)
    {
        var (system, code) = Identify(e);
        return MepSystemClassifier.Label(system, code);
    }

    /// <summary>A straight run in Combine's terms (mm), insulation included (modelled, else the profile's).</summary>
    public static CombineRun? Run(MEPCurve c, CombineProfile profile)
    {
        if (c.Location is not LocationCurve { Curve: Line line })
            return null;
        var (sys, code) = Identify(c);
        var insulation = 0.0;
        try
        {
            foreach (var id in InsulationLiningBase.GetInsulationIds(c.Document, c.Id))
            {
                if (c.Document.GetElement(id) is InsulationLiningBase ins)
                    insulation = Math.Max(insulation, UnitConv.ToMm(ins.Thickness));
            }
        }
        catch (Autodesk.Revit.Exceptions.ApplicationException)
        {
            // No insulation API for this run.
        }

        if (insulation == 0)
            insulation = profile.Insulation(sys, code);
        var a = line.GetEndPoint(0);
        var b = line.GetEndPoint(1);
        return new CombineRun
        {
            Id = c.Id.Value,
            System = sys,
            Code = code,
            Ax = UnitConv.ToMm(a.X), Ay = UnitConv.ToMm(a.Y), Az = UnitConv.ToMm(a.Z),
            Bx = UnitConv.ToMm(b.X), By = UnitConv.ToMm(b.Y), Bz = UnitConv.ToMm(b.Z),
            WidthMm = UnitConv.ToMm(MepCombine.Width(c)) + 2 * insulation,
            DepthMm = UnitConv.ToMm(MepCombine.Depth(c)) + 2 * insulation,
            Locked = c.Pinned || profile.IsLocked(sys, code),
            Name = $"{MepSystemClassifier.Label(sys, code)} {c.Id.Value}"
        };
    }
}

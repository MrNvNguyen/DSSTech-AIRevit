using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;

namespace DSSTech.Revit.ModelRevit;

/// <summary>
/// Runs left with System Type "Undefined" (Revit drops it from a run whose network was cut and joined again: a run
/// drawn through a junction, a piece deleted into it) given one back: the system of a run joined to it through its
/// fittings, else of the most runs of its type among those given, else the fallback.
/// </summary>
internal static class MepSystemFill
{
    public static ElementId SystemType(Element e) =>
        (e is Duct or FlexDuct
            ? e.get_Parameter(BuiltInParameter.RBS_DUCT_SYSTEM_TYPE_PARAM)
            : e.get_Parameter(BuiltInParameter.RBS_PIPING_SYSTEM_TYPE_PARAM))?.AsElementId() ?? ElementId.InvalidElementId;

    public static bool Undefined(Element e) =>
        e is Pipe or Duct or FlexPipe or FlexDuct && SystemType(e) == ElementId.InvalidElementId;

    /// <summary>
    /// Call outside a transaction (inside a group is fine): it makes its own. Returns how many runs were given a system.
    /// </summary>
    /// <param name="byType">False: only from a run joined to it (the runs given may be of other systems on one type).</param>
    public static int Fill(Document doc, IEnumerable<ElementId> ids, ElementId? fallback = null, bool byType = true)
    {
        var wanted = new Dictionary<ElementId, ElementId>();
        using (var tx = new Transaction(doc, "DSSTech MEP system"))
        {
            tx.Start();
            var runs = ids.Select(doc.GetElement).Where(e => e is { IsValidObject: true } && e is MEPCurve).Cast<MEPCurve>().ToList();
            var common = runs.Where(r => byType && !Undefined(r))
                .GroupBy(r => (r.GetTypeId(), r is Duct or FlexDuct))
                .ToDictionary(g => g.Key, g => g.GroupBy(SystemType).OrderByDescending(s => s.Count()).First().Key);
            foreach (var run in runs.Where(Undefined).ToList())
            {
                // One run given a system gives it to the network it is joined into: the rest of it is no longer Undefined.
                if (!Undefined(run))
                {
                    wanted[run.Id] = SystemType(run);
                    continue;
                }

                var system = Joined(run)
                             ?? (common.TryGetValue((run.GetTypeId(), run is Duct or FlexDuct), out var most) ? most : null)
                             ?? (fallback is { } f && f != ElementId.InvalidElementId ? f : null);
                if (system is null)
                    DssTechLog.Write($"mep system: #{run.Id.Value}: Undefined, no system to give it (none joined, none of its type)");
                else
                {
                    wanted[run.Id] = system;
                    Set(run, system);
                    doc.Regenerate();
                }
            }

            tx.Commit();
        }

        if (wanted.Count == 0)
            return 0;
        // A closed loop (runs and fittings only, nothing else joined): the system given holds until the commit, then
        // Revit drops it. The loop is opened at one joint, given the system, committed, and closed again (a loop that
        // has a system keeps it, as when a branch is joined to it).
        foreach (var kv in wanted)
        {
            if (doc.GetElement(kv.Key) is MEPCurve run && Undefined(run))
                Reopen(doc, run, kv.Value);
        }

        var left = wanted.Keys.Select(doc.GetElement).OfType<MEPCurve>().Where(Undefined).ToList();
        foreach (var run in left)
            DssTechLog.Write($"mep system: #{run.Id.Value} ({run.Name}) still Undefined; joined to {Refs(run)}");
        var filled = wanted.Count - left.Count;
        DssTechLog.Write($"mep system: {filled}/{wanted.Count} run(s) with System Type Undefined given one");
        return filled;
    }

    private static Parameter? Param(MEPCurve run) => run is Duct or FlexDuct
        ? run.get_Parameter(BuiltInParameter.RBS_DUCT_SYSTEM_TYPE_PARAM)
        : run.get_Parameter(BuiltInParameter.RBS_PIPING_SYSTEM_TYPE_PARAM);

    private static void Set(MEPCurve run, ElementId system)
    {
        try
        {
            // SetSystemType makes the run a system where it has none (a network of runs and fittings only, a closed
            // loop above all); setting the parameter does not, and Revit puts it back to Undefined.
            if (run is Pipe pipe)
                pipe.SetSystemType(system);
            else if (run is Duct duct)
                duct.SetSystemType(system);
            else if (Param(run) is not { } p)
                DssTechLog.Write($"mep system: #{run.Id.Value}: no System Type parameter");
            else if (p.IsReadOnly)
                DssTechLog.Write($"mep system: #{run.Id.Value}: System Type read-only while joined");
            else
                p.Set(system);
        }
        catch (Autodesk.Revit.Exceptions.ApplicationException ex)
        {
            DssTechLog.Write($"mep system: #{run.Id.Value}: {ex.Message}");
        }
    }

    private static void Reopen(Document doc, MEPCurve run, ElementId system)
    {
        var joint = run.ConnectorManager.Connectors.Cast<Connector>()
            .SelectMany(c => c.AllRefs.Cast<Connector>().Where(r => r.Owner is not MEPSystem && r.Owner.Id != run.Id)
                .Select(r => (Mine: c.Id, Owner: r.Owner.Id, Theirs: r.Id)))
            .Cast<(int Mine, ElementId Owner, int Theirs)?>().FirstOrDefault();
        if (joint is not { } j)
            return;
        try
        {
            using (var open = new Transaction(doc, "DSSTech MEP system"))
            {
                open.Start();
                Connector(doc, run.Id, j.Mine)!.DisconnectFrom(Connector(doc, j.Owner, j.Theirs)!);
                doc.Regenerate();
                Set(run, system);
                open.Commit();
            }

            using (var close = new Transaction(doc, "DSSTech MEP system"))
            {
                close.Start();
                var (a, b) = (Connector(doc, run.Id, j.Mine), Connector(doc, j.Owner, j.Theirs));
                if (a is not null && b is not null && !a.IsConnectedTo(b))
                    a.ConnectTo(b);
                close.Commit();
            }
        }
        catch (Exception ex) when (ex is Autodesk.Revit.Exceptions.ApplicationException or NullReferenceException)
        {
            DssTechLog.Write($"mep system: #{run.Id.Value}: reopen: {ex.Message}");
        }
    }

    private static Connector? Connector(Document doc, ElementId owner, int id)
    {
        var manager = doc.GetElement(owner) switch
        {
            MEPCurve c => c.ConnectorManager,
            FamilyInstance { MEPModel: { } m } => m.ConnectorManager,
            _ => null
        };
        return manager?.Connectors.Cast<Connector>().FirstOrDefault(c => c.Id == id);
    }

    private static string Refs(MEPCurve run) => string.Join(", ", run.ConnectorManager.Connectors.Cast<Connector>()
        .SelectMany(c => c.AllRefs.Cast<Connector>().Where(r => r.Owner.Id != run.Id)
            .Select(r => $"[{c.Id}] {r.Owner.Category?.Name} #{r.Owner.Id.Value} {(r.Owner is FamilyInstance fi ? fi.Symbol?.FamilyName : r.Owner.Name)} " +
                         $"{(r.Owner is MEPCurve o ? SystemType(o).Value.ToString() : "")}")));

    /// <summary>The system of the nearest run joined to this one through fittings and accessories.</summary>
    private static ElementId? Joined(MEPCurve run)
    {
        var seen = new HashSet<ElementId> { run.Id };
        var open = new Queue<Element>();
        open.Enqueue(run);
        while (open.Count > 0 && seen.Count < 200)
        {
            var e = open.Dequeue();
            if (e.Id != run.Id && e is MEPCurve && !Undefined(e))
                return SystemType(e);
            var manager = e switch
            {
                MEPCurve c => c.ConnectorManager,
                FamilyInstance { MEPModel: { } m } => m.ConnectorManager,
                _ => null
            };
            if (manager is null)
                continue;
            foreach (Connector c in manager.Connectors)
            {
                if (!c.IsConnected)
                    continue;
                foreach (Connector r in c.AllRefs)
                {
                    if (r.Owner is { } o && o is not MEPSystem && seen.Add(o.Id))
                        open.Enqueue(o);
                }
            }
        }

        return null;
    }
}

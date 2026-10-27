using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace DSSTech.Revit;

internal static class ControlCenterPane
{
    public static readonly Guid Id = new("7c2a9e14-5b6d-4f83-9a10-2d8e4c6b1f70");
}

internal sealed class ControlCenterPaneProvider : IDockablePaneProvider
{
    public void SetupDockablePane(DockablePaneProviderData data)
    {
        data.FrameworkElement = ControlCenterView.Instance;
        data.InitialState = new DockablePaneState
        {
            DockPosition = DockPosition.Right,
            MinimumWidth = 320
        };
        data.VisibleByDefault = false;
    }
}

internal static class ControlCenterHost
{
    private static readonly object Gate = new();
    private static readonly Queue<Action<UIApplication>> Pending = new();
    private static ExternalEvent? _event;

    public static void Install()
    {
        _event ??= ExternalEvent.Create(new Handler());
    }

    public static void Post(Action<UIApplication> action)
    {
        lock (Gate)
            Pending.Enqueue(action);
        _event?.Raise();
    }

    private sealed class Handler : IExternalEventHandler
    {
        public void Execute(UIApplication app)
        {
            while (true)
            {
                Action<UIApplication>? action;
                lock (Gate)
                {
                    if (Pending.Count == 0)
                        return;
                    action = Pending.Dequeue();
                }

                try
                {
                    action(app);
                }
                catch (Exception ex)
                {
                    TaskDialog.Show("DSSTech", ex.Message);
                }
            }
        }

        public string GetName() => "DSSTech Control Center";
    }
}

[Transaction(TransactionMode.Manual)]
public sealed class ControlCenterCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        var pane = commandData.Application.GetDockablePane(new DockablePaneId(ControlCenterPane.Id));
        if (!pane.IsShown())
            pane.Show();
        ControlCenterHost.Post(app =>
            ControlCenterView.Instance.RefreshStatus(app.ActiveUIDocument?.Document));
        return Result.Succeeded;
    }
}

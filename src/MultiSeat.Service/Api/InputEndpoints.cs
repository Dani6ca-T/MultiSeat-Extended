using MultiSeat.Service.Input;
using MultiSeat.Service.Sessions;

namespace MultiSeat.Service.Api;

public static class InputEndpoints
{
    public static void Map(WebApplication app)
    {
        var group = app.MapGroup("/api/input").WithTags("Input");

        // ── Controller routing mode ──────────────────────────────────
        // Lets the dashboard tell whether MultiSeat-managed controller routing is on.
        // When off (default), Apollo forwards the Moonlight client's controller natively,
        // so the XInput→seat assignment UI below is inert — the dashboard shows a note
        // instead of pretending the assignment does something.
        group.MapGet("/mode", (SeatManager mgr) =>
            Results.Ok(new { viGEmControllerEnabled = mgr.ControllerRoutingEnabled }));

        // ── Controller status ────────────────────────────────────────
        group.MapGet("/controllers", (SeatManager mgr) =>
        {
            var router = mgr.InputRouter;
            var connected = router.GetConnectedControllers();
            var assignments = router.GetAssignments();

            var controllers = connected.Select(idx => new
            {
                XInputIndex = idx,
                AssignedSeatId = assignments.TryGetValue(idx, out var seatId)
                    ? seatId : (Guid?)null,
                AssignedSeatName = assignments.TryGetValue(idx, out var sid)
                    ? mgr.GetSeat(sid)?.AccountName : null
            });

            return Results.Ok(controllers);
        });

        // ── All assignments ──────────────────────────────────────────
        group.MapGet("/assignments", (SeatManager mgr) =>
            Results.Ok(mgr.InputRouter.GetAssignments()));

        // ── Assign XInput controller to seat ─────────────────────────
        group.MapPost("/assign", (AssignControllerRequest request, SeatManager mgr) =>
        {
            if (request.XInputIndex is < 0 or > 3)
                return Results.BadRequest(new { error = "XInput index must be 0-3" });

            var seat = mgr.GetSeat(request.SeatId);
            if (seat is null)
                return Results.NotFound(new { error = "Seat not found" });

            mgr.InputRouter.AssignController(request.XInputIndex, request.SeatId);
            return Results.Ok(new { status = "assigned" });
        });

        // ── Unassign XInput controller ───────────────────────────────
        group.MapDelete("/assign/{xinputIndex:int}", (int xinputIndex, SeatManager mgr) =>
        {
            mgr.InputRouter.UnassignController(xinputIndex);
            return Results.Ok(new { status = "unassigned" });
        });

        // ── Auto-assign all controllers to seats ─────────────────────
        group.MapPost("/auto-assign", (SeatManager mgr) =>
        {
            var seats = mgr.GetAllSeats()
                .Where(s => s.Status is MultiSeat.Shared.Models.SeatStatus.Ready
                    or MultiSeat.Shared.Models.SeatStatus.Streaming)
                .ToList();

            mgr.InputRouter.AutoAssign(seats);
            return Results.Ok(new
            {
                status = "auto-assigned",
                assignments = mgr.InputRouter.GetAssignments()
            });
        });

        // ── Hook status ──────────────────────────────────────────────
        group.MapGet("/hooks/status", (SeatManager mgr) =>
        {
            var hookMgr = mgr.InputHookManager;
            return Results.Ok(new
            {
                Installed = hookMgr.IsInstalled,
                TargetSessionId = hookMgr.CurrentSessionId,
                Enabled = hookMgr.CurrentSessionId != 0xFFFFFFFF
            });
        });
    }
}

public sealed record AssignControllerRequest(int XInputIndex, Guid SeatId);

using Microsoft.AspNetCore.Mvc;
using MultiSeat.Service.Sessions;
using MultiSeat.Shared.Models;

namespace MultiSeat.Service.Api;

public static class SeatEndpoints
{
    public static void Map(WebApplication app)
    {
        var group = app.MapGroup("/api/seats").WithTags("Seats");

        group.MapGet("/", (SeatManager mgr) =>
            Results.Ok(mgr.GetAllSeats()));

        group.MapGet("/{id:guid}", (Guid id, SeatManager mgr) =>
        {
            var seat = mgr.GetSeat(id);
            return seat is null ? Results.NotFound() : Results.Ok(seat);
        });

        group.MapPost("/", async (SeatRequest request, SeatManager mgr, CancellationToken ct) =>
        {
            try
            {
                var seat = await mgr.ProvisionSeatAsync(request, ct);
                return Results.Created($"/api/seats/{seat.Id}", seat);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        group.MapPost("/{id:guid}/launch",
            async (Guid id, LaunchAppRequest request, SeatManager mgr, CancellationToken ct) =>
            {
                var seat = mgr.GetSeat(id);
                if (seat is null)
                    return Results.NotFound();

                try
                {
                    await mgr.LaunchAppInSeatAsync(id, request, ct);
                    return Results.Ok(new { status = "launched" });
                }
                catch (InvalidOperationException ex)
                {
                    return Results.BadRequest(new { error = ex.Message });
                }
            });

        group.MapDelete("/{id:guid}", async (Guid id, SeatManager mgr, CancellationToken ct) =>
        {
            var seat = mgr.GetSeat(id);
            if (seat is null)
                return Results.NotFound();

            await mgr.TeardownSeatAsync(id, ct);
            return Results.NoContent();
        });

        // ── Per-seat service management ────────────────────────────────

        group.MapGet("/{id:guid}/services", (Guid id, SeatManager mgr) =>
        {
            if (mgr.GetSeat(id) is null)
                return Results.NotFound();
            return Results.Ok(mgr.GetSeatServices(id));
        });

        group.MapPost("/{id:guid}/apollo/stop", (Guid id, SeatManager mgr) =>
        {
            if (mgr.GetSeat(id) is null)
                return Results.NotFound();
            try
            {
                mgr.StopApollo(id);
                return Results.Ok(new { status = "stopped" });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        group.MapPost("/{id:guid}/apollo/start",
            async (Guid id, SeatManager mgr, CancellationToken ct) =>
            {
                if (mgr.GetSeat(id) is null)
                    return Results.NotFound();
                try
                {
                    await mgr.StartApolloAsync(id, ct);
                    return Results.Ok(new { status = "started" });
                }
                catch (InvalidOperationException ex)
                {
                    return Results.BadRequest(new { error = ex.Message });
                }
            });

        group.MapPost("/{id:guid}/apollo/restart",
            async (Guid id, SeatManager mgr, CancellationToken ct) =>
            {
                if (mgr.GetSeat(id) is null)
                    return Results.NotFound();
                try
                {
                    await mgr.RestartApolloAsync(id, ct);
                    return Results.Ok(new { status = "restarted" });
                }
                catch (InvalidOperationException ex)
                {
                    return Results.BadRequest(new { error = ex.Message });
                }
            });

        group.MapPost("/{id:guid}/audio/reset", (Guid id, SeatManager mgr) =>
        {
            if (mgr.GetSeat(id) is null)
                return Results.NotFound();
            try
            {
                mgr.ResetAudio(id);
                return Results.Ok(new { status = "reset" });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        group.MapPost("/{id:guid}/display/reset",
            async (Guid id, SeatManager mgr, CancellationToken ct) =>
            {
                if (mgr.GetSeat(id) is null)
                    return Results.NotFound();
                try
                {
                    await mgr.ResetDisplayAsync(id, ct);
                    return Results.Ok(new { status = "reset" });
                }
                catch (InvalidOperationException ex)
                {
                    return Results.BadRequest(new { error = ex.Message });
                }
            });

        group.MapPost("/{id:guid}/controller/reset", (Guid id, SeatManager mgr) =>
        {
            if (mgr.GetSeat(id) is null)
                return Results.NotFound();
            try
            {
                mgr.ResetController(id);
                return Results.Ok(new { status = "reset" });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        group.MapPost("/{id:guid}/session-reconnect",
            async (Guid id, SeatManager mgr, SessionLauncher sessionLauncher, CancellationToken ct) =>
            {
                var seat = mgr.GetSeat(id);
                if (seat is null)
                    return Results.NotFound();

                await sessionLauncher.LaunchSessionAsync(seat.AccountName, ct);
                return Results.Ok(new { sessionId = seat.SessionId, message = "Session reconnected" });
            });
    }
}

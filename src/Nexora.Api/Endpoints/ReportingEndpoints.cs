using System.Diagnostics;
using System.Globalization;
using System.Text;
using Nexora.Api.Authentication;
using Nexora.Api.Filters;
using Nexora.Application.Common;
using Nexora.Application.DTOs;
using Nexora.Application.Services;

namespace Nexora.Api.Endpoints;

public static class ReportingEndpoints
{
    private const int MaxExportRows = 50_000;

    public static IEndpointRouteBuilder MapReportingEndpoints(this IEndpointRouteBuilder app)
    {
        var reports = app.MapGroup("/api/v1/reports").WithTags("Reports").RequireAuthorization(Policies.ViewReports);

        reports.MapGet("/summary", async ([AsParameters] ReportSummaryQuery query, ReportService service, HttpContext http, CancellationToken ct) =>
            {
                var stopwatch = Stopwatch.StartNew();
                var result = await service.GetSummaryAsync(query, ct);
                http.Response.Headers["X-Cache"] = result.CacheHit ? "HIT" : "MISS";
                http.Response.Headers["Server-Timing"] = $"report;dur={stopwatch.Elapsed.TotalMilliseconds.ToString("0.0", CultureInfo.InvariantCulture)}";
                return TypedResults.Ok(result.Summary);
            })
            .WithValidation<ReportSummaryQuery>()
            .WithSummary("Dashboard aggregates: totals per currency, status breakdown, top branches and a daily series");

        reports.MapGet("/transactions.csv", ([AsParameters] TransactionListQuery query, TransactionService service, HttpContext http) =>
            {
                query.Paging = "cursor";
                query.Cursor = null;
                query.SortBy = null;
                query.PageSize = 100;

                return Results.Stream(async stream =>
                {
                    await using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
                    await writer.WriteLineAsync("reference,created_at,branch,type,status,amount,fee,net_amount,currency,counterparty");

                    var written = 0;
                    do
                    {
                        var page = await service.ListAsync(query, http.RequestAborted);
                        foreach (var t in page.Items)
                        {
                            await writer.WriteLineAsync(string.Join(',',
                                t.Reference, t.CreatedAt.ToString("O"), CsvField.Quote(t.BranchName), t.Type, t.Status,
                                t.Amount.ToString(CultureInfo.InvariantCulture), t.Fee.ToString(CultureInfo.InvariantCulture),
                                t.NetAmount.ToString(CultureInfo.InvariantCulture), t.Currency, CsvField.Quote(t.CounterpartyName)));
                        }

                        written += page.Items.Count;
                        query.Cursor = page.NextCursor;
                    }
                    while (query.Cursor is not null && written < MaxExportRows);
                }, "text/csv", $"transactions-{DateTime.UtcNow:yyyyMMddHHmm}.csv");
            })
            .WithValidation<TransactionListQuery>()
            .WithSummary("Stream matching transactions as CSV (max 50,000 rows, keyset paged)");

        app.MapGet("/api/v1/audit-logs", async ([AsParameters] AuditLogQuery query, AuditLogService service, CancellationToken ct) =>
                TypedResults.Ok(await service.ListAsync(query, ct)))
            .WithTags("Audit")
            .RequireAuthorization(Policies.ViewAudit)
            .WithSummary("Search the immutable audit trail");

        return app;
    }
}

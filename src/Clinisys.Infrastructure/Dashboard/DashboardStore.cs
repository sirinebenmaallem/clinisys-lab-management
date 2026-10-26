using System.Data;
using Clinisys.Application.Features.Dashboard;
using Clinisys.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Clinisys.Infrastructure.Dashboard;

public sealed class DashboardStore(ClinisysDbContext db) : IDashboardStore
{
    public async Task<DashboardDto> GetAsync(DateOnly from, DateOnly to, CancellationToken ct)
    {
        var connection = db.Database.GetDbConnection();
        var opened = connection.State != ConnectionState.Open;
        if (opened) await db.Database.OpenConnectionAsync(ct);
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "dbo.GetStatisticsDashboard";
            command.CommandType = CommandType.StoredProcedure;
            command.CommandTimeout = db.Database.GetCommandTimeout() ?? 30;
            command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
            foreach (var (name, date) in new[] { ("@From", from), ("@To", to) })
            {
                var parameter = command.CreateParameter();
                parameter.ParameterName = name; parameter.DbType = DbType.Date;
                parameter.Value = date.ToDateTime(TimeOnly.MinValue);
                command.Parameters.Add(parameter);
            }
            await using var reader = await command.ExecuteReaderAsync(ct);
            await reader.ReadAsync(ct);
            var count = reader.GetInt64(0); var revenue = reader.GetDecimal(1);
            await reader.NextResultAsync(ct);
            var days = new List<DashboardDayDto>();
            while (await reader.ReadAsync(ct)) days.Add(new(DateOnly.FromDateTime(reader.GetDateTime(0)), reader.GetInt64(1)));
            await reader.NextResultAsync(ct);
            var top = new List<DashboardAnalysisDto>();
            while (await reader.ReadAsync(ct)) top.Add(new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetInt64(3)));
            return new(from, to, count, revenue, days, top);
        }
        finally { if (opened) await db.Database.CloseConnectionAsync(); }
    }
}

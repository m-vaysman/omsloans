using Microsoft.EntityFrameworkCore;
using OmsLoan.Domain;

namespace OmsLoan.Worker.Ingestion;

/// <summary>
/// <see cref="INoticeStore"/> over EF Core.
/// </summary>
/// <remarks>
/// Scope per notice: context is scoped, caller is a singleton. Per notice rather than per
/// poll so a failed insert leaves no tracked state for the next file in the same scan.
/// </remarks>
public sealed class EfNoticeStore(IServiceScopeFactory scopeFactory) : INoticeStore
{
    public async Task AddAsync(Notice notice, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();

        var db = scope.ServiceProvider.GetRequiredService<OmsLoanDbContext>();

        db.Notices.Add(notice);

        await db.SaveChangesAsync(cancellationToken);
    }
}

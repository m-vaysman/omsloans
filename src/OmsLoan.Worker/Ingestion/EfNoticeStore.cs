using Microsoft.EntityFrameworkCore;
using OmsLoan.Domain;

namespace OmsLoan.Worker.Ingestion;

/// <summary>
/// <see cref="INoticeStore"/> over EF Core.
/// </summary>
/// <remarks>
/// A scope per notice, because the context is registered scoped and this is called from a
/// singleton background service. Per notice rather than per poll is deliberate: one failed
/// insert then leaves no tracked state behind to affect the next file in the same scan.
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

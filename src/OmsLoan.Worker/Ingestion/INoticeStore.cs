using OmsLoan.Domain;

namespace OmsLoan.Worker.Ingestion;

/// <summary>
/// Records a notice. The one thing ingestion needs from the database.
/// </summary>
/// <remarks>
/// Interface rather than DbContext so the rule — record, commit, only then move — can be
/// tested against a store that fails on demand. A real database makes that hard to arrange.
/// </remarks>
public interface INoticeStore
{
    /// <summary>
    /// Inserts the notice and commits. Returning normally means it is durable; anything
    /// thrown means it is not, and the caller must leave the file where it is.
    /// </summary>
    Task AddAsync(Notice notice, CancellationToken cancellationToken);
}

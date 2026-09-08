using OmsLoan.Domain;

namespace OmsLoan.Worker.Ingestion;

/// <summary>
/// Records a notice. The one thing ingestion needs from the database.
/// </summary>
/// <remarks>
/// An interface rather than a DbContext dependency so the ordering rule — record, commit,
/// only then move the file — can be tested against a store that fails on demand. Proving
/// that a file stays put when the database is unavailable is the point of the rule, and it
/// is not something a real database makes easy to arrange.
/// </remarks>
public interface INoticeStore
{
    /// <summary>
    /// Inserts the notice and commits. Returning normally means it is durable; anything
    /// thrown means it is not, and the caller must leave the file where it is.
    /// </summary>
    Task AddAsync(Notice notice, CancellationToken cancellationToken);
}

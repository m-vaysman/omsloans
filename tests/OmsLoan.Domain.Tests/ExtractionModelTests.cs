using Microsoft.EntityFrameworkCore;
using OmsLoan.Domain;

namespace OmsLoan.Domain.Tests;

/// <summary>
/// Mapping of <see cref="Extraction"/> — the append-only row and its restrict-delete link
/// back to the notice that produced it.
/// </summary>
public class ExtractionModelTests
{
    [Fact]
    public void Extraction_MapsToExtractionsTable()
    {
        Assert.Equal("Extractions", DomainModel.Entity<Extraction>().GetTableName());
    }

    [Fact]
    public void RawJson_IsNvarcharMax_AndRequired()
    {
        var rawJson = DomainModel.Property<Extraction>(nameof(Extraction.RawJson));

        // Provider responses are unbounded and are written before parsing is attempted, so
        // an unparseable response is still stored rather than being lost with the error.
        Assert.Equal("nvarchar(max)", rawJson.GetColumnType());
        Assert.False(rawJson.IsNullable);
    }

    [Fact]
    public void ModelNameAndPromptVersion_AreRequired()
    {
        var modelName = DomainModel.Property<Extraction>(nameof(Extraction.ModelName));
        var promptVersion = DomainModel.Property<Extraction>(nameof(Extraction.PromptVersion));

        // The accuracy report slices by both. An extraction that recorded neither could not
        // be attributed to a model or a prompt revision, and would be dead weight.
        Assert.False(modelName.IsNullable);
        Assert.Equal(128, modelName.GetMaxLength());
        Assert.False(promptVersion.IsNullable);
        Assert.Equal(32, promptVersion.GetMaxLength());
    }

    [Fact]
    public void DeletingANotice_IsRestricted_SoExtractionsCannotBeOrphanedOrCascaded()
    {
        var foreignKey = Assert.Single(
            DomainModel.Entity<Extraction>().GetForeignKeys(),
            fk => fk.PrincipalEntityType.ClrType == typeof(Notice));

        // Cascade here would let one delete take the notice, every extraction attempt and
        // every human correction with it — the entire accuracy dataset for that notice.
        Assert.Equal(DeleteBehavior.Restrict, foreignKey.DeleteBehavior);
    }

    [Fact]
    public void NoticeForeignKey_IsRequired()
    {
        Assert.False(DomainModel.Property<Extraction>(nameof(Extraction.NoticeId)).IsNullable);
    }

    [Fact]
    public void CurrentExtractionLookup_IsIndexed()
    {
        // Every current-value read filters on both columns; without the index that is a
        // scan over a notice's full reprocessing history.
        var index = DomainModel.Index<Extraction>(
            nameof(Extraction.NoticeId),
            nameof(Extraction.IsCurrent));

        Assert.False(index.IsUnique);
    }

    [Fact]
    public void CreatedAtUtc_IsRequired()
    {
        Assert.False(DomainModel.Property<Extraction>(nameof(Extraction.CreatedAtUtc)).IsNullable);
    }
}

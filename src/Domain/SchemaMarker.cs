namespace po_prostu_silka.Domain;

/// <summary>
/// Proving entity for the persistence foundation (F-01).
///
/// Its only job is to give the first migration a real table to create, so the
/// migrate-on-deploy pipeline is exercised against something that can actually fail
/// on permissions, collation, or provider configuration - an empty migration cannot.
///
/// This is scaffolding, not domain. F-02 introduces the first real entities; when it
/// does, this type and its migration should be removed rather than built upon.
/// </summary>
public class SchemaMarker
{
    public int Id { get; set; }

    public DateTimeOffset AppliedAt { get; set; }
}

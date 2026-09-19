using po_prostu_silka.Application.Persistence;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Scheduling;

namespace po_prostu_silka.Application.Scheduling;

/// <summary>
/// Shape validation for the class-type write paths, with the bounds it enforces.
///
/// <para>
/// THESE BOUNDS ARE DUPLICATED IN ClassRequestValidator ON PURPOSE, not shared from here. An
/// occurrence may legitimately override its type's defaults (prd-v2 FR-008), so it cannot
/// inherit the type's bounds by reference any more than it inherits its numbers. Keep the
/// four pairs in step by hand; do not consolidate them.
/// </para>
/// </summary>
public static class ClassTypeValidator
{
    /// <summary>
    /// Bounds on a default duration. The floor matches ClassEndpoints.Validate — a zero-length class
    /// would make every overlap check meaningless. The ceiling is eight hours: past that it is a
    /// typo (600 for 60), not a class.
    /// </summary>
    private const int MinDurationMinutes = 1;

    private const int MaxDurationMinutes = 480;

    /// <summary>
    /// Bounds on a default capacity. The floor matches ClassEndpoints.Validate — a class nobody can
    /// book is not a class. The ceiling is far above any room this club has, and exists only to
    /// catch a slipped digit.
    /// </summary>
    private const int MinCapacity = 1;

    private const int MaxCapacity = 200;

    /// <summary>
    /// Matches ClassTypeConfiguration's column length. Keep the two in step.
    ///
    /// NOT optional to check. Without it a longer name reaches SQL Server, which refuses the INSERT
    /// with "String or binary data would be truncated" - an unhandled DbUpdateException, i.e. a 500
    /// for what is ordinary bad input. Every column with a HasMaxLength needs its own guard here.
    /// </summary>
    private const int MaxNameLength = 200;

    /// <summary>Matches ClassTypeConfiguration's column length. Keep the two in step.</summary>
    private const int MaxDescriptionLength = 1000;

    /// <summary>
    /// The rules shared by create and edit. Hand-rolled, like every other validation in this
    /// codebase — there is no validation library here and adding one for four fields is not
    /// warranted.
    /// </summary>
    public static IResult? Validate(ClassTypeRequest request)
    {
        // Description is the one genuinely optional field in the scheduling context, so it is
        // absent from this check on purpose.
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Results.Json(new ClassTypeFailure("missing_field"), statusCode: 400);
        }

        // Measured on the TRIMMED value, like the description below and like the write itself.
        if (request.Name.Trim().Length > MaxNameLength)
        {
            return Results.Json(new ClassTypeFailure("name_too_long"), statusCode: 400);
        }

        // Measured on the TRIMMED value, which is what gets stored - otherwise trailing whitespace
        // could be refused for a description that fits.
        if (NormalizeDescription(request.Description) is { Length: > MaxDescriptionLength })
        {
            return Results.Json(new ClassTypeFailure("description_too_long"), statusCode: 400);
        }

        if (request.DefaultDurationMinutes is < MinDurationMinutes or > MaxDurationMinutes)
        {
            return Results.Json(new ClassTypeFailure("invalid_duration"), statusCode: 400);
        }

        if (request.DefaultCapacity is < MinCapacity or > MaxCapacity)
        {
            return Results.Json(new ClassTypeFailure("invalid_capacity"), statusCode: 400);
        }

        return null;
    }

    /// <summary>
    /// Trims, and collapses "absent" to a single representation. A whitespace-only description and a
    /// missing one mean the same thing to a reader, so they must not be two different values in the
    /// database — otherwise the screen needs to test for both.
    /// </summary>
    public static string? NormalizeDescription(string? description) =>
        string.IsNullOrWhiteSpace(description) ? null : description.Trim();
}

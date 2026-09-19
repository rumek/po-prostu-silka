namespace po_prostu_silka.Application.Members;

/// <summary>
/// The member's own account surface.
///
/// <para>
/// Lives in <c>Members</c> rather than <c>Auth</c> because it is member data, not credentials or
/// session. The password endpoints that land with S-13's later phases stay in <c>Auth</c> for the
/// same reason, inverted.
/// </para>
///
/// <para>
/// STILL NAMED IN A LOGGER CATEGORY. <see cref="UpdateProfile"/> logs under typeof(ProfileEndpoints)
/// rather than its own type, deliberately — see the comment at that call site.
/// </para>
/// </summary>
public static class ProfileEndpoints
{
    public static IEndpointRouteBuilder MapProfileEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/profile").WithTags("Profile");

        // Bare RequireAuthorization(), NEVER the ActiveMember policy - the same rule /me and
        // /refresh follow. An account registered before S-13 has no contact details, and the screen
        // that prompts it to supply them is reachable while still Pending. Gating this on approval
        // would make the prompt appear on a screen whose save button always 403s.
        group.MapPut("/", UpdateProfile.HandleAsync).RequireAuthorization();

        return app;
    }
}

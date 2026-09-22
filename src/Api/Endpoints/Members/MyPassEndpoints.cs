using po_prostu_silka.Domain;
using po_prostu_silka.Application.Members;

namespace po_prostu_silka.Api.Endpoints.Members;

/// <summary>
/// The member's own karnet (S-16, MP-07) — the read half of the pass, and the only pass surface a
/// member ever sees.
///
/// <para>
/// SCOPED TO THE CALLER, AND THERE IS NO ID TO TAMPER WITH. The route takes no member id at all: it
/// resolves the caller from the cookie and asks about them. That is the same shape
/// <see cref="Training.MyPlanEndpoints"/> uses and it is stronger than an ownership comparison —
/// there is no parameter to get wrong, so there is no way to get it wrong.
/// </para>
///
/// <para>
/// READ ONLY, and that is the product decision rather than a scope cut (MP-01). A member does not
/// buy, extend or request a karnet in this app; they read what the club issued them. The screen shows
/// type, validity and entries left, and nothing on it is a button.
/// </para>
///
/// <para>
/// SEPARATE FROM <see cref="MembershipPassEndpoints"/> on purpose, rather than one more route on that
/// group. The two answer to different policies — MemberOnly here (ActiveMember until S-25), Admin
/// there — and this codebase applies one policy per group. Staff hold no karnet (S-25), so a staff
/// caller is refused here rather than shown an empty answer.
/// </para>
/// </summary>
public static class MyPassEndpoints
{
    public static IEndpointRouteBuilder MapMyPassEndpoints(this IEndpointRouteBuilder app)
    {
        var mine = app.MapGroup("/api/passes")
            .WithTags("MembershipPasses")
            .RequireAuthorization(AuthorizationPolicyNames.MemberOnly);

        mine.MapGet("/mine", GetMyPass.HandleAsync);

        return app;
    }
}

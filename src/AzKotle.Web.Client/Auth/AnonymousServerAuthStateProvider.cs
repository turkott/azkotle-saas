using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace AzKotle.Web.Client.Auth;

public sealed class AnonymousServerAuthStateProvider : AuthenticationStateProvider
{
    private static readonly Task<AuthenticationState> _anonymous =
        Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity())));

    public override Task<AuthenticationState> GetAuthenticationStateAsync() => _anonymous;
}

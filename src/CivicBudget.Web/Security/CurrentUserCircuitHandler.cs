using CivicBudget.Infrastructure.Security;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.Circuits;

namespace CivicBudget.Web.Security;

/// <summary>
/// A Blazor Server circuit has its own DI scope, created when the SignalR connection opens, so the
/// HTTP middleware never runs for it. This handler fills the circuit scope's
/// <see cref="CurrentUserContext"/> from the circuit's authentication state when the circuit opens,
/// and keeps it current if the state changes (for example, the revalidating provider signing the
/// user out). Without this, every DbContext created on the circuit would see no tenant and no rows.
/// </summary>
public sealed class CurrentUserCircuitHandler(
    AuthenticationStateProvider authenticationStateProvider,
    CurrentUserContext currentUser) : CircuitHandler, IDisposable
{
    public override async Task OnCircuitOpenedAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        AuthenticationState state = await authenticationStateProvider.GetAuthenticationStateAsync();
        currentUser.SetPrincipal(state.User);
        authenticationStateProvider.AuthenticationStateChanged += OnAuthenticationStateChanged;
    }

    private async void OnAuthenticationStateChanged(Task<AuthenticationState> task)
    {
        AuthenticationState state = await task;
        currentUser.SetPrincipal(state.User);
    }

    public void Dispose() => authenticationStateProvider.AuthenticationStateChanged -= OnAuthenticationStateChanged;
}

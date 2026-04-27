using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AzKotle.Application.Tenants.Team;

namespace AzKotle.Web.Client.Api;

public sealed class TeamApiClient
{
    private static readonly JsonSerializerOptions _serializerOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;

    public TeamApiClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<IReadOnlyList<TeamUserDto>> ListAsync(CancellationToken ct = default)
    {
        var resp = await _http.GetAsync("api/v1/tenant/users", ct);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<List<TeamUserDto>>(_serializerOptions, ct))
            ?? new List<TeamUserDto>();
    }

    public async Task<CreateTechnicianResponse> CreateTechnicianAsync(
        CreateTechnicianRequest request, CancellationToken ct = default)
    {
        var resp = await _http.PostAsJsonAsync("api/v1/tenant/users", request, _serializerOptions, ct);
        if (resp.StatusCode == HttpStatusCode.Conflict)
        {
            return new CreateTechnicianResponse(null, "email_taken");
        }
        resp.EnsureSuccessStatusCode();
        var dto = await resp.Content.ReadFromJsonAsync<TeamUserDto>(_serializerOptions, ct);
        return new CreateTechnicianResponse(dto, null);
    }

    public async Task<DeleteUserResponse> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var resp = await _http.DeleteAsync($"api/v1/tenant/users/{id}", ct);
        if (resp.StatusCode == HttpStatusCode.NoContent)
        {
            return new DeleteUserResponse(true, null);
        }
        if (resp.StatusCode == HttpStatusCode.BadRequest)
        {
            return new DeleteUserResponse(false, "self_delete_forbidden");
        }
        if (resp.StatusCode == HttpStatusCode.NotFound)
        {
            return new DeleteUserResponse(false, "not_found");
        }
        resp.EnsureSuccessStatusCode();
        return new DeleteUserResponse(true, null);
    }
}

public sealed record CreateTechnicianResponse(TeamUserDto? User, string? Error);
public sealed record DeleteUserResponse(bool Success, string? Error);

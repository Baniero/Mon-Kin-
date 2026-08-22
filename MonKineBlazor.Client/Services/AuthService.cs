using System.Net.Http.Json;
using Microsoft.JSInterop;
using MonKineBlazor.Shared.Models;

namespace MonKineBlazor.Client.Services;

public class AuthService
{
    private readonly HttpClient _http;
    private readonly IJSRuntime _jsRuntime;
    private const string StorageKey = "monkine.user";

    public UserDto? CurrentUser { get; private set; }
    public bool IsAuthenticated => CurrentUser != null;
    public bool IsInitialized { get; private set; }
    public event Action? OnChange;

    public AuthService(HttpClient http, IJSRuntime jsRuntime)
    {
        _http = http;
        _jsRuntime = jsRuntime;
    }

    public async Task InitializeAsync()
    {
        try
        {
            var json = await _jsRuntime.InvokeAsync<string>("localStorage.getItem", StorageKey);
            if (!string.IsNullOrWhiteSpace(json))
            {
                CurrentUser = System.Text.Json.JsonSerializer.Deserialize<UserDto>(json);
                NormalizeCurrentUserModules();
                ApplyCurrentUserHeader();
            }
            else
            {
                CurrentUser = null;
            }
        }
        catch
        {
            CurrentUser = null;
        }
        finally
        {
            LogHeaderState("InitializeAsync");
            IsInitialized = true;
            NotifyStateChanged();
        }
    }

    public async Task<bool> LoginAsync(string username, string password)
    {
        var response = await _http.PostAsJsonAsync("api/users/login", new LoginRequestDto { Username = username, Password = password });
        if (!response.IsSuccessStatusCode)
        {
            return false;
        }

        CurrentUser = await response.Content.ReadFromJsonAsync<UserDto>();
        if (CurrentUser != null)
        {
            NormalizeCurrentUserModules();
            ApplyCurrentUserHeader();
            LogHeaderState("LoginAsync");
            var json = System.Text.Json.JsonSerializer.Serialize(CurrentUser);
            await _jsRuntime.InvokeVoidAsync("localStorage.setItem", StorageKey, json);
            NotifyStateChanged();
            return true;
        }

        return false;
    }

    public async Task LogoutAsync()
    {
        CurrentUser = null;
        _http.DefaultRequestHeaders.Remove("X-User-Id");
        await _jsRuntime.InvokeVoidAsync("localStorage.removeItem", StorageKey);
        NotifyStateChanged();
    }

    private void ApplyCurrentUserHeader()
    {
        _http.DefaultRequestHeaders.Remove("X-User-Id");
        if (CurrentUser != null)
        {
            NormalizeCurrentUserModules();
            _http.DefaultRequestHeaders.Add("X-User-Id", CurrentUser.Id.ToString());
        }
    }

    private void NormalizeCurrentUserModules()
    {
        if (CurrentUser?.Modules == null)
        {
            return;
        }

        for (int i = 0; i < CurrentUser.Modules.Count; i++)
        {
            if (string.Equals(CurrentUser.Modules[i], "agenda", StringComparison.OrdinalIgnoreCase))
            {
                CurrentUser.Modules[i] = "appointments";
            }
        }

        CurrentUser.Modules = CurrentUser.Modules.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private void LogHeaderState(string origin)
    {
        var hasHeader = _http.DefaultRequestHeaders.Contains("X-User-Id");
        var headerValue = hasHeader ? string.Join(",", _http.DefaultRequestHeaders.GetValues("X-User-Id")) : "<missing>";
        Console.WriteLine($"[AuthService] {origin}: X-User-Id present={hasHeader}, value={headerValue}");
    }

    private void NotifyStateChanged() => OnChange?.Invoke();
}

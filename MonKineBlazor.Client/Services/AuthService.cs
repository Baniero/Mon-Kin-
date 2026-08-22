using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.JSInterop;
using MonKineBlazor.Shared.Models;

namespace MonKineBlazor.Client.Services;

public class AuthService
{
    private readonly HttpClient _http;
    private readonly IJSRuntime _jsRuntime;
    private const string StorageKey = "monkine.auth";

    private string? _authToken;

    public UserDto? CurrentUser { get; private set; }
    public bool IsAuthenticated => CurrentUser != null && !string.IsNullOrWhiteSpace(_authToken);
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
                var authState = System.Text.Json.JsonSerializer.Deserialize<AuthState>(json);
                if (authState?.User != null && !string.IsNullOrWhiteSpace(authState.Token))
                {
                    CurrentUser = authState.User;
                    _authToken = authState.Token;
                    NormalizeCurrentUserModules();
                    ApplyCurrentUserHeader();
                }
                else
                {
                    CurrentUser = null;
                    _authToken = null;
                }
            }
            else
            {
                CurrentUser = null;
                _authToken = null;
            }
        }
        catch
        {
            CurrentUser = null;
            _authToken = null;
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

        var loginResponse = await response.Content.ReadFromJsonAsync<LoginResponseDto>();
        if (loginResponse?.User != null && !string.IsNullOrWhiteSpace(loginResponse.Token))
        {
            CurrentUser = loginResponse.User;
            _authToken = loginResponse.Token;
            NormalizeCurrentUserModules();
            ApplyCurrentUserHeader();
            LogHeaderState("LoginAsync");
            var authState = new AuthState
            {
                User = CurrentUser,
                Token = _authToken
            };
            var json = System.Text.Json.JsonSerializer.Serialize(authState);
            await _jsRuntime.InvokeVoidAsync("localStorage.setItem", StorageKey, json);
            NotifyStateChanged();
            return true;
        }

        return false;
    }

    public async Task LogoutAsync()
    {
        CurrentUser = null;
        _authToken = null;
        _http.DefaultRequestHeaders.Authorization = null;
        await _jsRuntime.InvokeVoidAsync("localStorage.removeItem", StorageKey);
        NotifyStateChanged();
    }

    private void ApplyCurrentUserHeader()
    {
        _http.DefaultRequestHeaders.Authorization = null;
        if (CurrentUser != null && !string.IsNullOrWhiteSpace(_authToken))
        {
            NormalizeCurrentUserModules();
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _authToken);
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
        var hasAuthorization = _http.DefaultRequestHeaders.Authorization != null;
        var headerValue = hasAuthorization ? _http.DefaultRequestHeaders.Authorization.ToString() : "<missing>";
        Console.WriteLine($"[AuthService] {origin}: Authorization present={hasAuthorization}, value={headerValue}");
    }

    private class AuthState
    {
        public UserDto? User { get; set; }
        public string? Token { get; set; }
    }

    private void NotifyStateChanged() => OnChange?.Invoke();
}

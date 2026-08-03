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
                NotifyStateChanged();
            }
        }
        catch
        {
            CurrentUser = null;
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
        await _jsRuntime.InvokeVoidAsync("localStorage.removeItem", StorageKey);
        NotifyStateChanged();
    }

    private void NotifyStateChanged() => OnChange?.Invoke();
}

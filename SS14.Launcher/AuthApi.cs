using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.Serialization;
using System.Text.Json;
using System.Threading.Tasks;
using Serilog;
using Splat;
using SS14.Launcher.Models;
using SS14.Launcher.Models.Data;

namespace SS14.Launcher;

public sealed class AuthApi
{
    private readonly HttpClient _httpClient;

    public AuthApi()
    {
        _httpClient = Locator.Current.GetService<HttpClient>();
    }

    public Task<AuthenticateResult> AuthenticateAsync(Guid userId, string password)
    {
        var request = new AuthenticateRequest
        {
            UserId = userId,
            Password = password
        };

        return AuthenticateImpl(request);
    }

    public Task<AuthenticateResult> AuthenticateAsync(string username, string password)
    {
        var request = new AuthenticateRequest
        {
            Username = username,
            Password = password
        };

        return AuthenticateImpl(request);
    }

    private async Task<AuthenticateResult> AuthenticateImpl(AuthenticateRequest request)
    {
        try
        {
            const string authUrl = ConfigConstants.AuthUrl + "api/auth/authenticate";

            using var resp = await _httpClient.PostAsync(authUrl, request);

            if (resp.IsSuccessStatusCode)
            {
                var respJson = await resp.Content.AsJson<AuthenticateResponse>();
                var token = new LoginToken(respJson.Token, DateTimeOffset.Parse(respJson.ExpireTime));
                return new AuthenticateResult(new LoginInfo
                {
                    UserId = respJson.UserId,
                    Token = token,
                    Username = respJson.Username
                });
            }

            if (resp.StatusCode == HttpStatusCode.Unauthorized)
            {
                // Login failure.
                var respJson = await resp.Content.AsJson<AuthenticateDenyResponse>();
                return new AuthenticateResult(respJson.Errors);
            }

            Log.Error("Server returned unexpected HTTP status code: {responseCode}", resp.StatusCode);
            Log.Debug("Response for error:\n{response}\n{content}", resp, await resp.Content.ReadAsStringAsync());
            // Unknown error? uh oh.
            return new AuthenticateResult(new[] {"Server returned unknown error"});
        }
        catch (JsonException e)
        {
            Log.Error(e, "JsonException in AuthenticateAsync");
            return new AuthenticateResult(new[] {"Server sent invalid response"});
        }
        catch (HttpRequestException httpE)
        {
            Log.Error(httpE, "HttpRequestException in AuthenticateAsync");
            return new AuthenticateResult(new[] {$"Connection error to authentication server: {httpE.Message}"});
        }
    }

    public async Task<RegisterResult> RegisterAsync(string username, string email, string password)
    {
        try
        {
            var request = new RegisterRequest
            {
                Username = username,
                Email = email,
                Password = password
            };

            const string authUrl = ConfigConstants.AuthUrl + "api/auth/register";

            using var resp = await _httpClient.PostAsync(authUrl, request);

            if (resp.IsSuccessStatusCode)
            {
                var respJson = await resp.Content.AsJson<RegisterResponse>();
                return new RegisterResult(respJson.Status);
            }

            if (resp.StatusCode == HttpStatusCode.UnprocessableEntity)
            {
                // Register failure.
                var respJson = await resp.Content.AsJson<RegisterResponseError>();
                return new RegisterResult(respJson.Errors);
            }

            Log.Error("Server returned unexpected HTTP status code: {responseCode}", resp.StatusCode);
            Log.Debug("Response for error:\n{response}\n{content}", resp, await resp.Content.ReadAsStringAsync());
            // Unknown error? uh oh.
            return new RegisterResult(new[] {"Server returned unknown error"});
        }
        catch (JsonException e)
        {
            Log.Error(e, "JsonException in RegisterAsync");
            return new RegisterResult(new[] {"Server sent invalid response"});
        }
        catch (HttpRequestException httpE)
        {
            Log.Error(httpE, "HttpRequestException in RegisterAsync");
            return new RegisterResult(new[] {$"Connection error to authentication server: {httpE.Message}"});
        }
    }

    /// <returns>Any errors that occured</returns>
    public async Task<string[]?> ForgotPasswordAsync(string email)
    {
        try
        {
            var request = new ResetPasswordRequest
            {
                Email = email,
            };

            const string authUrl = ConfigConstants.AuthUrl + "api/auth/resetPassword";

            using var resp = await _httpClient.PostAsync(authUrl, request);

            if (resp.IsSuccessStatusCode)
            {
                return null;
            }

            // Unknown error? uh oh.
            Log.Error("Server returned unexpected HTTP status code: {responseCode}", resp.StatusCode);
            Log.Debug("Response for error:\n{response}\n{content}", resp, await resp.Content.ReadAsStringAsync());
            return new[] {"Server returned unknown error"};
        }
        catch (HttpRequestException httpE)
        {
            Log.Error(httpE, "HttpRequestException in ForgotPasswordAsync");
            return new[] {$"Connection error to authentication server: {httpE.Message}"};
        }
    }

    public async Task<string[]?> ResendConfirmationAsync(string email)
    {
        try
        {
            var request = new ResendConfirmationRequest
            {
                Email = email,
            };

            const string authUrl = ConfigConstants.AuthUrl + "api/auth/resendConfirmation";

            using var resp = await _httpClient.PostAsync(authUrl, request);

            if (resp.IsSuccessStatusCode)
            {
                return null;
            }

            // Unknown error? uh oh.
            Log.Error("Server returned unexpected HTTP status code: {responseCode}", resp.StatusCode);
            Log.Debug("Response for error:\n{response}\n{content}", resp, await resp.Content.ReadAsStringAsync());
            return new[] {"Server returned unknown error"};
        }
        catch (HttpRequestException httpE)
        {
            Log.Error(httpE, "HttpRequestException in ResendConfirmationAsync");
            return new[] {$"Connection error to authentication server: {httpE.Message}"};
        }
    }

    /// <returns>Null if the server refused to refresh the token (it expired).</returns>
    /// <exception cref="AuthApiException">
    ///     Thrown if an unexpected error occured.
    /// </exception>
    public async Task<LoginToken?> RefreshTokenAsync(string token)
    {
        try
        {
            var request = new RefreshRequest
            {
                Token = token
            };

            const string authUrl = ConfigConstants.AuthUrl + "api/auth/refresh";

            using var resp = await _httpClient.PostAsync(authUrl, request);

            if (resp.IsSuccessStatusCode)
            {
                var response = await resp.Content.AsJson<RefreshResponse>();
                var time = DateTimeOffset.Parse(response.ExpireTime);

                return new LoginToken(response.NewToken, time);
            }

            if (resp.StatusCode == HttpStatusCode.Unauthorized)
            {
                Log.Warning("Got unauthorized while trying to refresh token. Guess it expired.");

                return null;
            }

            // Unknown error? uh oh.
            Log.Error("Server returned unexpected HTTP status code: {responseCode}", resp.StatusCode);
            Log.Debug("Response for error:\n{response}\n{content}", resp, await resp.Content.ReadAsStringAsync());

            throw new AuthApiException($"Server returned unexpected HTTP status code: {resp.StatusCode}");
        }
        catch (HttpRequestException httpE)
        {
            Log.Error(httpE, "HttpRequestException in ResendConfirmationAsync");
            throw new AuthApiException("HttpRequestException thrown", httpE);
        }
        catch (JsonException jsonE)
        {
            Log.Error(jsonE, "JsonException in ResendConfirmationAsync");
            throw new AuthApiException("JsonException thrown", jsonE);
        }
    }

    public async Task LogoutTokenAsync(string token)
    {
        try
        {
            var request = new LogoutRequest
            {
                Token = token
            };

            const string authUrl = ConfigConstants.AuthUrl + "api/auth/logout";

            using var resp = await _httpClient.PostAsync(authUrl, request);

            if (resp.IsSuccessStatusCode)
            {
                return;
            }

            // Unknown error? uh oh.
            Log.Error("Server returned unexpected HTTP status code: {responseCode}", resp.StatusCode);
            Log.Debug("Response for error:\n{response}\n{content}", resp, await resp.Content.ReadAsStringAsync());
        }
        catch (HttpRequestException httpE)
        {
            // Does it make sense to just... swallow this exception? The token will stay "active" until it expires.
            Log.Error(httpE, "HttpRequestException in LogoutTokenAsync");
        }
    }

    /// <summary>
    ///     Check if a token is still valid.
    /// </summary>
    /// <returns>True if the token is still valid.</returns>
    /// <exception cref="AuthApiException">
    ///     Thrown if an unexpected error occured.
    /// </exception>
    public async Task<bool> CheckTokenAsync(string token)
    {
        try
        {
            const string authUrl = ConfigConstants.AuthUrl + "api/auth/ping";

            using var requestMessage = new HttpRequestMessage(HttpMethod.Get, authUrl);
            requestMessage.Headers.Authorization = new AuthenticationHeaderValue("SS14Auth", token);
            using var resp = await _httpClient.SendAsync(requestMessage);

            if (resp.IsSuccessStatusCode)
            {
                return true;
            }

            if (resp.StatusCode == HttpStatusCode.Unauthorized)
            {
                return false;
            }

            // Unknown error? uh oh.
            Log.Error("Server returned unexpected HTTP status code: {responseCode}", resp.StatusCode);
            Log.Debug("Response for error:\n{response}\n{content}", resp, await resp.Content.ReadAsStringAsync());
            throw new AuthApiException($"Server returned unexpected HTTP status code: {resp.StatusCode}");
        }
        catch (HttpRequestException httpE)
        {
            // Does it make sense to just... swallow this exception? The token will stay "active" until it expires.
            Log.Error(httpE, "HttpRequestException in CheckTokenAsync");
            throw new AuthApiException("HttpRequestException thrown", httpE);
        }
    }

    public sealed class AuthenticateRequest
    {
        public string? Username { get; set; }
        public Guid? UserId { get; set; }
        public string Password { get; set; } = default!;
    }

    public sealed class AuthenticateResponse
    {
        public string Token { get; set; } = default!;
        public string Username { get; set; } = default!;
        public Guid UserId { get; set; }
        public string ExpireTime { get; set; } = default!;
    }

    public sealed class AuthenticateDenyResponse
    {
        public string[] Errors { get; set; } = default!;
    }

    public sealed class RegisterRequest
    {
        public string Username { get; set; } = default!;
        public string Email { get; set; } = default!;
        public string Password { get; set; } = default!;
    }

    public sealed class RegisterResponse
    {
        public RegisterResponseStatus Status { get; set; }
    }

    public sealed class RegisterResponseError
    {
        public string[] Errors { get; set; } = default!;
    }

    public sealed class ResetPasswordRequest
    {
        public string Email { get; set; } = default!;
    }

    public sealed class ResendConfirmationRequest
    {
        public string Email { get; set; } = default!;
    }

    public sealed class LogoutRequest
    {
        public string Token { get; set; } = default!;
    }

    public sealed class RefreshRequest
    {
        public string Token { get; set; } = default!;
    }

    public sealed class RefreshResponse
    {
        public string NewToken { get; set; } = default!;
        public string ExpireTime { get; set; } = default!;
    }
}

public readonly struct AuthenticateResult
{
    private readonly LoginInfo? _loginInfo;
    private readonly string[]? _errors;

    public AuthenticateResult(LoginInfo loginInfo)
    {
        _loginInfo = loginInfo;
        _errors = null;
    }

    public AuthenticateResult(string[] errors)
    {
        _loginInfo = null;
        _errors = errors;
    }

    public bool IsSuccess => _loginInfo != null;

    public LoginInfo LoginInfo => _loginInfo
                                  ?? throw new InvalidOperationException(
                                      "This AuthenticateResult is not a success.");

    public string[] Errors => _errors
                              ?? throw new InvalidOperationException("This AuthenticateResult is not a failure.");
}

public readonly struct RegisterResult
{
    private readonly RegisterResponseStatus? _status;
    private readonly string[]? _errors;

    public RegisterResult(RegisterResponseStatus status)
    {
        _status = status;
        _errors = null;
    }

    public RegisterResult(string[] errors)
    {
        _status = null;
        _errors = errors;
    }

    public bool IsSuccess => _status != null;

    public RegisterResponseStatus Status => _status
                                            ?? throw new InvalidOperationException(
                                                "This RegisterResult is not a success.");

    public string[] Errors => _errors
                              ?? throw new InvalidOperationException("This RegisterResult is not a failure.");
}

public enum RegisterResponseStatus
{
    Registered,
    RegisteredNeedConfirmation
}

[Serializable]
public class AuthApiException : Exception
{
    public AuthApiException()
    {
    }

    public AuthApiException(string message) : base(message)
    {
    }

    public AuthApiException(string message, Exception inner) : base(message, inner)
    {
    }

    protected AuthApiException(
        SerializationInfo info,
        StreamingContext context) : base(info, context)
    {
    }
}
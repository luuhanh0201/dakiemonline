using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace DaKiem.Client.Accounts
{
    // The shapes of the account API's JSON (server/Game.Server/Accounts/AccountApi.cs). Fields, not
    // properties, because JsonUtility only reads fields; a missing number reads as 0.

    [Serializable]
    public sealed class AccountInfo
    {
        public string username;
        public string kind;
        public string contact;

        public bool IsGuest => kind == "guest";
    }

    [Serializable]
    public sealed class LoginReply
    {
        public string token;
        public AccountInfo account;
        public string guestSecret;
    }

    [Serializable]
    public sealed class MeReply
    {
        public AccountInfo account;
    }

    [Serializable]
    public sealed class CodeReply
    {
        public string contact;
        public int resendSec;
        public int lifetimeSec;
        public string delivery;

        public string Notice
        {
            get
            {
                int minutes = Mathf.CeilToInt(lifetimeSec / 60f);
                if (delivery == "operator")
                    return $"Bản thử: lấy mã qua người vận hành.\nChưa gửi email/SMS. Mã có hiệu lực {minutes} phút.";
                if (delivery == "contact")
                    return $"Đã gửi mã tới {contact}.\nMã có hiệu lực {minutes} phút.";
                // Older servers do not identify their sender; do not promise email/SMS delivery.
                return $"Đã tạo yêu cầu mã, hiệu lực {minutes} phút.\nNếu chưa nhận được, hãy liên hệ người vận hành.";
            }
        }
    }

    [Serializable]
    public sealed class ServerInfo
    {
        public int number;
        public string name;
        public bool isNew;
        public string status;
        public string character;
    }

    [Serializable]
    public sealed class ServersReply
    {
        public ServerInfo[] servers;
    }

    [Serializable]
    public sealed class CharacterInfo
    {
        public string name;
        public int server;
    }

    [Serializable]
    public sealed class CharacterReply
    {
        public CharacterInfo character;
    }

    /// <summary>A refusal from the server, or a network failure with <see cref="error"/> "network".</summary>
    [Serializable]
    public sealed class ApiError
    {
        public string error;
        public string message;
        public string field;
        public int waitSec;
        public int attemptsLeft;
    }

    [Serializable] internal sealed class CodeRequest { public string purpose; public string contact; public string username; }
    [Serializable] internal sealed class AccountRequest { public string username; public string password; public string contact; public string code; }
    [Serializable] internal sealed class LoginRequest { public string username; public string password; }
    [Serializable] internal sealed class GuestRequest { public string secret; }
    [Serializable] internal sealed class ResetRequest { public string contact; public string code; public string password; }
    [Serializable] internal sealed class CharacterRequest { public int server; public string name; }

    /// <summary>
    /// Calls the account API on the game server's own address with UnityWebRequest. Each call is a
    /// coroutine that ends by calling exactly one of its two callbacks.
    /// </summary>
    public sealed class AccountClient
    {
        private const int TimeoutSec = 15;

        public AccountClient(string baseUrl)
        {
            BaseUrl = baseUrl.TrimEnd('/');
        }

        public string BaseUrl { get; }

        /// <summary>The token sent with calls that need a login.</summary>
        public string Token { get; set; }

        /// <summary>http(s)://host:port from what the player typed, the same address the WebSocket uses.</summary>
        public static string FromWebSocketUrl(string webSocketUrl)
        {
            var uri = new Uri(webSocketUrl);
            string scheme = uri.Scheme == "wss" ? "https" : "http";
            return $"{scheme}://{uri.Authority}";
        }

        public IEnumerator SendCode(string purpose, string contact, string username, Action<CodeReply> done, Action<ApiError> failed)
        {
            return Call("POST", "/api/codes", new CodeRequest { purpose = purpose, contact = contact, username = username }, done, failed);
        }

        public IEnumerator Register(string username, string password, string contact, string code, Action<LoginReply> done, Action<ApiError> failed)
        {
            return Call("POST", "/api/register", new AccountRequest { username = username, password = password, contact = contact, code = code }, done, failed);
        }

        public IEnumerator Login(string username, string password, Action<LoginReply> done, Action<ApiError> failed)
        {
            return Call("POST", "/api/login", new LoginRequest { username = username, password = password }, done, failed);
        }

        public IEnumerator PlayNow(string guestSecret, Action<LoginReply> done, Action<ApiError> failed)
        {
            return Call("POST", "/api/guest", new GuestRequest { secret = guestSecret }, done, failed);
        }

        public IEnumerator Link(string username, string password, string contact, string code, Action<MeReply> done, Action<ApiError> failed)
        {
            return Call("POST", "/api/link", new AccountRequest { username = username, password = password, contact = contact, code = code }, done, failed);
        }

        public IEnumerator Reset(string contact, string code, string password, Action<ApiError> failed, Action done)
        {
            return Call<ApiError>("POST", "/api/reset", new ResetRequest { contact = contact, code = code, password = password }, _ => done(), failed);
        }

        public IEnumerator Me(Action<MeReply> done, Action<ApiError> failed)
        {
            return Call("GET", "/api/me", null, done, failed);
        }

        public IEnumerator Logout(Action done)
        {
            return Call<ApiError>("POST", "/api/logout", null, _ => done(), _ => done());
        }

        public IEnumerator Servers(Action<ServersReply> done, Action<ApiError> failed)
        {
            return Call("GET", "/api/servers", null, done, failed);
        }

        public IEnumerator CreateCharacter(int server, string name, Action<CharacterReply> done, Action<ApiError> failed)
        {
            return Call("POST", "/api/characters", new CharacterRequest { server = server, name = name }, done, failed);
        }

        private IEnumerator Call<T>(string method, string path, object body, Action<T> done, Action<ApiError> failed)
        {
            using var request = new UnityWebRequest(BaseUrl + path, method)
            {
                downloadHandler = new DownloadHandlerBuffer(),
                timeout = TimeoutSec,
            };

            if (body != null)
            {
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(JsonUtility.ToJson(body)));
                request.SetRequestHeader("Content-Type", "application/json");
            }

            if (!string.IsNullOrEmpty(Token))
            {
                request.SetRequestHeader("Authorization", "Bearer " + Token);
            }

            yield return request.SendWebRequest();

            string text = request.downloadHandler?.text;

            if (request.result == UnityWebRequest.Result.Success)
            {
                done(JsonUtility.FromJson<T>(text));
                yield break;
            }

            ApiError error = null;

            if (request.result == UnityWebRequest.Result.ProtocolError && !string.IsNullOrEmpty(text))
            {
                try
                {
                    error = JsonUtility.FromJson<ApiError>(text);
                }
                catch (ArgumentException)
                {
                }
            }

            failed(error != null && !string.IsNullOrEmpty(error.message)
                ? error
                : new ApiError { error = "network", message = $"Không kết nối được máy chủ ({request.error})." });
        }
    }
}

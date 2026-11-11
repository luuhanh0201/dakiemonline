using UnityEngine;

namespace DaKiem.Client.Accounts
{
    /// <summary>
    /// What this device remembers between runs. The password is never kept: "remember me" is the login
    /// token, which the server can end. The guest secret is what "Chơi ngay" uses to find the same guest
    /// again; losing it (uninstalling, clearing data) loses an unlinked guest, which is why the game
    /// keeps asking guests to link.
    /// </summary>
    public static class AccountStorage
    {
        private const string TokenKey = "account.token";
        private const string GuestSecretKey = "account.guestSecret";
        private const string UsernameKey = "account.username";
        private const string LastServerKey = "account.lastServer";
        private const string RememberKey = "account.remember";

        /// <summary>"Ghi nhớ đăng nhập": keep the login token after the game closes. On by default.</summary>
        public static bool Remember
        {
            get => PlayerPrefs.GetInt(RememberKey, 1) == 1;
            set
            {
                PlayerPrefs.SetInt(RememberKey, value ? 1 : 0);

                if (!value)
                {
                    PlayerPrefs.DeleteKey(TokenKey);
                }

                PlayerPrefs.Save();
            }
        }

        public static string Token
        {
            get => PlayerPrefs.GetString(TokenKey, string.Empty);
            set => Set(TokenKey, value);
        }

        public static string GuestSecret
        {
            get => PlayerPrefs.GetString(GuestSecretKey, string.Empty);
            set => Set(GuestSecretKey, value);
        }

        /// <summary>The last username typed, to fill the login screen.</summary>
        public static string Username
        {
            get => PlayerPrefs.GetString(UsernameKey, string.Empty);
            set => Set(UsernameKey, value);
        }

        public static int LastServer
        {
            get => PlayerPrefs.GetInt(LastServerKey, 0);
            set
            {
                PlayerPrefs.SetInt(LastServerKey, value);
                PlayerPrefs.Save();
            }
        }

        private static void Set(string key, string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                PlayerPrefs.DeleteKey(key);
            }
            else
            {
                PlayerPrefs.SetString(key, value);
            }

            PlayerPrefs.Save();
        }
    }
}

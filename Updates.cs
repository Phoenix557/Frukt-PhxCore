using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using MelonLoader;

namespace PhxCore
{
    /// <summary>
    /// Checks every installed Phoenix557 mod against the latest GitHub release of its repo when the game starts.
    /// Outdated mods are logged and listed in <see cref="UpdateModal"/>, which opens once all checks are back.
    /// </summary>
    static class Updates
    {
        const string Owner = "Phoenix557";

        static readonly Dictionary<string, string> KnownRepos = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "PhxCore", "Frukt-PhxCore" },
            { "Phx Pause", "Frukt-PhxPauseMenu" },
            { "Walking", "Frukt-Walking" },
            { "X-Ray", "Frukt-Xray-Organs" },
        };

        sealed class Check
        {
            public string Name;
            public string Version;
            public string Repo;
        }

        internal sealed class Outdated
        {
            public string Name;
            public string Installed;
            public string Latest;
            public string Url;
        }

        static readonly object Gate = new object();
        static readonly List<Outdated> Found = new List<Outdated>();
        static bool _started;
        static bool _done;
        static bool _handedOff;

        internal static void Start()
        {
            if (_started)
                return;
            _started = true;

            var checks = new List<Check>();
            foreach (MelonBase melon in MelonBase.RegisteredMelons)
            {
                if (melon?.Info == null)
                    continue;
                string repo = RepoOf(melon.Info);
                if (repo != null)
                    checks.Add(new Check { Name = melon.Info.Name, Version = melon.Info.Version, Repo = repo });
            }
            if (checks.Count == 0)
            {
                _done = true;
                return;
            }

            _ = CheckAll(checks);
        }

        static string RepoOf(MelonInfoAttribute info)
        {
            string link = info.DownloadLink;
            if (!string.IsNullOrEmpty(link))
            {
                const string marker = "github.com/" + Owner + "/";
                int at = link.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                if (at >= 0)
                {
                    string repo = link.Substring(at + marker.Length).Trim('/');
                    int slash = repo.IndexOf('/');
                    if (slash >= 0)
                        repo = repo.Substring(0, slash);
                    if (repo.Length > 0)
                        return repo;
                }
            }
            return KnownRepos.TryGetValue(info.Name, out string known) ? known : null;
        }

        static async Task CheckAll(List<Check> checks)
        {
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                http.DefaultRequestHeaders.UserAgent.ParseAdd("PhxCore-UpdateCheck");
                http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

                foreach (Check check in checks)
                {
                    try
                    {
                        using HttpResponseMessage response = await http.GetAsync("https://api.github.com/repos/" + Owner + "/" + check.Repo + "/releases/latest");
                        if (response.StatusCode == HttpStatusCode.NotFound)
                            continue;
                        if (!response.IsSuccessStatusCode)
                        {
                            MelonLogger.Warning("Could not check " + check.Name + " for updates (" + (int)response.StatusCode + ").");
                            continue;
                        }

                        using JsonDocument json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                        if (!json.RootElement.TryGetProperty("tag_name", out JsonElement tag))
                            continue;
                        string latest = tag.GetString();
                        if (!IsNewer(latest, check.Version))
                            continue;

                        string url = null;
                        if (json.RootElement.TryGetProperty("html_url", out JsonElement page))
                            url = page.GetString();
                        if (!IsOurRelease(url))
                            url = "https://github.com/" + Owner + "/" + check.Repo + "/releases/latest";

                        lock (Gate)
                            Found.Add(new Outdated { Name = check.Name, Installed = Clean(check.Version), Latest = Clean(latest), Url = url });
                        MelonLogger.Warning(check.Name + " v" + Clean(check.Version) + " is outdated, update to v" + Clean(latest) + ": " + url);
                    }
                    catch (Exception e)
                    {
                        MelonLogger.Warning("Could not check " + check.Name + " for updates: " + e.Message);
                    }
                }
            }
            finally
            {
                lock (Gate)
                    _done = true;
            }
        }

        /// <summary>Only ever open links to this author's GitHub releases.</summary>
        internal static bool IsOurRelease(string url)
        {
            return !string.IsNullOrEmpty(url)
                && url.StartsWith("https://github.com/" + Owner + "/", StringComparison.OrdinalIgnoreCase)
                && url.IndexOf("/releases", StringComparison.OrdinalIgnoreCase) > 0;
        }

        static string Clean(string version)
        {
            version = (version ?? "").Trim();
            return version.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? version.Substring(1) : version;
        }

        static bool IsNewer(string latest, string installed)
        {
            if (!Version.TryParse(Pad(Clean(latest)), out Version remote) || !Version.TryParse(Pad(Clean(installed)), out Version local))
                return false;
            return remote > local;
        }

        static string Pad(string version)
        {
            int dash = version.IndexOfAny(new[] { '-', '+' });
            if (dash >= 0)
                version = version.Substring(0, dash);
            return version.IndexOf('.') < 0 ? version + ".0" : version;
        }

        /// <summary>Main thread: once every check is back, hands the outdated list to the modal (once per launch).</summary>
        internal static void Tick()
        {
            if (!_handedOff)
            {
                List<Outdated> list = null;
                lock (Gate)
                {
                    if (_done)
                    {
                        _handedOff = true;
                        if (Found.Count > 0)
                            list = new List<Outdated>(Found);
                    }
                }
                if (list != null)
                {
                    list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
                    UpdateModal.Show(list);
                }
            }
            UpdateModal.Tick();
        }

        internal static void LateTick()
        {
            UpdateModal.LateTick();
        }
    }
}

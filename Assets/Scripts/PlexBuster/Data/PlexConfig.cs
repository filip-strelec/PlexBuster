using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace PlexBuster.Data
{
    /// <summary>
    /// Plex connection settings from environment variables, so the server address and token never live in
    /// code, scenes or git. <c>PLEX_URL</c> and <c>PLEX_TOKEN</c> come from the process environment, falling
    /// back to a git-ignored <c>.env</c> file (KEY=VALUE lines) next to the project folder (editor) or the
    /// .exe (build), then in persistentDataPath. Optional <c>PLEX_PLAYER</c> names the TV to preselect.
    /// </summary>
    public class PlexConfig
    {
        public const string UrlVariable = "PLEX_URL";
        public const string TokenVariable = "PLEX_TOKEN";
        public const string PlayerVariable = "PLEX_PLAYER";
        public const string EnvFileName = ".env";

        public string ServerUrl { get; private set; } = "";
        public string Token { get; private set; } = "";
        /// <summary>Name, model, id or address (or part of one) of the Plex player to offer first for playback.</summary>
        public string PreferredPlayer { get; private set; } = "";
        public string LoadedFrom { get; private set; } = "";
        public bool IsUsable => !string.IsNullOrWhiteSpace(ServerUrl) && !string.IsNullOrWhiteSpace(Token);

        public static PlexConfig Load()
        {
            var config = new PlexConfig();

            foreach (var path in CandidatePaths())
            {
                if (!File.Exists(path)) continue;
                var values = ReadEnvFile(path);
                if (values.TryGetValue(UrlVariable, out var url)) config.ServerUrl = url;
                if (values.TryGetValue(TokenVariable, out var token)) config.Token = token;
                if (values.TryGetValue(PlayerVariable, out var player)) config.PreferredPlayer = player;
                config.LoadedFrom = path;
                break;
            }

            // Real environment variables win over the file.
            var envUrl = Environment.GetEnvironmentVariable(UrlVariable);
            var envToken = Environment.GetEnvironmentVariable(TokenVariable);
            var envPlayer = Environment.GetEnvironmentVariable(PlayerVariable);
            if (!string.IsNullOrWhiteSpace(envUrl)) config.ServerUrl = envUrl.Trim();
            if (!string.IsNullOrWhiteSpace(envToken)) config.Token = envToken.Trim();
            if (!string.IsNullOrWhiteSpace(envPlayer)) config.PreferredPlayer = envPlayer.Trim();
            if (!string.IsNullOrWhiteSpace(envUrl) || !string.IsNullOrWhiteSpace(envToken))
                config.LoadedFrom = string.IsNullOrEmpty(config.LoadedFrom) ? "environment" : config.LoadedFrom + " + environment";

            return config;
        }

        public static IEnumerable<string> CandidatePaths()
        {
            // Application.dataPath is <project>/Assets in the editor and <game>/<name>_Data in a build.
            yield return Path.Combine(Directory.GetParent(Application.dataPath).FullName, EnvFileName);
            yield return Path.Combine(Application.persistentDataPath, EnvFileName);
        }

        /// <summary>Minimal dotenv: KEY=VALUE per line, # comments, optional surrounding quotes.</summary>
        static Dictionary<string, string> ReadEnvFile(string path)
        {
            var values = new Dictionary<string, string>();
            foreach (var raw in File.ReadAllLines(path))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                if (line.StartsWith("export ")) line = line.Substring(7).TrimStart();

                var equals = line.IndexOf('=');
                if (equals <= 0) continue;
                var key = line.Substring(0, equals).Trim();
                var value = line.Substring(equals + 1).Trim();
                if (value.Length >= 2 && (value[0] == '"' || value[0] == '\'') && value[^1] == value[0])
                    value = value.Substring(1, value.Length - 2);
                values[key] = value;
            }
            return values;
        }
    }
}

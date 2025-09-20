using System.Text.Json;
using Jellyfin.Plugin.Subdivx.Configuration;

namespace Jellyfin.Plugin.SubdivxTests;

public static class ConfigurationHelper
{
    public static PluginConfiguration LoadConfig()
    {
        var env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        var baseDir = TestContext.CurrentContext.TestDirectory;
        var configName = $"config{(string.IsNullOrWhiteSpace(env) ? "" : "." + env)}.json";
        var path = Path.Combine(baseDir, configName);

        var config = File.Exists(path)
            ? JsonSerializer.Deserialize<PluginConfiguration>(File.ReadAllText(path))
            : new PluginConfiguration();

        // Merge config.override.json (if present) after env-specific config
        var overridePath = Path.Combine(baseDir, "config.override.json");
        if (File.Exists(overridePath))
        {
            var overrideJson = File.ReadAllText(overridePath);
            try
            {
                using var doc = JsonDocument.Parse(overrideJson, new JsonDocumentOptions
                {
                    CommentHandling = JsonCommentHandling.Skip, // ignore // y /* ... */
                    AllowTrailingCommas = true                  // opcional: allows final comma in objects/arrays
                });
                
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    var type = typeof(PluginConfiguration);
                    var props = type.GetProperties();
                    foreach (var prop in doc.RootElement.EnumerateObject())
                    {
                        var targetProp = props.FirstOrDefault(p => string.Equals(p.Name, prop.Name, StringComparison.OrdinalIgnoreCase));
                        if (targetProp == null || !targetProp.CanWrite)
                            continue;

                        object value = null;
                        if (targetProp.PropertyType == typeof(string))
                        {
                            value = prop.Value.ValueKind == JsonValueKind.Null ? null : prop.Value.GetString();
                        }
                        else if (targetProp.PropertyType == typeof(bool))
                        {
                            // Accept boolean or string representations
                            if (prop.Value.ValueKind == JsonValueKind.String)
                            {
                                if (bool.TryParse(prop.Value.GetString(), out var b)) value = b;
                            }
                            else if (prop.Value.ValueKind == JsonValueKind.True || prop.Value.ValueKind == JsonValueKind.False)
                            {
                                value = prop.Value.GetBoolean();
                            }
                        }
                        else
                        {
                            // Fallback: try simple conversion from raw text
                            var raw = prop.Value.GetRawText();
                            try
                            {
                                value = System.Text.Json.JsonSerializer.Deserialize(raw, targetProp.PropertyType);
                            }
                            catch
                            {
                                // ignore if cannot convert
                            }
                        }

                        if (value != null || targetProp.PropertyType == typeof(string))
                        {
                            targetProp.SetValue(config, value);
                        }
                    }
                }
            }
            catch
            {
                // If override file is invalid JSON, ignore silently to avoid breaking tests
            }
        }

        var type_config = typeof(PluginConfiguration);
        // Read environment variables case-insensitively to support TOKEN vs Token, etc.
        var allEnv = Environment.GetEnvironmentVariables();
        var envDict = new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (System.Collections.DictionaryEntry entry in allEnv)
        {
            var key = entry.Key?.ToString();
            if (string.IsNullOrEmpty(key)) continue;
            envDict[key] = entry.Value?.ToString();
        }

        foreach (var prop in type_config.GetProperties())
        {
            if (!envDict.TryGetValue(prop.Name, out var envValue) || string.IsNullOrEmpty(envValue))
                continue;

            object converted = Convert.ChangeType(envValue, prop.PropertyType);
            prop.SetValue(config, converted);
        }

        return config;
    }
}

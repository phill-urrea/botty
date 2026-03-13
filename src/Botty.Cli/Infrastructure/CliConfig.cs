using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Botty.Cli.Infrastructure;

public class CliConfig
{
    [YamlMember(Alias = "api_url")]
    public string ApiUrl { get; set; } = "http://localhost:5001";

    [YamlMember(Alias = "api_key")]
    public string? ApiKey { get; set; }

    [YamlMember(Alias = "output_format")]
    public string OutputFormat { get; set; } = "table";

    private static string ConfigDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".botty");

    private static string ConfigPath => Path.Combine(ConfigDir, "config.yaml");

    public static CliConfig Load()
    {
        if (!File.Exists(ConfigPath))
            return new CliConfig();

        try
        {
            var yaml = File.ReadAllText(ConfigPath);
            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(UnderscoredNamingConvention.Instance)
                .IgnoreUnmatchedProperties()
                .Build();
            return deserializer.Deserialize<CliConfig>(yaml) ?? new CliConfig();
        }
        catch
        {
            return new CliConfig();
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(ConfigDir);
        var serializer = new SerializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .Build();
        File.WriteAllText(ConfigPath, serializer.Serialize(this));
    }
}

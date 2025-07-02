using Microsoft.Extensions.Configuration;

public static class ConfigHelper
{
    private static readonly IConfiguration config;

    static ConfigHelper()
    {
        config = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json")
            .Build();
    }

    public static string GetMesConnectionString()
    {
        string env = config["AppSettings:Environment"] ?? "PROD"; // 默认使用正式环境
        return config.GetConnectionString(env);
    }
}

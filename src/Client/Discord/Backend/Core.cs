using Microsoft.Extensions.Configuration;
using NetCord;
using NetCord.Gateway;
using NetCord.Logging;
using static NetCord.Gateway.GatewayIntents;

namespace Theta.Discord.Backend;

/// <summary>
/// Contains methods for initializing and preparing the bot.
/// </summary>
internal static class Core
{
	/// <summary>
	/// Main client, used for communicating with Discord.
	/// </summary>
	public static readonly GatewayClient Client = new(new BotToken(new ConfigurationBuilder().AddUserSecrets<Program>().Build().GetSection("DiscordToken").Value!), new()
	{ 
		Intents = Guilds | GuildUsers | GuildMessageReactions,
		Logger = new ConsoleLogger()
	});

	/// <summary>
	/// HTTP client, used for external network requests.
	/// </summary>
	public static readonly HttpClient NetClient = new();

	public static void Log(string message, LogLevel level) => Console.WriteLine($"{DateTime.Now,-12:T}System         {level switch { LogLevel.Information => "Info ", LogLevel.Error => "Fail ", LogLevel.Critical => "Crit ", _ => "     " }}    {message}");

	/// <summary>
	/// Logs messages from the system.
	/// </summary>
	/// <param name="message">The message to log.</param>
	public static async Task Start()
	{
		await Interactions.Initialization.SetupServices();
		await Client.StartAsync();
	}

	/// <summary>
	/// Stops the client, releases its resources, and restarts the bot.
	/// </summary>
	public static async Task Restart()
	{
		await Client.CloseAsync();
		Client.Dispose();

		Process.Start(Process.GetCurrentProcess().StartInfo);
		Environment.Exit(-1);
	}
}
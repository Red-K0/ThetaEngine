using Microsoft.Extensions.Configuration;
using NetCord;
using NetCord.Gateway;
using NetCord.Logging;
using static NetCord.Gateway.GatewayIntents;

namespace Discord;

/// <summary>
/// Contains methods for initializing and preparing the bot.
/// </summary>
internal static class Core
{
	public static readonly GatewayClient Client = new(new BotToken(new ConfigurationBuilder().AddUserSecrets<Program>().Build().GetSection("DiscordToken").Value!), new()
	{ 
		Intents = Guilds | GuildUsers | GuildMessageReactions,
		Logger = new ConsoleLogger()
	});

	public static async Task Start()
	{
		await Interactions.SetServices();

		await Client.StartAsync();
	}

	public static async Task Restart()
	{
		await Client.CloseAsync();
		Client.Dispose();

		Process.Start(Process.GetCurrentProcess().StartInfo);
		Environment.Exit(-1);
	}
}
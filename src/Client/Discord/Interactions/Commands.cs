using NetCord.Rest;
using NetCord.Services.ApplicationCommands;

namespace Theta.Discord.Interactions;

#pragma warning disable CA1724

/// <summary>
/// Contains the bot's slash commands and their associated tasks.
/// </summary>
public sealed partial class Commands : ApplicationCommandModule<SlashCommandContext>
{
	#region Attributes

	[SlashCommand("syscheck", "Check if the system's online.")]
	public partial Task SystemsCheck();

	#endregion Attributes

	/// <summary>
	/// Command task. Checks if the bot's command system is active.
	/// </summary>
	public async partial Task SystemsCheck()
	{
		Process currentProcess = Process.GetCurrentProcess();
		currentProcess.Refresh();

		await RespondAsync(InteractionCallback.Message($"""
		Time since client start: `{(DateTime.UtcNow - currentProcess.StartTime.ToUniversalTime()).TotalSeconds}`
		RAM in use: `{currentProcess.WorkingSet64 / 1024 / 1024}MB`
		"""));
	}

	#region Attributes

	[SlashCommand("use", "Check if the system's online.")]
	public partial Task UseItem
	(
		[SlashCommandParameter(Name = "item", Description = "The item to use.")]
		string name
	);

	#endregion Attributes

	/// <summary>
	/// Command task. Checks if the bot's command system is active.
	/// </summary>
	public async partial Task UseItem(string name)
	{
		if (name.Equals("overclock", StringComparison.OrdinalIgnoreCase))
		{
			await RespondAsync(InteractionCallback.Message(new()
			{
				Embeds = [
					new()
					{
						Title = "Overclocked",
						Color = new(58, 213, 127),
						Description = """
						```
						+ Speed increased to 60ft.
						+ Melee damage multiplied 2x.+ Melee damage multiplied 2x.+ Melee damage multiplied 2x.
						```
						""",
						Thumbnail = new("https://cdn.discordapp.com/emojis/1424153064406782117.webp")
					}
				]
			}));
		}
		else
		{
			await RespondAsync(InteractionCallback.Message($"Could not find an item with the name {name}"));
		}
	}

}
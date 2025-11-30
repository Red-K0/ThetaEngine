using Engine;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;

namespace Discord.Commands;

public sealed partial class Commands : ApplicationCommandModule<SlashCommandContext>
{
	[SlashCommand("save", "Saves the current state of the client.")]
	public async Task SaveState()
	{
		await RespondAsync(InteractionCallback.DeferredMessage());

		Serialization.Save();

		await FollowupAsync("State saved successfully.");
	}

	[SlashCommand("load", "Loads a previously saved client state from disk.")]
	public async Task LoadState(string file)
	{
		await RespondAsync(InteractionCallback.DeferredMessage());

		Serialization.Load(file);

		await FollowupAsync("Save file loaded succesfully.");
	}
}
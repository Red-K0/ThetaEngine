using static Theta.Discord.Backend.Core;
using NetCord.Services.ComponentInteractions;
using NetCord.Services.ApplicationCommands;
using NetCord.Services;
using NetCord;

namespace Theta.Discord.Interactions;

/// <summary>
/// Contains methods responsible for handling interactions.
/// </summary>
internal static partial class Initialization
{
	private static readonly ComponentInteractionService<ButtonInteractionContext> _buttonService = new();
	private static readonly ComponentInteractionService<ModalInteractionContext> _modalService = new();
	private static readonly ApplicationCommandService<SlashCommandContext> _commandService = new();

	/// <summary>
	/// Maps the <see cref="Client"/>'s events to their appropriate response method.
	/// </summary>
	public static async Task SetupServices()
	{
		_commandService.AddModule<Commands>();

		Client.InteractionCreate += HandleInteraction;

		await _commandService.RegisterCommandsAsync(Client.Rest, Client.Id);
	}

	public static async ValueTask HandleInteraction(Interaction interaction)
	{
		IExecutionResult result = await (interaction switch
		{
			SlashCommandInteraction c => _commandService.ExecuteAsync(new(c, Client)),
			      ButtonInteraction b =>  _buttonService.ExecuteAsync(new(b, Client)),
			       ModalInteraction m =>   _modalService.ExecuteAsync(new(m, Client)),

			_ => throw new InvalidOperationException()
		});

		if (result is IFailResult error) Log(error.Message, NetCord.Logging.LogLevel.Error);

	}
}
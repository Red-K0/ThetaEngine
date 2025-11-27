using NetCord;
using NetCord.Rest;
using NetCord.Services;
using NetCord.Services.ApplicationCommands;
using NetCord.Services.ComponentInteractions;

namespace Discord;

internal static class Interactions
{
	private static readonly ComponentInteractionService<ButtonInteractionContext> _buttons = new();
	private static readonly ComponentInteractionService<ModalInteractionContext> _modals = new();
	private static readonly ApplicationCommandService<SlashCommandContext> _commands = new();

	public static async Task SetServices()
	{
		_commands.AddModules(typeof(Program).Assembly);
		_buttons.AddModules(typeof(Program).Assembly);
		_modals.AddModules(typeof(Program).Assembly);

		Core.Client.InteractionCreate += async i =>
		{
			IExecutionResult result = await (i switch
			{
				SlashCommandInteraction c => _commands.ExecuteAsync(new(c, Core.Client)),
				ButtonInteraction b => _buttons.ExecuteAsync(new(b, Core.Client)),
				ModalInteraction m => _modals.ExecuteAsync(new(m, Core.Client)),

				_ => throw new InvalidOperationException()
			});

			if (result is IFailResult e) await i.SendResponseAsync(InteractionCallback.Message(e.Message));
		};

		await _commands.RegisterCommandsAsync(Core.Client.Rest, Core.Client.Id);
	}
}

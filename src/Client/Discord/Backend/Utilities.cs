using NetCord;

namespace Theta.Discord.Backend;
internal static class Utilities
{
	/// <summary>
	/// The hex code for the bot's default color code.
	/// </summary>
	public const int DefaultColor = 0x72767D;

	/// <summary>
	/// Generates a 24-bit integer from the current time in ticks.
	/// </summary>
	public static Color RandomColor => new(Environment.TickCount & 0xFFFFFF);

	/// <summary>
	/// Returns a user's display name.
	/// </summary>
	public static string GetDisplayName(this GuildUser user) => user.Nickname ?? user.GlobalName ?? user.Username;
}

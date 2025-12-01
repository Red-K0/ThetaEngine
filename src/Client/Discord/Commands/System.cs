using System.Text;
using Engine;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;
using Registries;

namespace Discord.Commands;

public sealed class System : ApplicationCommandModule<SlashCommandContext>
{
	private static string FormatBytes(double bytes)
	{
		ReadOnlySpan<string> sizes = ["B", "KB", "MB", "GB", "TB"];

		int order = 0;

		while (bytes >= 1024 && order < sizes.Length - 1)
		{
			bytes /= 1024;
			order++;
		}

		return $"{bytes:0.##} {sizes[order]}";
	}

	[SlashCommand("query", "Queries the state of a system parameter.")]
	public async Task Query(QueryType type)
	{
		EmbedProperties embed = new() { Title = "Query Results" };

		switch (type)
		{
			case QueryType.EntityTracker:
				embed.Description = $"""
				```ansi
				Entity Tracker State:           
				- Next Reference ID: {EntityRegistry.NextReference}
				- Held References:   {EntityRegistry.HeldReferences.Count}
				- Free References:   {EntityRegistry.FreedReferences.Count}
				```
				""";
				break;
			case QueryType.MemoryUsage:
				GCMemoryInfo info = GC.GetGCMemoryInfo();
				Process proc = Process.GetCurrentProcess();

				embed.Description = $"""
				```ansi
				GC Memory Info:                   
				- Concurrent:         {info.Concurrent}
				- Compacted:          {info.Compacted}

				Generations:
				- Generation 0
					- Collections:      {GC.CollectionCount(0)}
					- Size:             {FormatBytes(info.GenerationInfo[0].SizeAfterBytes)}
				- Generation 1
					- Collections:      {GC.CollectionCount(1)}
					- Size:             {FormatBytes(info.GenerationInfo[1].SizeAfterBytes)}
				- Generation 2
					- Collections:      {GC.CollectionCount(2)}
					- Size:             {FormatBytes(info.GenerationInfo[2].SizeAfterBytes)}

				Heap:
				- Total Available:    {FormatBytes(info.TotalAvailableMemoryBytes)}
				- Total Committed:    {FormatBytes(info.TotalCommittedBytes)}
				- Fragmentation:      {FormatBytes(info.FragmentedBytes)}
				- Memory Load:        {FormatBytes(info.MemoryLoadBytes)}
				- Heap Size:          {FormatBytes(info.HeapSizeBytes)}

				Finalization:
				- Pending Finalizers: {info.FinalizationPendingCount}
				- Pinned Objects:     {info.PinnedObjectsCount}

				Pause Information:
				- Current GC Index:   {info.Index}
				- GC Pause Count:     {info.PauseDurations.Length}

				Process Memory:
				- Virtual Memory:     {FormatBytes(proc.VirtualMemorySize64)}
				- Private Memory:     {FormatBytes(proc.PrivateMemorySize64)}
				- Working Set:        {FormatBytes(proc.WorkingSet64)}
				```
				""";
				break;
			case QueryType.SaveFiles:
				if (!Directory.Exists(Serialization.SaveDataFolder))
				{
					embed.Description = "No save files found.";
					break;
				}

				StringBuilder sb = new();

				sb.AppendLine("```ansi");
				sb.AppendLine("Recent Save Files:                      ");

				foreach (string file in Directory.GetFiles(Serialization.SaveDataFolder, "*.ths").OrderDescending().Take(5))
				{
					string name = Path.GetFileNameWithoutExtension(file);

					sb.AppendLine($"📄 {DateTimeOffset.FromUnixTimeSeconds(long.Parse(name)).UtcDateTime} ({name}.ths)");
				}

				sb.AppendLine("```");

				embed.Description = sb.ToString();
				break;
		}

		await RespondAsync(InteractionCallback.Message(new() { Embeds = [embed]}));
	}

	public enum QueryType
	{
		EntityTracker,
		MemoryUsage,
		SaveFiles,
	}
}

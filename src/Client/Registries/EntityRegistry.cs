using System.Collections.Concurrent;
using Engine;

namespace Registries;

internal static class EntityRegistry
{
	public static readonly Lock SaveLoadLock = new();

	public static ConcurrentDictionary<uint, Entity> HeldReferences = new();
	public static ConcurrentQueue<uint> FreedReferences = new();
	public static uint NextReference = 0;

	public static void AssignReference(Entity target)
	{
		lock (SaveLoadLock)
		{
			if (FreedReferences.TryDequeue(out uint reference))
			{
				if (!HeldReferences.TryAdd(reference, target)) FailWithMessage(reference, $"Reference '{reference}' was marked as freed, but is still in use.");
			}
			else
			{
				reference = Interlocked.Increment(ref NextReference);

				if (!HeldReferences.TryAdd(reference, target)) FailWithMessage(reference, $"Reference tracker out of sync. Expected reference '{reference}' to be unused, actual highest was {HeldReferences.Keys.Max()}.");
			}

			target.Reference = reference;
		}
	}

	public static void FreeReference(uint reference)
	{
		lock (SaveLoadLock)
		{
			if (!HeldReferences.TryRemove(reference, out _)) throw new ReferenceTrackerException(reference, $"A double free occurred for reference '{reference}'.");

			FreedReferences.Enqueue(reference);
		}
	}

	public static bool TryGetEntity(uint reference, [NotNullWhen(true)] out Entity? entity)
	{
		lock (SaveLoadLock) return HeldReferences.TryGetValue(reference, out entity);
	}

	[DoesNotReturn] static void FailWithMessage(uint reference, string message) => throw new(message, new ReferenceTrackerException(reference, message));

	public static void ClearState()
	{
		HeldReferences.Clear();

		GC.Collect(2, GCCollectionMode.Forced, true, false);

		GC.WaitForPendingFinalizers();

		FreedReferences.Clear();

		NextReference = 0;
	}

	private class ReferenceTrackerException : Exception
	{
		public uint Reference { get; }

		public object? Target => HeldReferences.GetValueOrDefault(Reference);

		public ReferenceTrackerException(uint reference) : base($"Unknown reference tracker error for reference '{reference}'.") => Reference = reference;
		public ReferenceTrackerException(uint reference, string message) : base(message) => Reference = reference;
		public ReferenceTrackerException(uint reference, string message, Exception innerException) : base(message, innerException) => Reference = reference;
	}
}

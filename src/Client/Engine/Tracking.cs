using System.Collections.Concurrent;

namespace Theta.Engine;

internal static class Tracking
{
	private static readonly ConcurrentDictionary<uint, Entity> _heldReferences = new();
	private static readonly ConcurrentQueue<uint> _freedReferences = new();
	private static uint _nextReference = 0;

	public static Serialization.TrackerSnapshot SaveSnapshot() => new(_nextReference, [.. _freedReferences], [.. _heldReferences.Values.Select(p => p.Save())]);

	public static void LoadSnapshot(Serialization.TrackerSnapshot snapshot)
	{
		ClearState();

		_nextReference = snapshot.NextReference;

		for (int i = 0; i < snapshot.FreedReferences.Length; i++) _freedReferences.Enqueue(snapshot.FreedReferences[i]);

		for (int i = 0; i < snapshot.Entities.Length; i++) _heldReferences.TryAdd(snapshot.Entities[i].Reference, Serialization.LoadEntity(snapshot.Entities[i]));
	}

	private static void ClearState()
	{
		foreach (Entity item in _heldReferences.Values) item.ClearData();

		_heldReferences.Clear();

		GC.Collect(2, GCCollectionMode.Forced, true, false);

		GC.WaitForPendingFinalizers();

		_freedReferences.Clear();

		_nextReference = 0;
	}

	public static void AssignReference(Entity target)
	{
		if (_freedReferences.TryDequeue(out uint reference))
		{
			if (!_heldReferences.TryAdd(reference, target)) FailWithMessage(reference, $"Reference '{reference}' was marked as freed, but is still in use.");
		}
		else
		{
			reference = Interlocked.Increment(ref _nextReference);

			if (!_heldReferences.TryAdd(reference, target)) FailWithMessage(reference, $"Reference tracker out of sync. Expected reference '{reference}' to be unused, actual highest was {_heldReferences.Keys.Max()}.");
		}

		target.Reference = reference;
	}

	public static void FreeReference(uint reference)
	{
		if (!_heldReferences.TryRemove(reference, out _)) throw new ReferenceTrackerException(reference, $"A double free occurred for reference '{reference}'.");

		_freedReferences.Enqueue(reference);
	}

	public static bool TryGetEntity(uint reference, [NotNullWhen(true)] out Entity? entity) => _heldReferences.TryGetValue(reference, out entity);

	[DoesNotReturn] static void FailWithMessage(uint reference, string message) => throw new(message, new ReferenceTrackerException(reference, message));

	private class ReferenceTrackerException : Exception
	{
		public uint Reference { get; }

		public object? Target => _heldReferences.GetValueOrDefault(Reference);

		public ReferenceTrackerException(uint reference) : base($"Unknown reference tracker error for reference '{reference}'.") => Reference = reference;
		public ReferenceTrackerException(uint reference, string message) : base(message) => Reference = reference;
		public ReferenceTrackerException(uint reference, string message, Exception innerException) : base(message, innerException) => Reference = reference;
	}
}

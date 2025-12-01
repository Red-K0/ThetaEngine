using static Registries.EntityRegistry;

namespace Engine;

internal abstract class Entity
{
	public static explicit operator Entity(uint a) => TryGetEntity(a, out Entity? b) ? b : throw new KeyNotFoundException($"Reference '{a}' not found.");
	public static explicit operator uint(Entity a) => a.Reference;

	public uint Reference;

	public Entity() => AssignReference(this);

	~Entity() => FreeReference(Reference);
}

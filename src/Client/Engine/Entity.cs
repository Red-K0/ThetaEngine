using static Engine.Serialization;
using static Engine.Tracking;
using System.Reflection;

namespace Engine;

internal abstract class Entity
{
	public static explicit operator Entity(uint a) => TryGetEntity(a, out Entity? b) ? b : throw new KeyNotFoundException($"Reference '{a}' not found.");
	public static explicit operator uint(Entity a) => a.Reference;

	public uint Reference;

	protected Entity(EntityRecord record) { Reference = record.Reference; LoadData(record.Data); }

	public Entity() => AssignReference(this);

	public EntityRecord Save() => new(Reference, GetType().GetCustomAttribute<EntityMarkerAttribute>()!.Id, SaveData());

	public virtual void ClearData() { }

	protected virtual object[] SaveData() => [];

	protected virtual void LoadData(object[] data) { }

	~Entity() => FreeReference(Reference);
}

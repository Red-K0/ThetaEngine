using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Text;
using Registries;

namespace Engine;

[AttributeUsage(AttributeTargets.Class)]
internal sealed class EntityMarkerAttribute(ushort id, byte fieldCount) : Attribute { public ushort Id { get; } = id; public byte FieldCount { get; } = fieldCount; }

internal static class Serialization
{
	private static readonly uint _magic = BitConverter.ToUInt32("THTA"u8);

	public static FrozenDictionary<ushort, Func<EntityRecord, Entity>> EntityConstructors { get; private set; } = null!;
	public static FrozenDictionary<Type, ushort> TypeToIdMap { get; private set; } = null!;
	public static FrozenDictionary<ushort, Type> IdToTypeMap { get; private set; } = null!;
	public static string SaveDataFolder { get; private set; } = null!;

	static Serialization() { SetupSaveFolder(); GetEntityMaps(); }

	public static T Assign<T>(this object[] array, ref T field, byte key) => field = (T)array[key];

	public static string Save()
	{
		lock (EntityRegistry.SaveLoadLock)
		{
			using FileStream fs = File.Create(Path.Combine(SaveDataFolder, $"{DateTimeOffset.Now.ToUniversalTime().ToUnixTimeSeconds()}.ths"));
			using BinaryWriter writer = new(fs, Encoding.UTF8, false);

			writer.WriteStruct(new FileHeader(_magic, EntityRegistry.NextReference));

			for (uint i = 0; i < EntityRegistry.NextReference; i++)
			{
				if (EntityRegistry.HeldReferences.TryGetValue(i, out Entity? value))
				{
					writer.Write(value);
				}
				else
				{
					writer.Write(ushort.MaxValue);
				}
			}

			return fs.Name;
		}
	}

	public static void Load(string fileName)
	{
		lock (EntityRegistry.SaveLoadLock)
		{
			PlayerRegistry.ClearState();

			// This must run last.
			EntityRegistry.ClearState();

			using FileStream fs = File.Open(Path.Combine(SaveDataFolder, fileName.EndsWith(".ths") ? fileName : $"{fileName}.ths"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
			using BinaryReader r = new(fs, Encoding.UTF8, false);

			FileHeader file = r.ReadStruct<FileHeader>();

			if (file.Magic != _magic) throw new IOException("File header mismatch.");

			EntityRegistry.NextReference = file.ReferenceCount;

			(List<Entity> entities, List<uint> freed) = r.ReadAllEntities(EntityRegistry.NextReference);

			EntityRegistry.HeldReferences = new ConcurrentDictionary<uint, Entity>(entities.Select(e => new KeyValuePair<uint, Entity>(e.Reference, e)));

			EntityRegistry.FreedReferences = new ConcurrentQueue<uint>(freed);
		}
	}

	#region Data Structures

	internal record struct EntityRecord(uint Reference, ushort TypeID, object[] Data) { public readonly Entity Load() => EntityConstructors[TypeID](this); }

	[StructLayout(LayoutKind.Sequential, Pack = 1, Size = 8)]
	private record struct FileHeader(uint Magic, uint ReferenceCount);

	#endregion

	#region Initialization

	public static void SetupSaveFolder()
	{
		SaveDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Theta");

		if (!Directory.Exists(SaveDataFolder)) Directory.CreateDirectory(SaveDataFolder);
	}

	public static void GetEntityMaps()
	{
		Dictionary<ushort, Func<EntityRecord, Entity>> constructors = [];
		Dictionary<Type, ushort> typeIDs = []; Dictionary<ushort, Type> types = [];

		Type[] arguments = [typeof(EntityRecord)];

		foreach (Type? type in typeof(Entity).Assembly.GetTypes().Where(t => t.IsSealed && t.IsEntity))
		{
			EntityMarkerAttribute? attr = type.GetCustomAttribute<EntityMarkerAttribute>();

			if (attr is null) continue;

			constructors.Add(attr.Id, e => (Entity)type.GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, arguments)!.Invoke([e]));
			typeIDs.Add(type, attr.Id);
			types.Add(attr.Id, type);
		}

		EntityConstructors = constructors.ToFrozenDictionary();
		TypeToIdMap = typeIDs.ToFrozenDictionary();
		IdToTypeMap = types.ToFrozenDictionary();
	}

	#endregion

	#region Reader/Writer Extensions

	private static unsafe T ReadStruct<T>(this BinaryReader reader) where T : unmanaged 
	{
		byte[] bytes = reader.ReadBytes(sizeof(T));

		fixed (byte* ptr = bytes) return Marshal.PtrToStructure<T>((nint)ptr);
	}

	/// <summary>
	/// Reads all entities present in file and returns them fully populated.<br/>
	/// - Each <see cref="Entity"/> begins with its type ID.<br/>
	/// - All fields are serialized, excluding the reference at the end.<br/>
	/// - Written references to entities are converted to their full entity objects.<br/>
	/// - An array is written as its <see cref="int"/> length, followed by its elements in order.<br/>
	/// - Entity instances are created in read order and assigned consecutive references.<br/>
	/// </summary>
	public static (List<Entity>, List<uint>) ReadAllEntities(this BinaryReader reader, uint referenceCount)
	{
		List<Entity> entities = []; List<Action> fixups = []; List<uint> freed = [];
		Dictionary<uint, Entity> referenceMap = [];

		Stream s = reader.BaseStream;

		for (uint i = 0; i < referenceCount; i++)
		{
			// Read entity type index to determine actual type.
			ushort typeIndex = reader.ReadUInt16();

			// This is a freed reference.
			if (typeIndex == ushort.MaxValue)
			{
				freed.Add(i);
				continue;
			}

			Type entityType = IdToTypeMap[typeIndex];

			// Create uninitialized instance to assign data to.
			Entity entity = (Entity)RuntimeHelpers.GetUninitializedObject(entityType)!;

			// Assign a new reference id to this entity. Traditionally the reference would be pulled directly from the 'Reference' field, but this is inefficient.
			// Entities are written in reference order, with gaps represented by filler entities.
			SetEntityReference(entity, i);

			// Register immediately so that forward/back references can reference earlier entities.
			entities.Add(referenceMap[i] = entity);

			// Get fields and iterate all except last (the entity's own reference).
			FieldInfo[] fields = entityType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy | BindingFlags.Instance);

			foreach (FieldInfo field in fields.SkipLast(1))
			{
				Type fieldType = field.FieldType;

				object? value = ReadArbitraryObjectWithFixups(reader, fieldType, fixups, referenceMap, entity, field);

				// If the read returns a resolved object (i.e., not an entity placeholder), set it now.
				// If value is placeholder, a fixup will set it later.
				if (value != PlaceholderForDeferred.Value) field.SetValue(entity, value);
			}
		}

		// Run fixups now that all entities exist and their assigned references are known.
		foreach (Action f in fixups) f();

		return (entities, freed);

		static object? ReadArbitraryObjectWithFixups(BinaryReader reader, Type type, List<Action> fixups, Dictionary<uint, Entity> referenceMap, Entity currentEntity, FieldInfo targetField)
		{
			if (type == typeof(string))
			{
				return reader.ReadString();
			}
			else if (type.IsValueType)
			{
				if (type == typeof(nint)) return (nint)reader.ReadUInt64();
				if (type == typeof(nuint)) return (nuint)reader.ReadUInt64();

				switch (Type.GetTypeCode(type))
				{
					case TypeCode.Boolean: return reader.ReadBoolean();
					case TypeCode.   Char: return reader.ReadChar();
					case TypeCode.  SByte: return reader.ReadSByte();
					case TypeCode.   Byte: return reader.ReadByte();
					case TypeCode.  Int16: return reader.ReadInt16();
					case TypeCode. UInt16: return reader.ReadUInt16();
					case TypeCode.  Int32: return reader.ReadInt32();
					case TypeCode. UInt32: return reader.ReadUInt32();
					case TypeCode.  Int64: return reader.ReadInt64();
					case TypeCode. UInt64: return reader.ReadUInt64();
					case TypeCode. Single: return reader.ReadSingle();
					case TypeCode. Double: return reader.ReadDouble();
					case TypeCode.Decimal: return reader.ReadDecimal();
					case TypeCode. Object:
						{
							if (!type.IsBlittable)
							{
								// Expect a static Read(BinaryReader) method.
								// TODO: Add analyzer for this.
								MethodInfo? readMethod = type.GetMethod("Read", BindingFlags.Public | BindingFlags.Static) ?? throw new ArgumentException("Non-blittable structs set as entity fields must define a static Read(BinaryReader) method.");

								return readMethod.Invoke(null, [reader]);
							}

							// For bittables, read raw bytes and marshal into a value type instance.
							unsafe
							{
								int size = type.Size;

								byte* buffer = stackalloc byte[size];

								reader.ReadExactly(new(buffer, size));

								return Marshal.PtrToStructure((nint)buffer, type);
							}
						}
				}
			}
			else if (type.IsEntity)
			{
				// Writer wrote the entity's reference as an uint.
				uint reference = reader.ReadUInt32();

				if (referenceMap.TryGetValue(reference, out Entity? existing))
				{
					return existing;
				}
				else
				{
					// Defer: create a fixup to fill targetField on currentEntity when references are available.
					fixups.Add(() =>
					{
						if (!referenceMap.TryGetValue(reference, out Entity? resolved)) throw new SerializationException($"Unresolved entity reference id {reference} during deserialization.");
						targetField.SetValue(currentEntity, resolved);
					});

					return PlaceholderForDeferred.Value;
				}
			}
			else if (type.IsSZArray)
			{
				int length = reader.ReadInt32();
				Type elementType = type.GetElementType()!;
				Array array = Array.CreateInstance(elementType, length);

				if (elementType.IsEntity)
				{
					// For entity elements we read int references and create fixups for unresolved ones.
					for (int i = 0; i < length; i++)
					{
						uint reference = reader.ReadUInt32();
						if (referenceMap.TryGetValue(reference, out Entity? existingElement))
						{
							array.SetValue(existingElement, i);
						}
						else
						{
							int localIndex = i;
							fixups.Add(() =>
							{
								if (!referenceMap.TryGetValue(reference, out Entity? resolvedElement)) throw new SerializationException($"Unresolved entity ID {reference} while resolving array element.");

								array.SetValue(resolvedElement, localIndex);
							});
						}
					}

					// If the field itself is an entity array and some elements were deferred, set the array via a fixup later so that the field is assigned after resolution.
					// But if no deferred elements exist, return array now.
					if (fixups.Count != 0)
					{
						fixups.Add(() => targetField.SetValue(currentEntity, array));
						return PlaceholderForDeferred.Value;
					}

					return array;
				}
				else
				{
					// For all non-entity element types.
					for (int i = 0; i < length; i++)
					{
						object? element = ReadArbitraryObjectWithFixups(reader, elementType, fixups, referenceMap, currentEntity, targetField);

						if (element == PlaceholderForDeferred.Value)
						{
							// This path should never happen.
							fixups.Add(Debugger.Break);
						}
						else
						{
							array.SetValue(element, i);
						}
					}

					return array;
				}
			}
			else
			{
				throw new SerializationException("An untracked reference type was found in an entity's fields.");
			}

			return null;
		}

		static void SetEntityReference(Entity e, uint reference)
		{
			// Reference is intentionally private, but must be accessed and assigned here (the standard object tracker is not reliable during deserialization).
			FieldInfo refField = e.GetType().GetField("Reference", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy)!;
			refField.SetValue(e, reference);
		}
	}

	private static void Write(this BinaryWriter writer, Entity entity)
	{
		Type entityType = entity.GetType();

		writer.Write(TypeToIdMap[entityType]);

		FieldInfo[] fields = entityType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy | BindingFlags.Instance);

		// Last is always Reference, as Entity is the base class, and exposes only this.
		foreach (FieldInfo field in fields.SkipLast(1)) writer.WriteArbitraryObject(field.GetValue(entity), field.FieldType);
	}

	private static unsafe void WriteArbitraryObject(this BinaryWriter writer, object? value, Type type)
	{
		if (value is string str)
		{
			writer.Write(str);
		}
		else if (type.IsValueType)
		{
			if (value is nint or nuint)
			{
				writer.Write((ulong)value!);
			}
			else
			{
				switch (Type.GetTypeCode(type))
				{
					case TypeCode.Boolean: writer.Write(   (bool)value!); break;
					case TypeCode.   Char: writer.Write(   (char)value!); break;
					case TypeCode.  SByte: writer.Write(  (sbyte)value!); break;
					case TypeCode.   Byte: writer.Write(   (byte)value!); break;
					case TypeCode.  Int16: writer.Write(  (short)value!); break;
					case TypeCode. UInt16: writer.Write( (ushort)value!); break;
					case TypeCode.  Int32: writer.Write(    (int)value!); break;
					case TypeCode. UInt32: writer.Write(   (uint)value!); break;
					case TypeCode.  Int64: writer.Write(   (long)value!); break;
					case TypeCode. UInt64: writer.Write(  (ulong)value!); break;
					case TypeCode. Single: writer.Write(  (float)value!); break;
					case TypeCode. Double: writer.Write( (double)value!); break;
					case TypeCode.Decimal: writer.Write((decimal)value!); break;

					case TypeCode.Object:
						if (type.IsBlittable)
						{
							GCHandle handle = GCHandle.Alloc(value, GCHandleType.Pinned);

							try
							{
								writer.Write(new Span<byte>((byte*)handle.AddrOfPinnedObject() + (sizeof(nint) * 2), type.Size));
							}
							finally
							{
								handle.Free();
							}
						}
						else
						{
							MethodInfo? method = type.GetMethod("Write") ?? throw new ArgumentException("Non-blittable structs set as entity fields must define a Write(BinaryWriter) method.");

							method.Invoke(value, [writer]);
						}
						break;
				}
			}
		}
		else if (type.IsEntity)
		{
			writer.Write(((Entity)value!).Reference);
		}
		else if (type.IsSZArray)
		{
			object[] array = (object[])value!;

			writer.Write(array.Length);

			foreach (object item in array) writer.WriteArbitraryObject(item, item.GetType());
		}
		else
		{
			throw new SerializationException("An untracked reference type was found in an entity's fields.");
		}
	}

	private static unsafe void WriteStruct<T>(this BinaryWriter writer, T source) where T : unmanaged 
	{
		byte* ptr = stackalloc byte[sizeof(T)];

		Marshal.StructureToPtr(source, (nint)ptr, false);

		writer.Write(new Span<byte>(ptr, sizeof(T)));
	}

	#endregion

	/// <summary>
	/// A placeholder for entity references that are valid, but not yet loaded.
	/// </summary>
	private static class PlaceholderForDeferred { public static readonly object Value = new(); }
}

using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace Engine;

[AttributeUsage(AttributeTargets.Class)]
internal sealed class EntityTypeIdAttribute(short id) : Attribute { public short Id { get; } = id; }

internal static class Serialization
{
	private const uint Magic = 0x41544854; // "THTA"
	private const uint Version = 1;

	[StructLayout(LayoutKind.Explicit)]
	private readonly struct FileHeader(uint magic, uint version, uint checksum, uint flags,  int heldCount,  int freeCount, uint nextRef, uint dataOffset)
	{
		[FieldOffset(0x00)] public readonly uint Magic = magic;
		[FieldOffset(0x04)] public readonly uint Version = version;
		[FieldOffset(0x08)] public readonly uint Checksum = checksum;
		[FieldOffset(0x0C)] public readonly uint Flags = flags;
		[FieldOffset(0x10)] public readonly  int HeldReferenceCount = heldCount;
		[FieldOffset(0x14)] public readonly  int FreeReferenceCount = freeCount;
		[FieldOffset(0x18)] public readonly uint NextReferenceValue = nextRef;
		[FieldOffset(0x1C)] public readonly uint OffsetToData = dataOffset;
	}

	[StructLayout(LayoutKind.Explicit)]
	private readonly struct EntityHeader(EntityRecord record)
	{
		[FieldOffset(0x00)] public readonly  uint Reference = record.Reference;
		[FieldOffset(0x04)] public readonly short TypeID = record.TypeId;
		[FieldOffset(0x08)] public readonly  byte FieldCount = (byte)record.Data.Length;
		[FieldOffset(0x0C)] public readonly  byte Flags = 0xFF;
	}

	static Serialization()
	{
		foreach (Type? type in typeof(Entity).Assembly.GetTypes().Where(typeof(Entity).IsAssignableFrom))
		{
			_entityConstructors.Add(type.GetCustomAttribute<EntityTypeIdAttribute>()!.Id, e => (Entity)type.GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, [typeof(EntityRecord)])!.Invoke([e]));
		}
	}

	#region Serialization

	public static void SaveState(string path)
	{
		using FileStream fs = File.Create(path);
		using BinaryWriter w = new(fs, Encoding.UTF8, false);

		TrackerSnapshot snapshot = Tracking.SaveSnapshot();

		w.WriteStruct(new FileHeader(Magic, Version, 0, 0, snapshot.Entities.Length, snapshot.FreedReferences.Length, snapshot.NextReference, 0x20));

		foreach (EntityRecord e in snapshot.Entities)
		{
			w.WriteStruct(new EntityHeader(e));

			w.WriteArray(e.Data, false);
		}
	}

	private static void WriteArray(this BinaryWriter writer, object[] array, bool writeLength = true)
	{
		if (writeLength) writer.Write(writeLength);

		foreach (object value in array)
		{
			TypeCode code = Type.GetTypeCode(value!.GetType());

			if (code is TypeCode.Object or TypeCode.DBNull or TypeCode.DateTime) throw new NotSupportedException($"Unsupported value type {value.GetType()}");

			writer.Write((byte)code);

			switch (code)
			{
				case TypeCode.Boolean: writer.Write(   (bool)value); break;
				case TypeCode.   Char: writer.Write(   (char)value); break;
				case TypeCode.  SByte: writer.Write(  (sbyte)value); break;
				case TypeCode.   Byte: writer.Write(   (byte)value); break;
				case TypeCode.  Int16: writer.Write(  (short)value); break;
				case TypeCode. UInt16: writer.Write( (ushort)value); break;
				case TypeCode.  Int32: writer.Write(    (int)value); break;
				case TypeCode. UInt32: writer.Write(   (uint)value); break;
				case TypeCode.  Int64: writer.Write(   (long)value); break;
				case TypeCode. UInt64: writer.Write(  (ulong)value); break;
				case TypeCode. Single: writer.Write(  (float)value); break;
				case TypeCode. Double: writer.Write( (double)value); break;
				case TypeCode.Decimal: writer.Write((decimal)value); break;
				case TypeCode. String: writer.Write( (string)value); break;
			}
		}
	}

	private static unsafe void WriteStruct<T>(this BinaryWriter writer, T source) where T : unmanaged 
	{
		byte* ptr = stackalloc byte[sizeof(FileHeader)];

		Marshal.StructureToPtr(source, (nint)ptr, false);

		writer.Write(new Span<byte>(ptr, sizeof(FileHeader)));
	}

	#endregion

	#region Deserialization

	private static readonly Dictionary<short, Func<EntityRecord, Entity>> _entityConstructors = [];

	public static T Assign<T>(this object[] array, ref T field, byte key) => field = (T)array[key];

	public static Entity LoadEntity(EntityRecord record) => _entityConstructors[record.TypeId](record);

	public static void LoadState(string path)
	{
		using FileStream fs = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
		using BinaryReader r = new(fs, Encoding.UTF8, false);

		if (r.ReadUInt32() != Magic || r.ReadUInt32() != Version) throw new IOException("File header mismatch.");

		uint nextReference = r.ReadUInt32();

		uint[] freedReferences = new uint[r.ReadInt32()];

		for (int i = 0; i < freedReferences.Length; i++) freedReferences[i] = r.ReadUInt32();

		EntityRecord[] entities = new EntityRecord[(r.ReadInt32())];

		for (int i = 0; i < entities.Length; i++) entities[i] = new EntityRecord(r.ReadUInt32(), r.ReadInt16(), ReadDataMap(r, r.ReadByte()));

		Tracking.LoadSnapshot(new(nextReference, freedReferences, entities));
	}

	private static object[] ReadDataMap(BinaryReader r, byte count)
	{
		r.BaseStream.Position++; // Skip padding byte

		object[] map = new object[count];

		for (int i = 0; i < count; i++)
		{
			map[i] = (TypeCode)r.ReadByte() switch
			{
				TypeCode.Boolean => r.ReadBoolean(),
				TypeCode.   Char => r.ReadChar(),
				TypeCode.  SByte => r.ReadSByte(),
				TypeCode.   Byte => r.ReadByte(),
				TypeCode.  Int16 => r.ReadInt16(),
				TypeCode. UInt16 => r.ReadUInt16(),
				TypeCode.  Int32 => r.ReadInt32(),
				TypeCode. UInt32 => r.ReadUInt32(),
				TypeCode.  Int64 => r.ReadInt64(),
				TypeCode. UInt64 => r.ReadUInt64(),
				TypeCode. Single => r.ReadSingle(),
				TypeCode. Double => r.ReadDouble(),
				TypeCode.Decimal => r.ReadDecimal(),
				TypeCode. String => r.ReadString(),
				_ => throw new NotSupportedException($"Unsupported type found in save file.")
			};
		}

		return map;
	}

	#endregion

	internal record struct TrackerSnapshot(uint NextReference, uint[] FreedReferences, EntityRecord[] Entities);

	internal record struct EntityRecord(uint Reference, short TypeId, object[] Data);
}

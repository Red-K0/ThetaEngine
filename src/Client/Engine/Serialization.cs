using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace Engine;

[AttributeUsage(AttributeTargets.Class)]
internal sealed class EntityMarkerAttribute(ushort id, byte fieldCount) : Attribute { public ushort Id { get; } = id; public byte FieldCount { get; } = fieldCount; }

internal static class Serialization
{
	private static readonly uint _magic = BitConverter.ToUInt32("THTA"u8);

	public static FrozenDictionary<ushort, Func<EntityRecord, Entity>> EntityConstructors { get; private set; } = null!;
	public static FrozenDictionary<ushort, byte> EntityFieldCounts { get; private set; } = null!;
	public static string SaveDataFolder { get; private set; } = null!;

	static Serialization() { SetupSaveFolder(); GetEntityMaps(); }

	public static T Assign<T>(this object[] array, ref T field, byte key) => field = (T)array[key];

	public static string Save()
	{
		lock (Tracking.SaveLoadLock)
		{
			using FileStream fs = File.Create(Path.Combine(SaveDataFolder, $"{DateTimeOffset.Now.ToUniversalTime().ToUnixTimeSeconds()}.ths"));
			using BinaryWriter writer = new(fs, Encoding.UTF8, false);

			writer.WriteStruct<FileHeader>(new(_magic, Tracking.NextReference));

			EntityHeader[] headers = new EntityHeader[Tracking.NextReference];
			List<object[]> dataSet = [];

			for (uint i = 0; i < headers.Length; i++)
			{
				if (!Tracking.HeldReferences.TryGetValue(i, out Entity? value))
				{
					headers[i] = new(0xFFFF, 0xFFFF);
				}
				else
				{
					EntityRecord record = value.Save();

					headers[i] = new(record.TypeID, 0);

					dataSet.Add(record.Data);
				}
			}

			writer.WriteStructArray(headers);

			foreach (object[] data in dataSet) if (data.Length != 0) writer.WriteArray(data);

			return fs.Name;
		}
	}

	public static void Load(string fileName)
	{
		lock (Tracking.SaveLoadLock)
		{
			Tracking.ClearState();

			using FileStream fs = File.Open(Path.Combine(SaveDataFolder, fileName.EndsWith(".ths") ? fileName : $"{fileName}.ths"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
			using BinaryReader r = new(fs, Encoding.UTF8, false);

			FileHeader file = r.ReadStruct<FileHeader>();

			if (file.Magic != _magic) throw new IOException("File header mismatch.");

			Tracking.NextReference = file.ReferenceCount;

			EntityHeader[] headers = r.ReadStructArray<EntityHeader>((int)file.ReferenceCount);

			EntityRecord[] records = [.. headers.Where(h => h.TypeID != 0xFFFF).Select((h, i) => new EntityRecord((uint)i, h.TypeID, null!))];

			foreach ((int i, byte l) in records.Select((h, i) => (i, EntityFieldCounts[h.TypeID]))) records[i].Data = ReadArray(r, l);

			Tracking.HeldReferences = new ConcurrentDictionary<uint, Entity>(records.Select(r => new KeyValuePair<uint, Entity>(r.Reference, r.Load())));

			Tracking.FreedReferences = new ConcurrentQueue<uint>(headers.Where(h => h.TypeID == 0xFFFF).Select((h, i) => (uint)i));
		}
	}

	#region Data Structures

	internal record struct EntityRecord(uint Reference, ushort TypeID, object[] Data) { public readonly Entity Load() => EntityConstructors[TypeID](this); }

	[StructLayout(LayoutKind.Sequential, Pack = 1, Size = 8)]
	private record struct FileHeader(uint Magic, uint ReferenceCount);

	[StructLayout(LayoutKind.Sequential, Pack = 1, Size = 4)]
	private record struct EntityHeader(ushort TypeID, ushort Flags);

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
		Dictionary<ushort, byte> fieldCounts = [];

		Type[] arguments = [typeof(EntityRecord)];

		foreach (Type? type in typeof(Entity).Assembly.GetTypes().Where(t => t.IsSealed && typeof(Entity).IsAssignableFrom(t)))
		{
			EntityMarkerAttribute? attr = type.GetCustomAttribute<EntityMarkerAttribute>();

			if (attr is null) continue;

			constructors.Add(attr.Id, e => (Entity)type.GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, arguments)!.Invoke([e]));
			fieldCounts.Add(attr.Id, attr.FieldCount);
		}

		EntityConstructors = constructors.ToFrozenDictionary();
		EntityFieldCounts = fieldCounts.ToFrozenDictionary();
	}

	#endregion

	#region Reader/Writer Extensions

	private static object[] ReadArray(BinaryReader reader, byte count)
	{
		object[] map = new object[count];

		for (int i = 0; i < count; i++) map[i] = (TypeCode)reader.ReadByte() switch
		{
			TypeCode.Boolean => reader.ReadBoolean(),
			TypeCode.   Char => reader.ReadChar(),
			TypeCode.  SByte => reader.ReadSByte(),
			TypeCode.   Byte => reader.ReadByte(),
			TypeCode.  Int16 => reader.ReadInt16(),
			TypeCode. UInt16 => reader.ReadUInt16(),
			TypeCode.  Int32 => reader.ReadInt32(),
			TypeCode. UInt32 => reader.ReadUInt32(),
			TypeCode.  Int64 => reader.ReadInt64(),
			TypeCode. UInt64 => reader.ReadUInt64(),
			TypeCode. Single => reader.ReadSingle(),
			TypeCode. Double => reader.ReadDouble(),
			TypeCode.Decimal => reader.ReadDecimal(),
			TypeCode. String => reader.ReadString(),
			_ => throw new NotSupportedException($"Unsupported type found in save file.")
		};

		return map;
	}

	private static unsafe T ReadStruct<T>(this BinaryReader reader) where T : unmanaged 
	{
		byte[] bytes = reader.ReadBytes(sizeof(T));

		fixed (byte* ptr = bytes) return Marshal.PtrToStructure<T>((nint)ptr);
	}

	private static unsafe T[] ReadStructArray<T>(this BinaryReader reader, int count) where T : unmanaged
	{
		T[] arr = new T[count];

		fixed (T* ptr = arr) reader.ReadExactly(new(ptr, sizeof(T) * arr.Length));

		return arr;
	}

	private static void WriteArray(this BinaryWriter writer, object[] array)
	{
		TypeCode code = Type.GetTypeCode(array!.GetType().GetElementType());

		if (code is TypeCode.Object or TypeCode.DBNull or TypeCode.DateTime) throw new NotSupportedException($"Unsupported value type.");

		writer.Write((byte)code);

		foreach (object value in array) switch (code)
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

	private static unsafe void WriteStruct<T>(this BinaryWriter writer, T source) where T : unmanaged 
	{
		byte* ptr = stackalloc byte[sizeof(T)];

		Marshal.StructureToPtr(source, (nint)ptr, false);

		writer.Write(new Span<byte>(ptr, sizeof(T)));
	}

	private static unsafe void WriteStructArray<T>(this BinaryWriter writer, T[] values) where T : unmanaged
	{
		fixed (T* ptr = values) writer.Write(new Span<byte>(ptr, sizeof(T) * values.Length));
	}

	#endregion
}

using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

internal static class Extensions
{
	private static class BlittableHelper<T>
	{
		static BlittableHelper() { try { if (default(T) is not null) { GCHandle.Alloc(default(T), GCHandleType.Pinned).Free(); IsBlittable = true; } } catch { } }

		public static readonly bool IsBlittable = false;
	}

	private static readonly MethodInfo _unsafeSizeOf = typeof(Unsafe).GetMethod(nameof(Unsafe.SizeOf), BindingFlags.Public | BindingFlags.Static)!;

	extension(Type t)
	{
		/// <summary>
		/// Gets a value indicating whether the <see cref="Type"/> is safely blittable.
		/// </summary>
		public bool IsBlittable => (bool)typeof(BlittableHelper<>).MakeGenericType(t).GetField("IsBlittable", BindingFlags.Static | BindingFlags.Public)!.GetValue(null!)!;

		public bool IsEntity => typeof(Engine.Entity).IsAssignableFrom(t);

		/// <summary>
		/// Returns the size of a value of the given <see cref="Type"/>.
		/// </summary>
		public int Size => (int)_unsafeSizeOf.MakeGenericMethod(t).Invoke(null, null)!;
	}

	extension(ReadOnlySpan<char> a)
	{
		public int GetDistance(ReadOnlySpan<char> b)
		{
			int lengthA = a.Length, lengthB = b.Length;

			if (lengthA == 0) return lengthB; if (lengthB == 0) return lengthA;

			Span<int> prev = stackalloc int[lengthB + 1], curr = stackalloc int[lengthB + 1];

			for (int j = 0; j <= lengthB; j++) prev[j] = j;

			for (int i = 1; i <= lengthA; i++)
			{
				curr[0] = i;

				int ai = a[i - 1];

				for (int j = 1; j <= lengthB; j++) curr[j] = Math.Min(Math.Min(prev[j] + 1, curr[j - 1] + 1), prev[j - 1] + (b[j - 1] == ai ? 0 : 1));

				curr.CopyTo(prev);
			}

			return prev[lengthB];
		}
	}
}

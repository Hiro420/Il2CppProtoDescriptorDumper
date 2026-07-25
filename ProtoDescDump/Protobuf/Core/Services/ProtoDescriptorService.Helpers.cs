using google.protobuf;
using ProtoBuf;
using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace ProtoDescDump.Core;

public sealed partial class ProtoDescriptorService
{
	static bool IsNamedType(FieldDescriptorProto.Type type)
	{
		return type == FieldDescriptorProto.Type.TYPE_MESSAGE || type == FieldDescriptorProto.Type.TYPE_ENUM;
	}

	static string GetPackagePath(string package, string name)
	{
		package = package.Length == 0 || package.StartsWith('.') ? package : $".{package}";
		return name.StartsWith('.') ? name : $"{package}.{name}";
	}

	static string GetLabel(FieldDescriptorProto.Label label)
	{
		return label switch
		{
			FieldDescriptorProto.Label.LABEL_REQUIRED => "required",
			FieldDescriptorProto.Label.LABEL_REPEATED => "repeated",
			_ => "optional",
		};
	}

	static string GetType(FieldDescriptorProto.Type type)
	{
		return type switch
		{
			FieldDescriptorProto.Type.TYPE_INT32 => "int32",
			FieldDescriptorProto.Type.TYPE_INT64 => "int64",
			FieldDescriptorProto.Type.TYPE_SINT32 => "sint32",
			FieldDescriptorProto.Type.TYPE_SINT64 => "sint64",
			FieldDescriptorProto.Type.TYPE_UINT32 => "uint32",
			FieldDescriptorProto.Type.TYPE_UINT64 => "uint64",
			FieldDescriptorProto.Type.TYPE_STRING => "string",
			FieldDescriptorProto.Type.TYPE_BOOL => "bool",
			FieldDescriptorProto.Type.TYPE_BYTES => "bytes",
			FieldDescriptorProto.Type.TYPE_DOUBLE => "double",
			FieldDescriptorProto.Type.TYPE_ENUM => "enum",
			FieldDescriptorProto.Type.TYPE_FLOAT => "float",
			FieldDescriptorProto.Type.TYPE_GROUP => "GROUP",
			FieldDescriptorProto.Type.TYPE_MESSAGE => "message",
			FieldDescriptorProto.Type.TYPE_FIXED32 => "fixed32",
			FieldDescriptorProto.Type.TYPE_FIXED64 => "fixed64",
			FieldDescriptorProto.Type.TYPE_SFIXED32 => "sfixed32",
			FieldDescriptorProto.Type.TYPE_SFIXED64 => "sfixed64",
			_ => type.ToString(),
		};
	}

	static bool ExtractType(
		IExtensible data,
		FieldDescriptorProto field,
		out string? value)
	{
		switch (field.type)
		{
			case FieldDescriptorProto.Type.TYPE_INT32:
			case FieldDescriptorProto.Type.TYPE_SINT32:
			case FieldDescriptorProto.Type.TYPE_SFIXED32:
			case FieldDescriptorProto.Type.TYPE_ENUM:
				return TryFormat<int>(
					data,
					field.number,
					static x => x.ToString(CultureInfo.InvariantCulture),
					out value);

			case FieldDescriptorProto.Type.TYPE_UINT32:
			case FieldDescriptorProto.Type.TYPE_FIXED32:
				return TryFormat<uint>(
					data,
					field.number,
					static x => x.ToString(CultureInfo.InvariantCulture),
					out value);

			case FieldDescriptorProto.Type.TYPE_INT64:
			case FieldDescriptorProto.Type.TYPE_SINT64:
			case FieldDescriptorProto.Type.TYPE_SFIXED64:
				return TryFormat<long>(
					data,
					field.number,
					static x => x.ToString(CultureInfo.InvariantCulture),
					out value);

			case FieldDescriptorProto.Type.TYPE_UINT64:
			case FieldDescriptorProto.Type.TYPE_FIXED64:
				return TryFormat<ulong>(
					data,
					field.number,
					static x => x.ToString(CultureInfo.InvariantCulture),
					out value);

			case FieldDescriptorProto.Type.TYPE_FLOAT:
				return TryFormat<float>(
					data,
					field.number,
					static x => x.ToString("R", CultureInfo.InvariantCulture),
					out value);

			case FieldDescriptorProto.Type.TYPE_DOUBLE:
				return TryFormat<double>(
					data,
					field.number,
					static x => x.ToString("R", CultureInfo.InvariantCulture),
					out value);

			case FieldDescriptorProto.Type.TYPE_BOOL:
				return TryFormat<bool>(
					data,
					field.number,
					static x => x ? "true" : "false",
					out value);

			case FieldDescriptorProto.Type.TYPE_STRING:
				return TryFormat<string>(
					data,
					field.number,
					static x => Util.ToLiteral(x),
					out value);

			case FieldDescriptorProto.Type.TYPE_BYTES:
				return TryFormat<byte[]>(
					data,
					field.number,
					static x => Util.ToLiteral(Convert.ToBase64String(x)),
					out value);

			case FieldDescriptorProto.Type.TYPE_MESSAGE:
			case FieldDescriptorProto.Type.TYPE_GROUP:
			default:
				value = null;
				return false;
		}
	}

	static bool TryFormat<T>(
		IExtensible data,
		int fieldNumber,
		Func<T, string> formatter,
		out string? value)
	{
		if (Extensible.TryGetValue(data, fieldNumber, out T item))
		{
			value = formatter(item);
			return true;
		}

		value = null;
		return false;
	}

	static List<string> ExtractRepeatedScalarValues(IExtensible data, FieldDescriptorProto field)
	{
		var values = new List<string>();
		var extension = data.GetExtensionObject(false);

		if (extension == null)
			return values;

		var stream = extension.BeginQuery();
		try
		{
			while (TryReadWireVarint(stream, out var tag))
			{
				if (tag == 0)
					break;

				var currentFieldNumber = (int)(tag >> 3);
				var wireType = (int)(tag & 7);

				if (currentFieldNumber <= 0)
					throw new InvalidDataException($"Invalid protobuf field tag 0x{tag:X}.");

				if (currentFieldNumber != field.number)
				{
					SkipWireValue(stream, wireType);
					continue;
				}

				if (wireType == 2 && IsPackableScalar(field.type))
				{
					var payload = ReadLengthDelimited(stream, $"packed {field.type} custom option");
					ReadPackedScalarValues(payload, field.type, values);
				}
				else
				{
					values.Add(ReadScalarValue(stream, wireType, field.type));
				}
			}
		}
		finally
		{
			extension.EndQuery(stream);
		}

		return values;
	}

	static bool IsPackableScalar(FieldDescriptorProto.Type type)
	{
		return type switch
		{
			FieldDescriptorProto.Type.TYPE_DOUBLE => true,
			FieldDescriptorProto.Type.TYPE_FLOAT => true,
			FieldDescriptorProto.Type.TYPE_INT64 => true,
			FieldDescriptorProto.Type.TYPE_UINT64 => true,
			FieldDescriptorProto.Type.TYPE_INT32 => true,
			FieldDescriptorProto.Type.TYPE_FIXED64 => true,
			FieldDescriptorProto.Type.TYPE_FIXED32 => true,
			FieldDescriptorProto.Type.TYPE_BOOL => true,
			FieldDescriptorProto.Type.TYPE_UINT32 => true,
			FieldDescriptorProto.Type.TYPE_SFIXED32 => true,
			FieldDescriptorProto.Type.TYPE_SFIXED64 => true,
			FieldDescriptorProto.Type.TYPE_SINT32 => true,
			FieldDescriptorProto.Type.TYPE_SINT64 => true,
			_ => false,
		};
	}

	static void ReadPackedScalarValues(
		ReadOnlySpan<byte> payload,
		FieldDescriptorProto.Type type,
		List<string> values)
	{
		var offset = 0;
		while (offset < payload.Length)
			values.Add(ReadPackedScalarValue(payload, ref offset, type));
	}

	static string ReadPackedScalarValue(
		ReadOnlySpan<byte> payload,
		ref int offset,
		FieldDescriptorProto.Type type)
	{
		switch (type)
		{
			case FieldDescriptorProto.Type.TYPE_INT32:
				return unchecked((int)(uint)ReadWireVarint(payload, ref offset))
					.ToString(CultureInfo.InvariantCulture);

			case FieldDescriptorProto.Type.TYPE_UINT32:
				return unchecked((uint)ReadWireVarint(payload, ref offset))
					.ToString(CultureInfo.InvariantCulture);

			case FieldDescriptorProto.Type.TYPE_INT64:
				return unchecked((long)ReadWireVarint(payload, ref offset))
					.ToString(CultureInfo.InvariantCulture);

			case FieldDescriptorProto.Type.TYPE_UINT64:
				return ReadWireVarint(payload, ref offset).ToString(CultureInfo.InvariantCulture);

			case FieldDescriptorProto.Type.TYPE_SINT32:
				return DecodeZigZag32(ReadWireVarint(payload, ref offset))
					.ToString(CultureInfo.InvariantCulture);

			case FieldDescriptorProto.Type.TYPE_SINT64:
				return DecodeZigZag64(ReadWireVarint(payload, ref offset))
					.ToString(CultureInfo.InvariantCulture);

			case FieldDescriptorProto.Type.TYPE_BOOL:
				return ReadWireVarint(payload, ref offset) == 0 ? "false" : "true";

			case FieldDescriptorProto.Type.TYPE_FIXED32:
				return ReadFixed32(payload, ref offset).ToString(CultureInfo.InvariantCulture);

			case FieldDescriptorProto.Type.TYPE_SFIXED32:
				return unchecked((int)ReadFixed32(payload, ref offset))
					.ToString(CultureInfo.InvariantCulture);

			case FieldDescriptorProto.Type.TYPE_FLOAT:
				return FormatFloat(BitConverter.Int32BitsToSingle(
					unchecked((int)ReadFixed32(payload, ref offset))));

			case FieldDescriptorProto.Type.TYPE_FIXED64:
				return ReadFixed64(payload, ref offset).ToString(CultureInfo.InvariantCulture);

			case FieldDescriptorProto.Type.TYPE_SFIXED64:
				return unchecked((long)ReadFixed64(payload, ref offset))
					.ToString(CultureInfo.InvariantCulture);

			case FieldDescriptorProto.Type.TYPE_DOUBLE:
				return FormatDouble(BitConverter.Int64BitsToDouble(
					unchecked((long)ReadFixed64(payload, ref offset))));

			default:
				throw new InvalidDataException($"Scalar type {type} cannot use packed encoding.");
		}
	}

	static string ReadScalarValue(Stream stream, int wireType, FieldDescriptorProto.Type type)
	{
		switch (type)
		{
			case FieldDescriptorProto.Type.TYPE_INT32:
				RequireWireType(wireType, 0, type);
				return unchecked((int)(uint)ReadRequiredWireVarint(stream, type))
					.ToString(CultureInfo.InvariantCulture);

			case FieldDescriptorProto.Type.TYPE_UINT32:
				RequireWireType(wireType, 0, type);
				return unchecked((uint)ReadRequiredWireVarint(stream, type))
					.ToString(CultureInfo.InvariantCulture);

			case FieldDescriptorProto.Type.TYPE_INT64:
				RequireWireType(wireType, 0, type);
				return unchecked((long)ReadRequiredWireVarint(stream, type))
					.ToString(CultureInfo.InvariantCulture);

			case FieldDescriptorProto.Type.TYPE_UINT64:
				RequireWireType(wireType, 0, type);
				return ReadRequiredWireVarint(stream, type).ToString(CultureInfo.InvariantCulture);

			case FieldDescriptorProto.Type.TYPE_SINT32:
				RequireWireType(wireType, 0, type);
				return DecodeZigZag32(ReadRequiredWireVarint(stream, type))
					.ToString(CultureInfo.InvariantCulture);

			case FieldDescriptorProto.Type.TYPE_SINT64:
				RequireWireType(wireType, 0, type);
				return DecodeZigZag64(ReadRequiredWireVarint(stream, type))
					.ToString(CultureInfo.InvariantCulture);

			case FieldDescriptorProto.Type.TYPE_BOOL:
				RequireWireType(wireType, 0, type);
				return ReadRequiredWireVarint(stream, type) == 0 ? "false" : "true";

			case FieldDescriptorProto.Type.TYPE_FIXED32:
				RequireWireType(wireType, 5, type);
				return ReadFixed32(stream).ToString(CultureInfo.InvariantCulture);

			case FieldDescriptorProto.Type.TYPE_SFIXED32:
				RequireWireType(wireType, 5, type);
				return unchecked((int)ReadFixed32(stream)).ToString(CultureInfo.InvariantCulture);

			case FieldDescriptorProto.Type.TYPE_FLOAT:
				RequireWireType(wireType, 5, type);
				return FormatFloat(BitConverter.Int32BitsToSingle(unchecked((int)ReadFixed32(stream))));

			case FieldDescriptorProto.Type.TYPE_FIXED64:
				RequireWireType(wireType, 1, type);
				return ReadFixed64(stream).ToString(CultureInfo.InvariantCulture);

			case FieldDescriptorProto.Type.TYPE_SFIXED64:
				RequireWireType(wireType, 1, type);
				return unchecked((long)ReadFixed64(stream)).ToString(CultureInfo.InvariantCulture);

			case FieldDescriptorProto.Type.TYPE_DOUBLE:
				RequireWireType(wireType, 1, type);
				return FormatDouble(BitConverter.Int64BitsToDouble(unchecked((long)ReadFixed64(stream))));

			case FieldDescriptorProto.Type.TYPE_STRING:
				RequireWireType(wireType, 2, type);
				return FormatUtf8String(ReadLengthDelimited(stream, "string custom option"));

			case FieldDescriptorProto.Type.TYPE_BYTES:
				RequireWireType(wireType, 2, type);
				return FormatBytesLiteral(ReadLengthDelimited(stream, "bytes custom option"));

			default:
				throw new InvalidDataException($"Unsupported repeated scalar custom-option type {type}.");
		}
	}

	static ulong ReadRequiredWireVarint(Stream stream, FieldDescriptorProto.Type type)
	{
		if (!TryReadWireVarint(stream, out var value))
			throw new EndOfStreamException($"Truncated {type} custom-option value.");

		return value;
	}

	static int DecodeZigZag32(ulong value)
	{
		var raw = unchecked((uint)value);
		return unchecked((int)((raw >> 1) ^ (uint)-(int)(raw & 1)));
	}

	static long DecodeZigZag64(ulong value)
	{
		return unchecked((long)((value >> 1) ^ (ulong)-(long)(value & 1)));
	}

	static void RequireWireType(int actual, int expected, FieldDescriptorProto.Type type)
	{
		if (actual != expected)
			throw new InvalidDataException(
				$"Custom option {type} used wire type {actual}; expected {expected}.");
	}

	static byte[] ReadLengthDelimited(Stream stream, string context)
	{
		if (!TryReadWireVarint(stream, out var rawLength) || rawLength > int.MaxValue)
			throw new InvalidDataException($"Invalid {context} length.");

		var payload = new byte[(int)rawLength];
		ReadExactly(stream, payload);
		return payload;
	}

	static uint ReadFixed32(Stream stream)
	{
		Span<byte> payload = stackalloc byte[4];
		ReadExactly(stream, payload);
		return BinaryPrimitives.ReadUInt32LittleEndian(payload);
	}

	static ulong ReadFixed64(Stream stream)
	{
		Span<byte> payload = stackalloc byte[8];
		ReadExactly(stream, payload);
		return BinaryPrimitives.ReadUInt64LittleEndian(payload);
	}

	static uint ReadFixed32(ReadOnlySpan<byte> payload, ref int offset)
	{
		if (payload.Length - offset < 4)
			throw new EndOfStreamException("Truncated packed fixed32 value.");

		var value = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(offset, 4));
		offset += 4;
		return value;
	}

	static ulong ReadFixed64(ReadOnlySpan<byte> payload, ref int offset)
	{
		if (payload.Length - offset < 8)
			throw new EndOfStreamException("Truncated packed fixed64 value.");

		var value = BinaryPrimitives.ReadUInt64LittleEndian(payload.Slice(offset, 8));
		offset += 8;
		return value;
	}

	static string FormatUtf8String(byte[] payload)
	{
		try
		{
			return Util.ToLiteral(new UTF8Encoding(false, true).GetString(payload));
		}
		catch (DecoderFallbackException ex)
		{
			throw new InvalidDataException("Custom-option string contains invalid UTF-8.", ex);
		}
	}

	static string FormatBytesLiteral(ReadOnlySpan<byte> payload)
	{
		var result = new StringBuilder(payload.Length + 2);
		result.Append('"');

		foreach (var value in payload)
		{
			switch (value)
			{
				case (byte)'\\':
					result.Append(@"\\");
					break;
				case (byte)'"':
					result.Append("\\\"");
					break;
				default:
					if (value >= 0x20 && value <= 0x7E)
					{
						result.Append((char)value);
					}
					else
					{
						result.Append('\\');
						result.Append(Convert.ToString(value, 8).PadLeft(3, '0'));
					}
					break;
			}
		}

		result.Append('"');
		return result.ToString();
	}

	static string FormatFloat(float value)
	{
		if (float.IsNaN(value))
			return "nan";
		if (float.IsPositiveInfinity(value))
			return "inf";
		if (float.IsNegativeInfinity(value))
			return "-inf";

		return value.ToString("R", CultureInfo.InvariantCulture);
	}

	static string FormatDouble(double value)
	{
		if (double.IsNaN(value))
			return "nan";
		if (double.IsPositiveInfinity(value))
			return "inf";
		if (double.IsNegativeInfinity(value))
			return "-inf";

		return value.ToString("R", CultureInfo.InvariantCulture);
	}

	static List<int> ExtractRepeatedEnumValues(IExtensible data, int fieldNumber)
	{
		var values = new List<int>();
		var extension = data.GetExtensionObject(false);

		if (extension == null)
			return values;

		var stream = extension.BeginQuery();
		try
		{
			while (TryReadWireVarint(stream, out var tag))
			{
				if (tag == 0)
					break;

				var currentFieldNumber = (int)(tag >> 3);
				var wireType = (int)(tag & 7);

				if (currentFieldNumber <= 0)
					throw new InvalidDataException($"Invalid protobuf field tag 0x{tag:X}.");

				if (currentFieldNumber != fieldNumber)
				{
					SkipWireValue(stream, wireType);
					continue;
				}

				switch (wireType)
				{
					case 0: // expanded enum value
						if (!TryReadWireVarint(stream, out var rawValue))
							throw new EndOfStreamException("Truncated enum custom-option value.");

						values.Add(unchecked((int)(uint)rawValue));
						break;

					case 2: // packed repeated enum values
						if (!TryReadWireVarint(stream, out var rawLength) || rawLength > int.MaxValue)
							throw new InvalidDataException("Invalid packed enum custom-option length.");

						var payload = new byte[(int)rawLength];
						ReadExactly(stream, payload);

						var offset = 0;
						while (offset < payload.Length)
						{
							var packedValue = ReadWireVarint(payload, ref offset);
							values.Add(unchecked((int)(uint)packedValue));
						}
						break;

					default:
						throw new InvalidDataException(
							$"Enum custom option field {fieldNumber} used unsupported wire type {wireType}.");
				}
			}
		}
		finally
		{
			extension.EndQuery(stream);
		}

		return values;
	}

	static bool TryReadWireVarint(Stream stream, out ulong value)
	{
		value = 0;

		for (var index = 0; index < 10; index++)
		{
			var current = stream.ReadByte();
			if (current < 0)
			{
				if (index == 0)
					return false;

				throw new EndOfStreamException("Truncated protobuf varint.");
			}

			if (index == 9 && (current & 0xFE) != 0)
				throw new InvalidDataException("Protobuf varint exceeds 64 bits.");

			value |= (ulong)(current & 0x7F) << (index * 7);
			if ((current & 0x80) == 0)
				return true;
		}

		throw new InvalidDataException("Invalid protobuf varint.");
	}

	static ulong ReadWireVarint(ReadOnlySpan<byte> payload, ref int offset)
	{
		ulong value = 0;

		for (var index = 0; index < 10; index++)
		{
			if (offset >= payload.Length)
				throw new EndOfStreamException("Truncated packed enum value.");

			var current = payload[offset++];
			if (index == 9 && (current & 0xFE) != 0)
				throw new InvalidDataException("Packed enum varint exceeds 64 bits.");

			value |= (ulong)(current & 0x7F) << (index * 7);
			if ((current & 0x80) == 0)
				return value;
		}

		throw new InvalidDataException("Invalid packed enum varint.");
	}

	static void SkipWireValue(Stream stream, int wireType)
	{
		switch (wireType)
		{
			case 0:
				if (!TryReadWireVarint(stream, out _))
					throw new EndOfStreamException("Truncated protobuf varint while skipping a field.");
				break;

			case 1:
				SkipBytes(stream, 8);
				break;

			case 2:
				if (!TryReadWireVarint(stream, out var length))
					throw new EndOfStreamException("Truncated protobuf length while skipping a field.");
				SkipBytes(stream, length);
				break;

			case 3:
				while (true)
				{
					if (!TryReadWireVarint(stream, out var nestedTag))
						throw new EndOfStreamException("Truncated protobuf group.");

					var nestedWireType = (int)(nestedTag & 7);
					if (nestedWireType == 4)
						break;

					SkipWireValue(stream, nestedWireType);
				}
				break;

			case 4:
				break;

			case 5:
				SkipBytes(stream, 4);
				break;

			default:
				throw new InvalidDataException($"Unsupported protobuf wire type {wireType}.");
		}
	}

	static void SkipBytes(Stream stream, ulong count)
	{
		var buffer = new byte[256];

		while (count > 0)
		{
			var requested = (int)Math.Min((ulong)buffer.Length, count);
			var read = stream.Read(buffer, 0, requested);
			if (read <= 0)
				throw new EndOfStreamException("Truncated protobuf field.");

			count -= (ulong)read;
		}
	}

	static void ReadExactly(Stream stream, byte[] buffer)
	{
		ReadExactly(stream, buffer.AsSpan());
	}

	static void ReadExactly(Stream stream, Span<byte> buffer)
	{
		var offset = 0;
		while (offset < buffer.Length)
		{
			var read = stream.Read(buffer[offset..]);
			if (read <= 0)
				throw new EndOfStreamException("Truncated protobuf payload.");

			offset += read;
		}
	}

	static string ResolveType(FieldDescriptorProto field)
	{
		if (IsNamedType(field.type))
		{
			return field.type_name;
		}

		return GetType(field.type);
	}

	static void AppendHeadingSpace(StringBuilder sb, ref bool marker)
	{
		if (marker)
		{
			sb.AppendLine();
			marker = false;
		}
	}

	void PushDescriptorName(FileDescriptorProto file)
	{
		messageNameStack.Push(file.package);
	}

	void PushDescriptorName(DescriptorProto proto)
	{
		messageNameStack.Push(proto.name);
	}

	void PushDescriptorName(FieldDescriptorProto field)
	{
		messageNameStack.Push(field.name);
	}

	void PopDescriptorName()
	{
		messageNameStack.Pop();
	}
}


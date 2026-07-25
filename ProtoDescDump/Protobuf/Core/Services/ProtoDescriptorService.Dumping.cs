using google.protobuf;
using ProtoBuf;
using ProtoDescDump.Core.Services.ProtoDescriptor;
using System.Text;

namespace ProtoDescDump.Core;

public sealed partial class ProtoDescriptorService
{
	void DumpFileDescriptor(FileDescriptorProto proto, StringBuilder sb)
	{
		var originalDependencyCount = proto.dependency.Count;

		if (!string.IsNullOrEmpty(proto.package))
			PushDescriptorName(proto);

		var marker = false;

		string syntax = string.IsNullOrEmpty(proto.syntax) ? "proto2" : proto.syntax;

		AppendHeadingSpace(sb, ref marker);
		sb.AppendLine($"syntax = {Util.ToLiteral(syntax)};");
		marker = true;

		if (proto.dependency.Count > 0)
			AppendHeadingSpace(sb, ref marker);

		for (var i = 0; i < originalDependencyCount; i++)
		{
			var dependency = proto.dependency[i];
			var modifier = string.Empty;

			if (proto.public_dependency.Contains(i))
			{
				modifier = "public ";
			}
			else if (proto.weak_dependency.Contains(i))
			{
				modifier = "weak ";
			}

			sb.AppendLine($"import {modifier}\"{dependency}\";");
			marker = true;
		}

		var customImportInsertPos = sb.Length;

		if (!string.IsNullOrEmpty(proto.package))
		{
			AppendHeadingSpace(sb, ref marker);
			sb.AppendLine($"package {proto.package};");
			marker = true;
		}

		var options = DumpOptions(proto, proto.options);

		foreach (var option in options)
		{
			AppendHeadingSpace(sb, ref marker);
			sb.AppendLine($"option {option.Key} = {option.Value};");
		}

		if (options.Count > 0)
		{
			marker = true;
		}

		DumpExtensionDescriptors(proto, proto.extension, sb, 0, ref marker);

		foreach (var field in proto.enum_type)
		{
			DumpEnumDescriptor(proto, field, sb, 0, ref marker);
		}

		foreach (var message in proto.message_type)
		{
			DumpDescriptor(proto, message, sb, 0, ref marker);
		}

		foreach (var service in proto.service)
		{
			DumpService(proto, service, sb, ref marker);
		}

		if (proto.dependency.Count > originalDependencyCount)
		{
			var importBlock = new StringBuilder();
			if (originalDependencyCount == 0 && customImportInsertPos > 0)
				importBlock.AppendLine();

			for (var i = originalDependencyCount; i < proto.dependency.Count; i++)
				importBlock.AppendLine($"import \"{proto.dependency[i]}\";");

			if (originalDependencyCount == 0 && customImportInsertPos == 0)
				importBlock.AppendLine();

			sb.Insert(customImportInsertPos, importBlock.ToString());
		}

		if (!string.IsNullOrEmpty(proto.package))
			PopDescriptorName();
	}

	Dictionary<string, string> DumpOptions(FileDescriptorProto source, google.protobuf.FileOptions options)
	{
		var optionsKv = new Dictionary<string, string>();

		if (options == null)
			return optionsKv;

		if (options.ShouldSerializedeprecated())
			optionsKv.Add("deprecated", options.deprecated ? "true" : "false");
		if (options.ShouldSerializeoptimize_for())
			optionsKv.Add("optimize_for", $"{options.optimize_for}");
		if (options.ShouldSerializecc_generic_services())
			optionsKv.Add("cc_generic_services", options.cc_generic_services ? "true" : "false");
		if (options.ShouldSerializecc_enable_arenas())
			optionsKv.Add("cc_enable_arenas", options.cc_enable_arenas ? "true" : "false");
		if (options.ShouldSerializego_package())
			optionsKv.Add("go_package", Util.ToLiteral(options.go_package));
		if (options.ShouldSerializejava_package())
			optionsKv.Add("java_package", Util.ToLiteral(options.java_package));
		if (options.ShouldSerializejava_outer_classname())
			optionsKv.Add("java_outer_classname", Util.ToLiteral(options.java_outer_classname));
		if (options.ShouldSerializejava_generate_equals_and_hash())
			optionsKv.Add("java_generate_equals_and_hash", options.java_generate_equals_and_hash ? "true" : "false");
		if (options.ShouldSerializejava_generic_services())
			optionsKv.Add("java_generic_services", options.java_generic_services ? "true" : "false");
		if (options.ShouldSerializejava_multiple_files())
			optionsKv.Add("java_multiple_files", options.java_multiple_files ? "true" : "false");
		if (options.ShouldSerializejava_string_check_utf8())
			optionsKv.Add("java_string_check_utf8", options.java_string_check_utf8 ? "true" : "false");
		if (options.ShouldSerializepy_generic_services())
			optionsKv.Add("py_generic_services", options.py_generic_services ? "true" : "false");
		if (options.ShouldSerializeruby_package())
			optionsKv.Add("ruby_package", Util.ToLiteral(options.ruby_package));
		if (options.ShouldSerializeobjc_class_prefix())
			optionsKv.Add("objc_class_prefix", Util.ToLiteral(options.objc_class_prefix));
		if (options.ShouldSerializecsharp_namespace())
			optionsKv.Add("csharp_namespace", Util.ToLiteral(options.csharp_namespace));
		if (options.ShouldSerializeswift_prefix())
			optionsKv.Add("swift_prefix", Util.ToLiteral(options.swift_prefix));
		if (options.ShouldSerializephp_generic_services())
			optionsKv.Add("php_generic_services", options.php_generic_services ? "true" : "false");
		if (options.ShouldSerializephp_class_prefix())
			optionsKv.Add("php_class_prefix", Util.ToLiteral(options.php_class_prefix));
		if (options.ShouldSerializephp_namespace())
			optionsKv.Add("php_namespace", Util.ToLiteral(options.php_namespace));
		if (options.ShouldSerializephp_metadata_namespace())
			optionsKv.Add("php_metadata_namespace", Util.ToLiteral(options.php_metadata_namespace));

		DumpOptionsMatching(source, ".google.protobuf.FileOptions", options, optionsKv);

		return optionsKv;
	}

	Dictionary<string, string> DumpOptions(FileDescriptorProto source, FieldOptions options)
	{
		var optionsKv = new Dictionary<string, string>();

		if (options == null)
			return optionsKv;

		if (options.ShouldSerializectype())
			optionsKv.Add("ctype", $"{options.ctype}");
		if (options.ShouldSerializedeprecated())
			optionsKv.Add("deprecated", options.deprecated ? "true" : "false");
		if (options.ShouldSerializelazy())
			optionsKv.Add("lazy", options.lazy ? "true" : "false");
		if (options.ShouldSerializepacked())
			optionsKv.Add("packed", options.packed ? "true" : "false");
		if (options.ShouldSerializeweak())
			optionsKv.Add("weak", options.weak ? "true" : "false");
		if (options.ShouldSerializejstype())
			optionsKv.Add("jstype", $"{options.jstype}");

		DumpOptionsMatching(source, ".google.protobuf.FieldOptions", options, optionsKv);

		return optionsKv;
	}

	Dictionary<string, string> DumpOptions(FileDescriptorProto source, MessageOptions options)
	{
		var optionsKv = new Dictionary<string, string>();

		if (options == null)
			return optionsKv;

		if (options.ShouldSerializemessage_set_wire_format())
			optionsKv.Add("message_set_wire_format", options.message_set_wire_format ? "true" : "false");
		if (options.ShouldSerializeno_standard_descriptor_accessor())
			optionsKv.Add("no_standard_descriptor_accessor", options.no_standard_descriptor_accessor ? "true" : "false");
		if (options.ShouldSerializedeprecated())
			optionsKv.Add("deprecated", options.deprecated ? "true" : "false");

		DumpOptionsMatching(source, ".google.protobuf.MessageOptions", options, optionsKv);

		return optionsKv;
	}

	Dictionary<string, string> DumpOptions(FileDescriptorProto source, EnumOptions options)
	{
		var optionsKv = new Dictionary<string, string>();

		if (options == null)
			return optionsKv;

		if (options.ShouldSerializeallow_alias())
			optionsKv.Add("allow_alias", options.allow_alias ? "true" : "false");
		if (options.ShouldSerializedeprecated())
			optionsKv.Add("deprecated", options.deprecated ? "true" : "false");

		DumpOptionsMatching(source, ".google.protobuf.EnumOptions", options, optionsKv);

		return optionsKv;
	}

	Dictionary<string, string> DumpOptions(FileDescriptorProto source, EnumValueOptions options)
	{
		var optionsKv = new Dictionary<string, string>();

		if (options == null)
			return optionsKv;

		if (options.ShouldSerializedeprecated())
			optionsKv.Add("deprecated", options.deprecated ? "true" : "false");

		DumpOptionsMatching(source, ".google.protobuf.EnumValueOptions", options, optionsKv);

		return optionsKv;
	}

	Dictionary<string, string> DumpOptions(FileDescriptorProto source, ServiceOptions options)
	{
		var optionsKv = new Dictionary<string, string>();

		if (options == null)
			return optionsKv;

		if (options.ShouldSerializedeprecated())
			optionsKv.Add("deprecated", options.deprecated ? "true" : "false");

		DumpOptionsMatching(source, ".google.protobuf.ServiceOptions", options, optionsKv);

		return optionsKv;
	}

	Dictionary<string, string> DumpOptions(FileDescriptorProto source, MethodOptions options)
	{
		var optionsKv = new Dictionary<string, string>();

		if (options == null)
			return optionsKv;

		if (options.ShouldSerializedeprecated())
			optionsKv.Add("deprecated", options.deprecated ? "true" : "false");

		DumpOptionsMatching(source, ".google.protobuf.MethodOptions", options, optionsKv);

		return optionsKv;
	}

	bool CanUseRelativeTypeName(string absoluteType)
	{
		if (!absoluteType.StartsWith(".", StringComparison.Ordinal))
			return true;

		var relativeType = absoluteType[1..];
		var separator = relativeType.IndexOf('.');
		var firstSegment = separator < 0 ? relativeType : relativeType[..separator];
		var scopes = messageNameStack.Reverse().SkipLast(1); // exclude the field currently being emitted
		var scope = string.Join(".", scopes);

		while (!string.IsNullOrEmpty(scope))
		{
			var capturedPrefix = $".{scope}.{firstSegment}";
			var capturedPackage = capturedPrefix[1..];
			var isCaptured = protobufTypeMap.ContainsKey(capturedPrefix) ||
				protobufs.Any(proto =>
				proto.package == capturedPackage ||
				proto.package.StartsWith(capturedPackage + ".", StringComparison.Ordinal));

			if (isCaptured)
			{
				return absoluteType == capturedPrefix ||
					absoluteType.StartsWith(capturedPrefix + ".", StringComparison.Ordinal);
			}

			var lastSeparator = scope.LastIndexOf('.');
			scope = lastSeparator < 0 ? string.Empty : scope[..lastSeparator];
		}

		return true;
	}

	bool TryGetRepeatedCustomOptionValues(
		FieldDescriptorProto field,
		IExtensible options,
		out List<string> values)
	{
		values = new List<string>();

		if (field.label != FieldDescriptorProto.Label.LABEL_REPEATED)
			return false;

		if (IsNamedType(field.type) && !string.IsNullOrEmpty(field.type_name))
		{
			if (!protobufTypeMap.TryGetValue(field.type_name, out var typeNode) ||
				typeNode.Source is not EnumDescriptorProto enumProto)
			{
				return false;
			}

			foreach (var index in ExtractRepeatedEnumValues(options, field.number))
			{
				var enumValue = enumProto.value.Find(x => x.number == index);
				values.Add(enumValue?.name ?? index.ToString());
			}

			return values.Count > 0;
		}

		values.AddRange(ExtractRepeatedScalarValues(options, field));
		return values.Count > 0;
	}

	bool TryFormatCustomOptionValue(FieldDescriptorProto field, IExtensible options, out string? value)
	{
		if (IsNamedType(field.type) && !string.IsNullOrEmpty(field.type_name))
		{
			if (!protobufTypeMap.TryGetValue(field.type_name, out var typeNode))
			{
				logger.Warn($"Skipping custom option field {field.name}: unresolved type {field.type_name}.");
				value = null;
				return false;
			}

			var fieldData = typeNode.Source;

			if (fieldData is EnumDescriptorProto enumProto)
			{
				if (field.label == FieldDescriptorProto.Label.LABEL_REPEATED)
				{
					var enumValues = ExtractRepeatedEnumValues(options, field.number);
					if (enumValues.Count == 0)
					{
						value = null;
						return false;
					}

					var enumNames = enumValues.Select(index =>
					{
						var enumValue = enumProto.value.Find(x => x.number == index);
						return enumValue?.name ?? index.ToString();
					});
					value = $"[{string.Join(", ", enumNames)}]";
					return true;
				}

				if (Extensible.TryGetValue(options, field.number, out int index))
				{
					var enumValue = enumProto.value.Find(x => x.number == index);
					value = enumValue?.name ?? index.ToString();
					return true;
				}
			}
			else if (fieldData is DescriptorProto messageProto)
			{
				var extension = Extensible.GetValue<ExtensionPlaceholder>(options, field.number);
				if (extension == null)
				{
					value = null;
					return false;
				}

				var fields = new List<string>();
				foreach (var subField in messageProto.field)
				{
					// Aggregate option values use protobuf TextFormat-like syntax, but
					// protoc/IDE .proto parsers do not consistently accept list literals
					// (for example: not_in: [0]). The portable representation for a
					// repeated field is to emit the field once per value:
					//     not_in: 0 not_in: 1
					if (TryGetRepeatedCustomOptionValues(subField, extension, out var repeatedValues))
					{
						foreach (var repeatedValue in repeatedValues)
							fields.Add($"{subField.name}: {repeatedValue}");
					}
					else if (TryFormatCustomOptionValue(subField, extension, out var subValue))
					{
						fields.Add($"{subField.name}: {subValue}");
					}
				}

				if (fields.Count > 0)
				{
					value = $"{{ {string.Join(" ", fields)} }}";
					return true;
				}
			}
		}
		else if (field.label == FieldDescriptorProto.Label.LABEL_REPEATED)
		{
			var scalarValues = ExtractRepeatedScalarValues(options, field);
			if (scalarValues.Count == 0)
			{
				value = null;
				return false;
			}

			value = $"[{string.Join(", ", scalarValues)}]";
			return true;
		}
		else if (ExtractType(options, field, out value))
		{
			return true;
		}

		value = null;
		return false;
	}

	void DumpOptionsFieldRecursive(FieldDescriptorProto field, IExtensible options, Dictionary<string, string> optionsKv, string? path, string? fullExtensionName = null)
	{
		var optionName = (fullExtensionName ?? field.name).TrimStart('.');
		var key = string.IsNullOrEmpty(path) ? $"({optionName})" : $"{path}.{field.name}";

		try
		{
			if (TryFormatCustomOptionValue(field, options, out var value))
				optionsKv[key] = value!;
		}
		catch (Exception ex) when (ex is ProtoException or InvalidDataException or EndOfStreamException)
		{
			logger.Warn(
				$"Skipping incompatible custom option {key} " +
				$"(field {field.number}, declared {field.label} {field.type}): {ex.Message}");
		}
	}

	void DumpOptionsMatching(FileDescriptorProto source, string typeName, IExtensible options, Dictionary<string, string> optionsKv)
	{
		var dependencies = new HashSet<FileDescriptorProto>(protobufMap[source.name].AllPublicDependencies)
		{
			source
		};

		foreach (var type in protobufTypeMap)
		{
			if (dependencies.Contains(type.Value.Proto!) && type.Value.Source is FieldDescriptorProto field)
			{
				if (!string.IsNullOrEmpty(field.extendee) && field.extendee == typeName)
				{
					var optionCount = optionsKv.Count;
					DumpOptionsFieldRecursive(field, options, optionsKv, null, type.Key);

					if (optionsKv.Count > optionCount &&
						type.Value.Proto?.name is { Length: > 0 } optionFile &&
						optionFile != source.name &&
						!source.dependency.Contains(optionFile))
					{
						source.dependency.Add(optionFile);
					}
				}
			}
		}
	}

	void DumpExtensionDescriptors(FileDescriptorProto source, IEnumerable<FieldDescriptorProto> fields, StringBuilder sb, int level, ref bool marker)
	{
		var levelspace = new string('\t', level);

		foreach (var mapping in fields.GroupBy(x => x.extendee))
		{
			if (string.IsNullOrEmpty(mapping.Key))
				throw new Exception("Empty extendee in extension, this should not be possible");

			AppendHeadingSpace(sb, ref marker);
			string mappingKey = mapping.Key;
			if (mappingKey.StartsWith('.'))
			{
				mappingKey = mappingKey[1..];
			}
			sb.AppendLine($"{levelspace}extend {mappingKey} {{");

			foreach (var field in mapping)
			{
				sb.AppendLine($"{levelspace}\t{BuildDescriptorDeclaration(source, field)}");
			}

			sb.AppendLine($"{levelspace}}}");
			marker = true;
		}
	}

	void DumpDescriptor(FileDescriptorProto source, DescriptorProto proto, StringBuilder sb, int level, ref bool marker)
	{
		PushDescriptorName(proto);

		var levelspace = new string('\t', level);
		var innerMarker = false;

		AppendHeadingSpace(sb, ref marker);
		sb.AppendLine($"{levelspace}message {proto.name} {{");

		var options = DumpOptions(source, proto.options);

		foreach (var option in options)
		{
			AppendHeadingSpace(sb, ref innerMarker);
			sb.AppendLine($"{levelspace}\toption {option.Key} = {option.Value};");
		}

		if (options.Count > 0)
		{
			innerMarker = true;
		}

		if (proto.extension.Count > 0)
		{
			DumpExtensionDescriptors(source, proto.extension, sb, level + 1, ref innerMarker);
		}

		foreach (var field in proto.nested_type)
		{
			if (field.options != null && field.options.map_entry)
			{
				continue;
			}

			DumpDescriptor(source, field, sb, level + 1, ref innerMarker);
		}

		foreach (var field in proto.enum_type)
		{
			DumpEnumDescriptor(source, field, sb, level + 1, ref innerMarker);
		}

		var rootFields = proto.field.Where(x => !x.ShouldSerializeoneof_index()).ToList();

		foreach (var field in rootFields)
		{
			AppendHeadingSpace(sb, ref innerMarker);
			sb.AppendLine($"{levelspace}\t{BuildDescriptorDeclaration(source, field)}");
		}

		if (rootFields.Count > 0)
		{
			innerMarker = true;
		}

		for (var i = 0; i < proto.oneof_decl.Count; i++)
		{
			var oneof = proto.oneof_decl[i];
			var fields = proto.field.Where(x => x.ShouldSerializeoneof_index() && x.oneof_index == i).ToArray();

			AppendHeadingSpace(sb, ref innerMarker);
			string oneofName = oneof.name;
			if (oneofName.StartsWith('_'))
			{
				oneofName = oneofName[1..];
			}
			sb.AppendLine($"{levelspace}\toneof {oneofName} {{");

			foreach (var field in fields)
			{
				sb.AppendLine($"{levelspace}\t\t{BuildDescriptorDeclaration(source, field, emitFieldLabel: false)}");
			}

			sb.AppendLine($"{levelspace}\t}}");
			innerMarker = true;
		}

		foreach (var range in proto.extension_range)
		{
			var max = Convert.ToString(range.end - 1);

			if (range.end >= 536870911)
			{
				max = "max";
			}

			AppendHeadingSpace(sb, ref innerMarker);
			sb.AppendLine($"{levelspace}\textensions {range.start} to {max};");
		}

		sb.AppendLine($"{levelspace}}}");
		marker = true;

		PopDescriptorName();
	}

	void DumpEnumDescriptor(FileDescriptorProto source, EnumDescriptorProto field, StringBuilder sb, int level, ref bool marker)
	{
		var levelspace = new string('\t', level);

		AppendHeadingSpace(sb, ref marker);
		sb.AppendLine($"{levelspace}enum {field.name} {{");

		foreach (var option in DumpOptions(source, field.options))
		{
			sb.AppendLine($"{levelspace}\toption {option.Key} = {option.Value};");
		}

		foreach (var enumValue in field.value)
		{
			var options = DumpOptions(source, enumValue.options);

			var parameters = string.Empty;
			if (options.Count > 0)
			{
				parameters = $" [{string.Join(", ", options.Select(kvp => $"{kvp.Key} = {kvp.Value}"))}]";
			}

			sb.AppendLine($"{levelspace}\t{enumValue.name} = {enumValue.number}{parameters};");
		}

		sb.AppendLine($"{levelspace}}}");
		marker = true;
	}

	void DumpService(FileDescriptorProto source, ServiceDescriptorProto service, StringBuilder sb, ref bool marker)
	{
		var innerMarker = false;

		AppendHeadingSpace(sb, ref marker);
		sb.AppendLine($"service {service.name} {{");

		var rootOptions = DumpOptions(source, service.options);

		foreach (var option in rootOptions)
		{
			sb.AppendLine($"\toption {option.Key} = {option.Value};");
		}

		if (rootOptions.Count > 0)
		{
			innerMarker = true;
		}

		foreach (var method in service.method)
		{
			var declaration = $"\trpc {method.name} ({(method.client_streaming ? "stream " : "")}{method.input_type}) returns ({(method.server_streaming ? "stream " : "")}{method.output_type})";

			var options = DumpOptions(source, method.options);

			AppendHeadingSpace(sb, ref innerMarker);

			if (options.Count == 0)
			{
				sb.AppendLine($"{declaration};");
			}
			else
			{
				sb.AppendLine($"{declaration} {{");

				foreach (var option in options)
				{
					sb.AppendLine($"\t\toption {option.Key} = {option.Value};");
				}

				sb.AppendLine("\t}");
				innerMarker = true;
			}
		}

		sb.AppendLine("}");
		marker = true;
	}

	string BuildDescriptorDeclaration(FileDescriptorProto source, FieldDescriptorProto field, bool emitFieldLabel = true)
	{
		PushDescriptorName(field);

		var type = ResolveType(field);

		if (field.label == FieldDescriptorProto.Label.LABEL_REPEATED
			&& field.type == FieldDescriptorProto.Type.TYPE_MESSAGE
			&& !string.IsNullOrEmpty(field.type_name)
			&& protobufTypeMap.TryGetValue(field.type_name, out var typeNode)
			&& typeNode.Source is DescriptorProto mapEntryDescriptor
			&& mapEntryDescriptor.options != null
			&& mapEntryDescriptor.options.map_entry)
		{
			var keyField = mapEntryDescriptor.field.FirstOrDefault(f => f.name == "key");
			var valueField = mapEntryDescriptor.field.FirstOrDefault(f => f.name == "value");

			if (keyField != null && valueField != null)
			{
				var keyType = GetType(keyField.type);
				var valueType = ResolveType(valueField);

				if (!string.IsNullOrEmpty(source.package))
				{
					string packagePrefix = "." + source.package + ".";
					if (valueType.StartsWith(packagePrefix, StringComparison.Ordinal))
					{
						valueType = valueType[packagePrefix.Length..];
					}
					else if (valueType.StartsWith('.') && CanUseRelativeTypeName(valueType))
					{
						valueType = valueType[1..];
					}
				}

				type = $"map<{keyType}, {valueType}>";
				emitFieldLabel = false;
			}
		}
		var options = new Dictionary<string, string>();

		if (!string.IsNullOrEmpty(source.package))
		{
			string packagePrefix = "." + source.package + ".";
			if (type.StartsWith(packagePrefix, StringComparison.Ordinal))
			{
				type = type[packagePrefix.Length..];
			}
			else if (type.StartsWith('.') && CanUseRelativeTypeName(type))
			{
				type = type[1..];
			}
		}

		if (!string.IsNullOrEmpty(field.json_name))
		{
			options.Add("json_name", Util.ToLiteral(field.json_name));
		}

		var fieldOptions = DumpOptions(source, field.options);
		foreach (var pair in fieldOptions)
		{
			options[pair.Key] = pair.Value;
		}

		var parameters = string.Empty;
		if (options.Count > 0)
		{
			parameters = $" [{string.Join(", ", options.Select(kvp => $"{kvp.Key} = {kvp.Value}"))}]";
		}

		PopDescriptorName();

		var descriptorDeclarationBuilder = new StringBuilder();
		if (emitFieldLabel)
		{
			string label = GetLabel(field.label);
			if (source.syntax != "proto3" || label != "optional")
			{
				descriptorDeclarationBuilder.Append(label);
				descriptorDeclarationBuilder.Append(' ');
			}
		}

		descriptorDeclarationBuilder.Append($"{type} {field.name} = {field.number}{parameters};");

		return descriptorDeclarationBuilder.ToString();
	}
}


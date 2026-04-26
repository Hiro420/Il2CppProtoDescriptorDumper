using google.protobuf;
using ProtoDescDump.Core.Abstractions;
using ProtoDescDump.Core.Services.ProtoDescriptor;

namespace ProtoDescDump.Core;

public sealed partial class ProtoDescriptorService(IEnumerable<FileDescriptorProto> protobufs, ILogger logger) : IProtoDescriptorAnalyzer, IProtoDescriptorFormatter
{
	public delegate void ProcessProtobuf(FileDescriptorProto buffer, string proto);

	readonly List<FileDescriptorProto> protobufs = [.. protobufs];
	readonly Stack<string> messageNameStack = [];
	readonly Dictionary<string, ProtoNode> protobufMap = [];
	readonly Dictionary<string, ProtoTypeNode> protobufTypeMap = [];
	readonly ILogger logger = logger;
}


using google.protobuf;

namespace ProtoDescDump.Core.Abstractions;

public interface IProtoDescriptorFormatter
{
	string FormatFile(FileDescriptorProto proto);
}


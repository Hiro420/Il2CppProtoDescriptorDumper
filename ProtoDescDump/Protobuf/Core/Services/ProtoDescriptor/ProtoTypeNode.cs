using google.protobuf;

namespace ProtoDescDump.Core.Services.ProtoDescriptor;

sealed class ProtoTypeNode
{
	public string? Name;
	public FileDescriptorProto? Proto;
	public object? Source;
	public bool Defined;
}


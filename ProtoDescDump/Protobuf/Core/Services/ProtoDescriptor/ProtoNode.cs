using google.protobuf;

namespace ProtoDescDump.Core.Services.ProtoDescriptor;

sealed class ProtoNode
{
	public string? Name;
	public FileDescriptorProto? Proto;
	public List<ProtoNode> Dependencies = [];
	public HashSet<FileDescriptorProto> AllPublicDependencies = [];
	public List<ProtoTypeNode> Types = [];
	public bool Defined;
}


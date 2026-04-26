using google.protobuf;

namespace ProtoDescDump.Core.Abstractions;

public interface IProtoDescriptorAnalyzer
{
	bool Analyze(IReadOnlyList<FileDescriptorProto> protobufs);
}


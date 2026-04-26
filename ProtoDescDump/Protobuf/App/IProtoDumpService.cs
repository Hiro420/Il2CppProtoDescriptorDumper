namespace ProtoDescDump.App;

public interface IProtoDumpService
{
	int Run(byte[] pb, string outputDir);
}


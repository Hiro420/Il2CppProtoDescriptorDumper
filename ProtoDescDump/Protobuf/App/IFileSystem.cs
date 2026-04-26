namespace ProtoDescDump.App;

public interface IFileSystem
{
	Stream OpenRead(string path);
	void WriteAllText(string path, string contents);
	void EnsureDirectory(string path);
}


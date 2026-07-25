using google.protobuf;
using ProtoBuf;
using ProtoDescDump.Core.Abstractions;

namespace ProtoDescDump.App;

public sealed class ProtoDumpService(
	IFileSystem fileSystem,
	ILogger logger,
	IProtoDescriptorAnalyzer analyzer,
	IProtoDescriptorFormatter formatter) : IProtoDumpService
{
	private readonly IFileSystem fileSystem = fileSystem;
	private readonly ILogger logger = logger;
	private readonly IProtoDescriptorAnalyzer analyzer = analyzer;
	private readonly IProtoDescriptorFormatter formatter = formatter;

	public int Run(byte[] pb, string outputDir)
	{
		try
		{
			using MemoryStream ms = new MemoryStream(pb);
			FileDescriptorSet set = Serializer.Deserialize<FileDescriptorSet>(ms);

			if (!analyzer.Analyze(set.file))
			{
				logger.Error("Dump failed. Not all dependencies and types were found.");
				return -1;
			}

			logger.Info("Analysis succeeded. Dumping proto files...");

			foreach (var buffer in set.file)
			{
				var relativeName = buffer.name.Replace('/', Path.DirectorySeparatorChar);

				// dirty hack to get rid of the duplicate package name in the output path
				string outputFile;
				if (Path.GetDirectoryName(relativeName) is { Length: > 0 })
				{
					outputFile = Path.Combine(outputDir, relativeName);
				}
				else
				{
					var packageParts = (buffer.package ?? string.Empty)
						.Split('.', StringSplitOptions.RemoveEmptyEntries);

					outputFile = Path.Combine([outputDir, .. packageParts, relativeName]);
				}

				fileSystem.EnsureDirectory(Path.GetDirectoryName(outputFile)!);
				var protoText = formatter.FormatFile(buffer);

				logger.Info($"Outputting proto to \"{outputFile}\"");
				fileSystem.WriteAllText(outputFile, protoText);
			}

			logger.Info("Dump completed successfully.");
			return 0;
		}
		catch (Exception ex)
		{
			logger.Error("[FATAL] Failed", ex);
			return -1;
		}
	}
}


using google.protobuf;
using Mono.Cecil;
using Mono.Cecil.Rocks;
using ProtoBuf;
using ProtoDescDump.App;
using ProtoDescDump.Core;

namespace ProtoDescDump;

class MainApp
{
	const string dumpDir = "Dump";
	const string dummyDllDir = "Dll";
	const string scriptName = "il2cpp.json";
	const string gaName = "GameAssembly.dll";
	public static List<AssemblyDefinition> assemblyDefs = new();
	public static List<PEHeader.SectionTable> sectionTables = [];
	public static Il2CppScript Il2CppScript = new Il2CppScript();
	public static byte[] moduleBytes = [];
	public static ulong baseAddress = 0x0;
	public static uint InitUsagesRVA = 0x0;

	public static void Main(string[] args)
	{
		string allDllPath = Path.Combine(dumpDir, dummyDllDir);
		string dllPath2 = Path.Combine(allDllPath, "System.dll");
		if (!File.Exists(dllPath2))
		{
			Console.WriteLine($"Error: {dllPath2} not found.");
			return;
		}

		Console.WriteLine($"Loading assemblies from: {allDllPath}");

		DefaultAssemblyResolver resolver = new DefaultAssemblyResolver();
		resolver.AddSearchDirectory(allDllPath);
		ReaderParameters readerParameters = new ReaderParameters();
		readerParameters.AssemblyResolver = resolver;
		AssemblyDefinition assemblyDef2 = AssemblyDefinition.ReadAssembly(dllPath2, readerParameters);

		foreach (string filename in Directory.EnumerateFiles(allDllPath, "*.dll"))
		{
			Console.WriteLine($"Loading assembly from: {filename}");
			AssemblyDefinition assemblyDef = AssemblyDefinition.ReadAssembly(filename, readerParameters);
			assemblyDefs.Add(assemblyDef);
		}

		Console.WriteLine($"Initializing GameAssembly: {gaName}");

		moduleBytes = File.ReadAllBytes(gaName);
		baseAddress = Misc.GetVA(gaName);
		sectionTables = Misc.GetPeHeaderSectionTables(gaName);

		// Hopefully universal enough?
		TypeDefinition OidLookup = assemblyDef2.MainModule.GetType("Internal.Cryptography.OidLookup");
		TypeDefinition __c = OidLookup.NestedTypes.FirstOrDefault(t => t.Name == "<>c") ?? throw new Exception("Could not find <>c type.");

		// The reason we need want to find InitializeRuntimeMetadata is because in modern unity versions it inits the usages first by the metadataPointer of each stringliteral
		// This might confuse the x64 emulator, so we blacklist the calls to InitializeRuntimeMetadata
		InitUsagesRVA = UsagesFinder.FindInitCallRva(__c.GetStaticConstructor().GetMethodRVA());

		if (InitUsagesRVA == 0)
		{
			Console.WriteLine("Error: Could not find usages of Init method.");
			return;
		}

		Console.WriteLine($"Loading Il2CppScript from: {Path.Combine(dumpDir, scriptName)}");

		Il2CppScript = Il2CppScript.LoadFromFile(Path.Combine(dumpDir, scriptName));
		Console.WriteLine($"Loaded {Il2CppScript.addressMap.stringLiterals.Count} string literals from il2cpp.json.");

		FileDescriptorSet set = new FileDescriptorSet();

		int total = 0, success = 0, fail = 0, wrongB64 = 0;
		var failedTypes = new List<(TypeDefinition td, string? base64, string? error)>();

		IEnumerable<TypeDefinition> allTypes = assemblyDefs.SelectMany(a => a.MainModule.GetAllTypes());

		foreach (TypeDefinition typeDef in allTypes)
		{
			if (!typeDef.Name.EndsWith("Reflection"))
				continue;
			if (typeDef.Namespace.StartsWith("MiHoYo.SDK.Protobuf")) // for certain anime games
				continue;
			if (!typeDef.Fields.All(f => f.Name == "descriptor"))
				continue;
			if (!typeDef.Methods.Any(f => f.Name == "get_Descriptor"))
				continue;
			total++;


			MethodDefinition staticCtor = typeDef.GetStaticConstructor() ?? throw new Exception($"Static constructor not found for type {typeDef.FullName}.");

			string? base64 = StaticCtorResolver.RecoverDescriptorBase64(staticCtor, verbose: false);
			if (base64 == null)
			{
				Console.WriteLine($"FAIL: {typeDef.FullName}");
				fail++;
				failedTypes.Add((typeDef, null, null));
				continue;
			}

			try
			{
				var bytes = Convert.FromBase64String(base64);
				var ms = new MemoryStream(bytes);
				var single = Serializer.Deserialize<FileDescriptorProto>(ms);
				set.file.Add(single);
				Console.WriteLine($"OK: {typeDef.FullName} len={base64.Length} decoded={bytes.Length}");
				success++;
			}
			catch (Exception ex)
			{
				Console.WriteLine($"WRONG_B64: {typeDef.FullName} len={base64.Length} error={ex.Message}");
				wrongB64++;
				failedTypes.Add((typeDef, base64, ex.Message));
			}
		}


		var logger = new ConsoleLogger();
		var fileSystem = new LocalFileSystem();
		var coreService = new ProtoDescriptorService([], logger);
		var app = new ProtoDumpService(fileSystem, logger, coreService, coreService);
		using (MemoryStream stream = new MemoryStream())
		{
			Serializer.Serialize(stream, set);
			app.Run(stream.ToArray(), "output");
		}

		Console.WriteLine($"\nTotal: {total}, Success: {success}, Fail: {fail}, WrongB64: {wrongB64}");

		//Console.WriteLine("\n========== VERBOSE RE-RUN FOR FAILURES ==========");
		//foreach (var (td, b64, err) in failedTypes)
		//{
		//	Console.WriteLine($"\n===== {td.FullName} =====");
		//	if (b64 != null)
		//		Console.WriteLine($"  Base64 ({b64.Length} chars): {b64[..Math.Min(80, b64.Length)]}...");
		//	if (err != null)
		//		Console.WriteLine($"  Error: {err}");
		//	var sc = td.GetStaticConstructor()!;
		//	StaticCtorResolver.RecoverDescriptorBase64(sc, verbose: true);
		//}


	}
}


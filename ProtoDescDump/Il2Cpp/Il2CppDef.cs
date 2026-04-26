namespace ProtoDescDump;

public class Il2CppScript
{
	public AddressMap addressMap { get; set; } = new AddressMap();

	public static Il2CppScript LoadFromFile(string filePath)
	{
		if (!File.Exists(filePath))
		{
			Console.WriteLine($"Error: {filePath} not found.");
			return new Il2CppScript();
		}
		string jsonContent = File.ReadAllText(filePath);
		return Newtonsoft.Json.JsonConvert.DeserializeObject<Il2CppScript>(jsonContent) ?? new Il2CppScript();
	}
}

public class AddressMap
{
	public List<Il2CppMethodDefinition> methodDefinitions { get; set; } = [];
	public List<Il2CppStringLiteral> stringLiterals { get; set; } = [];
	public List<Il2CppTypeInfoPointer> typeInfoPointers { get; set; } = [];
	public List<Il2CppTypeRefPointer> typeRefPointers { get; set; } = [];
	public List<Il2CppMethodInfoPointer> methodInfoPointers { get; set; } = [];
}

public class Il2CppMethodDefinition
{
	public string virtualAddress { get; set; } = "0x0";
	public string name { get; set; } = "";
	public string signature { get; set; } = "";
	public string dotNetSignature { get; set; } = "";
	public string group { get; set; } = "";

	public ulong VA => Convert.ToUInt64(virtualAddress, 16);
	public uint RVA => (uint)(VA - MainApp.baseAddress);
}

public class Il2CppStringLiteral
{
	public string virtualAddress { get; set; } = "0x0";
	public string name { get; set; } = "";

	[Newtonsoft.Json.JsonProperty("string")]
	public string s_string { get; set; } = "";
	public ulong VA => Convert.ToUInt64(virtualAddress, 16);
	public uint RVA => (uint)(VA - MainApp.baseAddress);
}

public class Il2CppTypeInfoPointer
{
	public string virtualAddress { get; set; } = "0x0";
	public string name { get; set; } = "";
	public string type { get; set; } = "";
	public string dotNetType { get; set; } = "";
	public ulong VA => Convert.ToUInt64(virtualAddress, 16);
	public uint RVA => (uint)(VA - MainApp.baseAddress);
}

public class Il2CppTypeRefPointer
{
	public string virtualAddress { get; set; } = "0x0";
	public string name { get; set; } = "";
	public string dotNetType { get; set; } = "";
	public ulong VA => Convert.ToUInt64(virtualAddress, 16);
	public uint RVA => (uint)(VA - MainApp.baseAddress);
}

public class Il2CppMethodInfoPointer
{
	public string virtualAddress { get; set; } = "0x0";
	public string name { get; set; } = "";
	public string dotNetSignature { get; set; } = "";
	public string methodAddress { get; set; } = "";
	public ulong VA => Convert.ToUInt64(virtualAddress, 16);
	public uint RVA => (uint)(VA - MainApp.baseAddress);
	public ulong MethodVA => Convert.ToUInt64(methodAddress, 16);
	public uint MethodRVA => (uint)(MethodVA - MainApp.baseAddress);
}


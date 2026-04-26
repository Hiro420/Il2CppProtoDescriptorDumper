using Mono.Cecil;
using System.Globalization;

namespace ProtoDescDump;

public static class CecilEx
{
	public static uint GetMethodRVA(this MethodDefinition methodDef)
	{
		CustomAttribute? AddressAttribute = methodDef.CustomAttributes.FirstOrDefault(a => a.AttributeType.Name == "AddressAttribute");
		if (AddressAttribute == null)
		{
			Console.WriteLine($"Warning: Method {methodDef.FullName} does not have an AddressAttribute.");
			return 0;
		}
		CustomAttributeNamedArgument? RVAField = AddressAttribute.Fields.FirstOrDefault(f => f.Name == "RVA");
		if (RVAField == null)
		{
			Console.WriteLine($"Warning: Method {methodDef.FullName} does not have an RVA field in its AddressAttribute.");
			return 0;
		}
		string rvaStr = RVAField.Value.Argument.Value?.ToString() ?? "0";
		uint rva;
		if (rvaStr.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
		{
			rva = Convert.ToUInt32(rvaStr[2..], 16);
		}
		else
		{
			rva = uint.Parse(rvaStr, NumberStyles.Integer, CultureInfo.InvariantCulture);
		}
		return rva;
	}
}

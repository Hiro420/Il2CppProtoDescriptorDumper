using Iced.Intel;
using Mono.Cecil;

namespace ProtoDescDump;

internal class InstructionParser
{
	public static List<Instruction> GetInstructions(Il2cppFunctionAddressData address, ByteArrayCodeReader codeReader, bool? isDebug = false)
	{
		codeReader.Position = Convert.ToInt32(address.Offset);
		var decoder = Iced.Intel.Decoder.Create(IntPtr.Size * 8, codeReader);
		decoder.IP = address.VA;
		var instructions = new List<Instruction>();
		bool debug = isDebug ?? false;

		if (debug) Console.WriteLine("/*");

		while (true)
		{
			var instruction = decoder.Decode();

			if (debug)
			{
				string instructionStr = instruction.ToString();

				//instructionStr = Regex.Replace(instructionStr, @"[0-9A-Fa-f]+h", match =>
				//{
				//	return ulong.TryParse(match.Value.Substring(0, match.Value.Length - 1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong decimalValue)
				//		? decimalValue.ToString()
				//		: match.Value;
				//});

				Console.WriteLine($"\t{instruction.IP:X} | {instructionStr}");
			}

			instructions.Add(instruction);

			if (instruction.Mnemonic == Mnemonic.Ret)
			{
				break;
			}
		}
		if (debug) Console.WriteLine("*/");

		return instructions;
	}

	public static List<Instruction> GetInstructions(Il2cppFunctionAddressData address, bool? isDebug = false)
	{
		var codeReader = new ByteArrayCodeReader(MainApp.moduleBytes);
		return GetInstructions(address, codeReader, isDebug);
	}

	public static List<Instruction> GetInstructions(uint RVA, bool? isDebug = false)
	{
		var addressData = new Il2cppFunctionAddressData(RVA);
		return GetInstructions(addressData, isDebug);
	}

	public static List<Instruction> GetInstructions(MethodDefinition methodDefinition, bool? isDebug = false)
	{
		uint RVA = methodDefinition.GetMethodRVA();
		return GetInstructions(RVA, isDebug);
	}


}

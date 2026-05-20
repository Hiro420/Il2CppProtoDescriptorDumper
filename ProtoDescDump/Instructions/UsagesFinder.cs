using Iced.Intel;

namespace ProtoDescDump;

internal class UsagesFinder
{
	private const ulong ImageBase = 0x180000000;

	private static ulong VaToRva(ulong va)
	{
		return checked((ulong)(va - ImageBase));
	}

	private static bool IsConditionalJump(Mnemonic mnemonic)
	{
		return mnemonic == Mnemonic.Jne || mnemonic == Mnemonic.Jnp ||
			   mnemonic == Mnemonic.Jno || mnemonic == Mnemonic.Jns ||
			   mnemonic == Mnemonic.Je;
	}

	private static bool IsCmpMemImm0(Instruction ins)
	{
		return ins.Mnemonic == Mnemonic.Cmp &&
			   ins.Op0Kind == OpKind.Memory &&
			   (
				   (ins.Op1Kind == OpKind.Immediate8 && ins.Immediate8 == 0) ||
				   (ins.Op1Kind == OpKind.Immediate8to16 && ins.Immediate8to16 == 0) ||
				   (ins.Op1Kind == OpKind.Immediate8to32 && ins.Immediate8to32 == 0) ||
				   (ins.Op1Kind == OpKind.Immediate8to64 && ins.Immediate8to64 == 0) ||
				   (ins.Op1Kind == OpKind.Immediate16 && ins.Immediate16 == 0) ||
				   (ins.Op1Kind == OpKind.Immediate32 && ins.Immediate32 == 0) ||
				   (ins.Op1Kind == OpKind.Immediate32to64 && ins.Immediate32to64 == 0) ||
				   (ins.Op1Kind == OpKind.Immediate64 && ins.Immediate64 == 0)
			   );
	}

	public static ulong FindInitCallRva(uint cctorRva)
	{
		var addressData = new Il2cppFunctionAddressData(cctorRva);
		Console.WriteLine($"[+] Scanning cctor at RVA: 0x{addressData.RVA:X}, Offset: 0x{addressData.Offset:X}");

		List<Instruction> instructions = InstructionParser.GetInstructions(addressData);
		if (instructions == null || instructions.Count == 0)
		{
			Console.WriteLine("[-] No instructions decoded.");
			return 0;
		}

		for (int i = 0; i < instructions.Count - 1; i++)
		{
			Instruction cmp = instructions[i];
			if (!IsCmpMemImm0(cmp))
				continue;

			Instruction jcc = instructions[i + 1];
			if (!IsConditionalJump(jcc.Mnemonic))
				continue;

			ulong skipTargetVa = jcc.NearBranch64;

			Console.WriteLine($"[+] Found init-guard cmp at 0x{cmp.IP:X}");
			Console.WriteLine($"[+] Found conditional jump at 0x{jcc.IP:X} -> 0x{skipTargetVa:X}");

			for (int j = i + 2; j < instructions.Count; j++)
			{
				Instruction ins = instructions[j];

				if (ins.IP >= skipTargetVa)
					break;

				if (ins.Mnemonic == Mnemonic.Call)
				{
					ulong targetVa = ins.NearBranch64;
                    ulong targetRva = VaToRva(targetVa);

					Console.WriteLine($"[+] Found init call at 0x{ins.IP:X} -> VA 0x{targetVa:X}, RVA 0x{targetRva:X}");
					return targetRva;
				}
			}

			Console.WriteLine("[-] Guard pattern found, but no call inside guarded block.");
			return 0;
		}

		Console.WriteLine("[-] Could not find guarded init-call pattern.");
		return 0;
	}
}
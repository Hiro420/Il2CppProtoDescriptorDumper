using Iced.Intel;
using Mono.Cecil;

namespace ProtoDescDump;

internal static class StaticCtorResolver
{
	private const int MaxTrackedArrayElements = short.MaxValue + 1;

	// x86-64 Virtual Memory Boundary
	private const ulong SyntheticHeapStart = 0x7FFFUL << 48;
	private const ulong SyntheticStringStart = 0x7FFEUL << 48;

	public static string? RecoverDescriptorBase64(MethodDefinition staticCtor, bool verbose = true)
	{
		if (staticCtor == null) throw new ArgumentNullException(nameof(staticCtor));

		uint rva = staticCtor.GetMethodRVA();
		if (rva == 0)
		{
			Console.WriteLine("Static constructor has no RVA; cannot resolve.");
			return null;
		}

		var instructions = InstructionParser.GetInstructions(new Il2cppFunctionAddressData(rva), false);
		if (instructions.Count == 0)
		{
			Console.WriteLine("No native instructions decoded for static constructor.");
			return null;
		}

		var emulator = new X64Emulator(MainApp.moduleBytes, MainApp.baseAddress, MainApp.sectionTables);

		var stringByValue = new Dictionary<ulong, string>();
		ulong nextSyntheticString = SyntheticStringStart;
		foreach (var lit in MainApp.Il2CppScript.addressMap.stringLiterals)
		{
			ulong value;
			var raw = emulator.ReadMemory(lit.VA, 8);
			if (raw.HasValue && raw.Value != 0)
			{
				value = raw.Value;
			}
			else
			{
				value = AllocateSyntheticString(ref nextSyntheticString);
				emulator.WriteMemory(lit.VA, value, 8);
			}

			stringByValue.TryAdd(value, lit.s_string);
		}

		var instrByIP = new Dictionary<ulong, int>();
		for (int idx = 0; idx < instructions.Count; idx++)
			instrByIP[instructions[idx].IP] = idx;

		var arrayNewTargets = new HashSet<ulong>();
		var stringConcatTargets = new HashSet<ulong>();
		var stelemRefHelperCache = new Dictionary<ulong, bool>();

		ulong? arrayStoreHelperVA = null;
		ulong? activeArrayBase = null;
		ulong nextSyntheticArrayBase = SyntheticHeapStart;
		int expectedArrayCount = 0;
		bool firstArrayAllocSeen = false;
		string? result = null;

		var visitCount = new Dictionary<ulong, int>();
		const int MaxVisitsPerIP = 10_000;

		for (int i = 0; i < instructions.Count;)
		{
			var instr = instructions[i];
			int visits = visitCount.GetValueOrDefault(instr.IP, 0);
			if (visits >= MaxVisitsPerIP)
			{
				i++;
				continue;
			}
			visitCount[instr.IP] = visits + 1;

			ulong? jumpTarget = emulator.Step(instr);

			if (instr.Mnemonic == Mnemonic.Call && instr.Op0Kind == OpKind.NearBranch64)
			{
				ulong target = instr.NearBranchTarget;
				ulong? rcx = emulator.GetRegister(Register.RCX);
				ulong? rdx = emulator.GetRegister(Register.RDX);
				ulong? r8 = emulator.GetRegister(Register.R8);
				ulong? r9 = emulator.GetRegister(Register.R9);

				string? rcxString = ResolveString(rcx, stringByValue);
				string? rdxString = ResolveString(rdx, stringByValue);
				string? r8String = ResolveString(r8, stringByValue);
				string? r9String = ResolveString(r9, stringByValue);

				if (verbose)
				{
					Console.Write($"  CALL 0x{target:X} | RCX={FormatValue(rcx)} RDX={FormatValue(rdx)} R8={FormatValue(r8)}");
					PrintStringArg("rcxS", rcxString);
					PrintStringArg("rdxS", rdxString);
					PrintStringArg("r8S", r8String);
					PrintStringArg("r9S", r9String);
					Console.WriteLine();
				}

				if (target == MainApp.InitUsagesRVA)
				{
					emulator.ClearVolatileOnCall();
					goto endLoop;
				}

				if (arrayNewTargets.Contains(target) && IsPlausibleArrayLength(rdx))
				{
					if (TryFinishArray(emulator, activeArrayBase, expectedArrayCount, stringByValue,
						out var previousCandidate, out int previousResolved) &&
						LooksLikeDescriptorBase64(previousCandidate))
					{
						result = previousCandidate;
						if (verbose)
							Console.WriteLine($"[NewArrayAlloc] Previous emulated array is a valid descriptor, length={result.Length}");
						break;
					}

					if (verbose && activeArrayBase.HasValue && expectedArrayCount > 0)
						Console.WriteLine($"[NewArrayAlloc] Replacing previous array ({previousResolved}/{expectedArrayCount} resolved elements).");

					expectedArrayCount = checked((int)rdx!.Value);
					activeArrayBase = AllocateArray(emulator, ref nextSyntheticArrayBase, expectedArrayCount, rcx);
					emulator.ClearVolatileOnCall();
					emulator.ForceSetRegister(Register.RAX, activeArrayBase.Value);
					if (verbose)
						Console.WriteLine($"[ArrayNew] target=0x{target:X}, count={expectedArrayCount}, object=0x{activeArrayBase.Value:X}");
					goto endLoop;
				}

				if (!firstArrayAllocSeen && IsPlausibleArrayLength(rdx) && r8String == null)
				{
					arrayNewTargets.Add(target);
					firstArrayAllocSeen = true;
					expectedArrayCount = checked((int)rdx!.Value);
					activeArrayBase = AllocateArray(emulator, ref nextSyntheticArrayBase, expectedArrayCount, rcx);
					emulator.ClearVolatileOnCall();
					emulator.ForceSetRegister(Register.RAX, activeArrayBase.Value);
					if (verbose)
						Console.WriteLine($"[ArrayNew] Detected array allocation at 0x{instr.IP:X}, target=0x{target:X}, count={expectedArrayCount}, object=0x{activeArrayBase.Value:X}");
					goto endLoop;
				}

				if (firstArrayAllocSeen && activeArrayBase.HasValue && rcx == activeArrayBase.Value &&
					rdx.HasValue && rdx.Value < (ulong)expectedArrayCount)
				{
					if (arrayStoreHelperVA == null)
					{
						if (!stelemRefHelperCache.TryGetValue(target, out bool isStelemRef))
						{
							isStelemRef = IsReferenceArrayStoreHelper(target);
							stelemRefHelperCache[target] = isStelemRef;
						}

						if (isStelemRef)
						{
							arrayStoreHelperVA = target;
							if (verbose)
								Console.WriteLine($"[ArrayStore] Recognized stelem.ref helper at 0x{target:X}");
						}
					}

					if (target == arrayStoreHelperVA)
					{
						ulong value = r8 ?? 0;
						ulong elementAddress = activeArrayBase.Value + 0x20UL + rdx.Value * 8UL;
						emulator.WriteMemory(elementAddress, value, 8);

						if (verbose && ResolveString(r8, stringByValue) is { } stored)
							Console.WriteLine($"  [{rdx.Value}] = \"{Preview(stored)}\"");

						emulator.ClearVolatileOnCall();
						goto endLoop;
					}
				}

				bool passesActiveArray = activeArrayBase.HasValue &&
					(rcx == activeArrayBase.Value || rdx == activeArrayBase.Value ||
					 r8 == activeArrayBase.Value || r9 == activeArrayBase.Value);

				if (firstArrayAllocSeen && activeArrayBase.HasValue && expectedArrayCount > 0 &&
					passesActiveArray && target != arrayStoreHelperVA && !arrayNewTargets.Contains(target) &&
					TryFinishArray(emulator, activeArrayBase, expectedArrayCount, stringByValue,
						out var arrayCandidate, out int resolvedCount))
				{
					if (stringConcatTargets.Contains(target) || LooksLikeBase64(arrayCandidate))
					{
						stringConcatTargets.Add(target);
						ulong returnValue = RegisterSyntheticString(arrayCandidate, stringByValue, ref nextSyntheticString);
						emulator.ClearVolatileOnCall();
						emulator.ForceSetRegister(Register.RAX, returnValue);

						if (verbose)
							Console.WriteLine($"[Concat] string[] at 0x{target:X}: {resolvedCount}/{expectedArrayCount} emulated elements, length={arrayCandidate.Length}");

						if (LooksLikeDescriptorBase64(arrayCandidate))
						{
							result = arrayCandidate;
							goto endLoop;
						}

						goto endLoop;
					}
				}

				if (TryBuildStringArguments(rcxString, rdxString, r8String, r9String,
					out var directCandidate, out int directArgCount) &&
					(stringConcatTargets.Contains(target) || LooksLikeBase64(directCandidate)))
				{
					stringConcatTargets.Add(target);
					ulong returnValue = RegisterSyntheticString(directCandidate, stringByValue, ref nextSyntheticString);
					emulator.ClearVolatileOnCall();
					emulator.ForceSetRegister(Register.RAX, returnValue);

					if (verbose)
						Console.WriteLine($"[DirectConcat] {directArgCount} string args at 0x{target:X}, length={directCandidate.Length}");

					if (LooksLikeDescriptorBase64(directCandidate))
						result = directCandidate;

					goto endLoop;
				}

				emulator.ClearVolatileOnCall();
			}

		endLoop:
			if (result != null)
				break;

			if (jumpTarget.HasValue && instrByIP.TryGetValue(jumpTarget.Value, out int jumpIdx))
				i = jumpIdx;
			else
				i++;
		}

		if (result == null &&
			TryFinishArray(emulator, activeArrayBase, expectedArrayCount, stringByValue,
				out var fallbackCandidate, out int fallbackResolved) &&
			LooksLikeDescriptorBase64(fallbackCandidate))
		{
			if (verbose)
				Console.WriteLine($"[EndFallback] Emulated array memory {fallbackResolved}/{expectedArrayCount}, length={fallbackCandidate.Length}");
			result = fallbackCandidate;
		}

		if (result == null)
		{
			int resolved = CountResolvedArrayElements(emulator, activeArrayBase, expectedArrayCount, stringByValue);
			Console.WriteLine("Failed to locate descriptor base64 in static constructor.");
			Console.WriteLine($"  Emulated array elements resolved: {resolved}/{expectedArrayCount}");
			Console.WriteLine($"  Array store helper VA: {(arrayStoreHelperVA.HasValue ? $"0x{arrayStoreHelperVA.Value:X}" : "not found")}");
			return null;
		}

		if (verbose)
			Console.WriteLine($"Recovered base64 has length {result.Length}.");

		return result;
	}

	private static bool IsPlausibleArrayLength(ulong? value) =>
		value.HasValue && value.Value >= 1 && value.Value <= MaxTrackedArrayElements;

	private static string? ResolveString(ulong? value, IReadOnlyDictionary<ulong, string> stringByValue)
	{
		if (!value.HasValue)
			return null;
		return stringByValue.TryGetValue(value.Value, out var s) ? s : null;
	}

	private static bool TryBuildStringArguments(
		string? rcx, string? rdx, string? r8, string? r9,
		out string result, out int count)
	{
		result = string.Empty;
		count = 0;
		if (rcx == null || rdx == null)
			return false;

		var sb = new System.Text.StringBuilder(rcx.Length + rdx.Length + (r8?.Length ?? 0) + (r9?.Length ?? 0));
		sb.Append(rcx);
		sb.Append(rdx);
		count = 2;

		if (r8 != null)
		{
			sb.Append(r8);
			count = 3;
			if (r9 != null)
			{
				sb.Append(r9);
				count = 4;
			}
		}

		result = sb.ToString();
		return true;
	}

	private static ulong AllocateArray(
		X64Emulator emulator,
		ref ulong nextBase,
		int elementCount,
		ulong? klass)
	{
		ulong result = nextBase;
		ulong bytes = 0x20UL + checked((ulong)elementCount * 8UL);
		ulong alignedSpan = Math.Max(0x1000UL, (bytes + 0xFFFUL) & ~0xFFFUL);
		nextBase = checked(nextBase + alignedSpan);

		emulator.WriteMemory(result + 0x00, klass ?? 0, 8);
		emulator.WriteMemory(result + 0x18, (ulong)elementCount, 4);
		for (int i = 0; i < elementCount; i++)
			emulator.WriteMemory(result + 0x20UL + (ulong)i * 8UL, 0, 8);

		return result;
	}

	private static ulong AllocateSyntheticString(ref ulong nextValue)
	{
		ulong result = nextValue;
		nextValue = checked(nextValue + 8);
		return result;
	}

	private static ulong RegisterSyntheticString(
		string value,
		IDictionary<ulong, string> stringByValue,
		ref ulong nextSyntheticString)
	{
		ulong pointer = AllocateSyntheticString(ref nextSyntheticString);
		stringByValue[pointer] = value;
		return pointer;
	}

	private static bool IsReferenceArrayStoreHelper(ulong targetVa)
	{
		if (targetVa < MainApp.baseAddress)
			return false;

		ulong rva64 = targetVa - MainApp.baseAddress;
		if (rva64 > uint.MaxValue)
			return false;

		try
		{
			foreach (var instruction in InstructionParser.GetInstructions((uint)rva64, false))
			{
				if (instruction.Mnemonic == Mnemonic.Mov &&
					instruction.Op0Kind == OpKind.Memory &&
					instruction.Op1Kind == OpKind.Register &&
					X64Emulator.Canonicalize(instruction.MemoryBase) == Register.RCX &&
					X64Emulator.Canonicalize(instruction.MemoryIndex) == Register.RDX &&
					instruction.MemoryIndexScale == 8 &&
					instruction.MemoryDisplacement64 == 0x20 &&
					X64Emulator.Canonicalize(instruction.Op1Register) == Register.R8)
				{
					return true;
				}
			}
		}
		catch
		{
			// wtf?
		}

		return false;
	}

	private static bool TryFinishArray(
		X64Emulator emulator,
		ulong? arrayBase,
		int expectedCount,
		IReadOnlyDictionary<ulong, string> stringByValue,
		out string result,
		out int resolvedCount)
	{
		result = string.Empty;
		resolvedCount = 0;
		if (!arrayBase.HasValue || expectedCount <= 0)
			return false;

		var sb = new System.Text.StringBuilder();
		for (int i = 0; i < expectedCount; i++)
		{
			var element = emulator.ReadMemory(arrayBase.Value + 0x20UL + (ulong)i * 8UL, 8);
			if (!element.HasValue || !stringByValue.TryGetValue(element.Value, out var chunk))
				return false;

			sb.Append(chunk);
			resolvedCount++;
		}

		result = sb.ToString();
		return true;
	}

	private static int CountResolvedArrayElements(
		X64Emulator emulator,
		ulong? arrayBase,
		int expectedCount,
		IReadOnlyDictionary<ulong, string> stringByValue)
	{
		if (!arrayBase.HasValue || expectedCount <= 0)
			return 0;

		int count = 0;
		for (int i = 0; i < expectedCount; i++)
		{
			var element = emulator.ReadMemory(arrayBase.Value + 0x20UL + (ulong)i * 8UL, 8);
			if (element.HasValue && stringByValue.ContainsKey(element.Value))
				count++;
		}
		return count;
	}

	private static string FormatValue(ulong? value) => value?.ToString("X") ?? "?";

	private static void PrintStringArg(string name, string? value)
	{
		if (value != null)
			Console.Write($" {name}=\"{Preview(value)}\"");
	}

	private static string Preview(string value) =>
		value[..Math.Min(20, value.Length)];

	private static bool LooksLikeBase64(string s)
	{
		if (s.Length < 20) return false;
		foreach (char c in s)
		{
			if (!((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') ||
				  (c >= '0' && c <= '9') || c == '+' || c == '/' || c == '='))
				return false;
		}
		return true;
	}

	private static bool LooksLikeDescriptorBase64(string s)
	{
		if (!LooksLikeBase64(s)) return false;
		try
		{
			var bytes = Convert.FromBase64String(s);
			if (bytes.Length < 3 || bytes[0] != 0x0A) return false;

			int nameLen = 0;
			int shift = 0;
			int pos = 1;
			while (pos < bytes.Length)
			{
				byte b = bytes[pos++];
				nameLen |= (b & 0x7F) << shift;
				if ((b & 0x80) == 0) break;
				shift += 7;
			}

			if (nameLen <= 0 || pos + nameLen > bytes.Length) return false;
			var name = System.Text.Encoding.UTF8.GetString(bytes, pos, nameLen);
			return name.EndsWith(".proto", StringComparison.OrdinalIgnoreCase);
		}
		catch
		{
			return false;
		}
	}
}

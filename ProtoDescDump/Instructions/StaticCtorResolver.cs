using Iced.Intel;
using Mono.Cecil;

namespace ProtoDescDump;

internal static class StaticCtorResolver
{
	public static string? RecoverDescriptorBase64(MethodDefinition staticCtor, bool verbose = true)
	{
		if (staticCtor == null) throw new ArgumentNullException(nameof(staticCtor));

		uint rva = staticCtor.GetMethodRVA();
		if (rva == 0)
		{
			Console.WriteLine("Static constructor has no RVA; cannot resolve.");
			return null;
		}

		var address = new Il2cppFunctionAddressData(rva);
		var instructions = InstructionParser.GetInstructions(address, false);
		if (instructions.Count == 0)
		{
			Console.WriteLine("No native instructions decoded for static constructor.");
			return null;
		}

		var emulator = new X64Emulator(MainApp.moduleBytes, MainApp.baseAddress, MainApp.sectionTables);
		var stringLiterals = MainApp.Il2CppScript.addressMap.stringLiterals;

		var stringBySlotVa = new Dictionary<ulong, string>();
		foreach (var lit in stringLiterals)
		{
			stringBySlotVa[lit.VA] = lit.s_string;
		}

		var regString = new Dictionary<Register, string>();

		var arrayStore = new SortedDictionary<int, string>();

		Register? arrayReg = null;

		ulong? arrayStoreHelperVA = null;
		ulong? interleavedHelperVA = null;
		int nextSecondaryStoreIndex = 0;
		ulong? stringConcatVA = null;
		ulong? arrayNewVA = null;
		var arrayNewTargets = new HashSet<ulong>();

		var memString = new Dictionary<ulong, string>();
		ulong syntheticCounter = 0;

		var callTargets = new HashSet<ulong>();
		foreach (var instr in instructions)
		{
			if (instr.Mnemonic == Mnemonic.Call && instr.Op0Kind == OpKind.NearBranch64)
			{
				callTargets.Add(instr.NearBranchTarget);
			}
		}

		var instrByIP = new Dictionary<ulong, int>();
		for (int idx = 0; idx < instructions.Count; idx++)
			instrByIP[instructions[idx].IP] = idx;

		int arrayStoreCount = 0;
		int expectedArrayCount = 0;
		bool firstArrayAllocSeen = false;
		string? result = null;

		var visitCount = new Dictionary<ulong, int>();
		const int MaxVisitsPerIP = 10_000;
		for (int i = 0; i < instructions.Count;)
		{
			var instr = instructions[i];
			var visits = visitCount.GetValueOrDefault(instr.IP, 0);
			if (visits >= MaxVisitsPerIP) { i++; continue; }
			visitCount[instr.IP] = visits + 1;

			ulong? preMovSrcAddr = null;
			if (instr.Mnemonic == Mnemonic.Mov &&
				instr.Op0Kind == OpKind.Register &&
				instr.Op1Kind == OpKind.Memory)
			{
				preMovSrcAddr = instr.IsIPRelativeMemoryOperand
					? instr.IPRelativeMemoryAddress
					: emulator.EvaluateMemoryAddress(instr);
			}

			var jumpTarget = emulator.Step(instr);

			switch (instr.Mnemonic)
			{
				case Mnemonic.Lea:
					if (instr.Op0Kind == OpKind.Register && instr.Op1Kind == OpKind.Memory)
					{
						var dst = Canon(instr.Op0Register);
						var addr = emulator.GetRegister(dst);
						if (addr.HasValue && stringBySlotVa.TryGetValue(addr.Value, out var s))
						{
							regString[dst] = s;
						}
						else
						{
							regString.Remove(dst);
						}
					}
					break;

				case Mnemonic.Mov:
					if (instr.Op0Kind == OpKind.Register)
					{
						var dst = Canon(instr.Op0Register);

						if (instr.Op1Kind == OpKind.Register)
						{
							var src = Canon(instr.Op1Register);
							if (regString.TryGetValue(src, out var sv))
								regString[dst] = sv;
							else
								regString.Remove(dst);

							if (arrayReg.HasValue && src == arrayReg.Value)
								arrayReg = dst;
						}
						else if (instr.Op1Kind == OpKind.Memory)
						{
							if (preMovSrcAddr.HasValue && stringBySlotVa.TryGetValue(preMovSrcAddr.Value, out var ms))
							{
								regString[dst] = ms;
							}
							else
							{
								var regVal = emulator.GetRegister(dst);
								if (regVal.HasValue && stringBySlotVa.TryGetValue(regVal.Value, out var ms2))
									regString[dst] = ms2;
								else
									regString.Remove(dst);
							}
						}
						else if (instr.Op1Kind >= OpKind.Immediate8 && instr.Op1Kind <= OpKind.Immediate64)
						{
							var imm = unchecked((ulong)instr.Immediate64);
							if (stringBySlotVa.TryGetValue(imm, out var si))
								regString[dst] = si;
							else
								regString.Remove(dst);
						}
						else
						{
							regString.Remove(dst);
						}
					}
					else if (instr.Op0Kind == OpKind.Memory && instr.Op1Kind == OpKind.Register)
					{
						var writeAddr = emulator.EvaluateMemoryAddress(instr);
						if (writeAddr.HasValue)
						{
							var src = Canon(instr.Op1Register);
							if (regString.TryGetValue(src, out var strFromReg))
								memString[writeAddr.Value] = strFromReg;
							else
							{
								var srcVal = emulator.GetRegister(src);
								if (srcVal.HasValue && stringBySlotVa.TryGetValue(srcVal.Value, out var strFromVal))
									memString[writeAddr.Value] = strFromVal;
							}
						}
					}
					break;

				case Mnemonic.Xor:
					if (instr.Op0Kind == OpKind.Register && instr.Op1Kind == OpKind.Register
						&& instr.Op0Register == instr.Op1Register)
					{
						regString.Remove(Canon(instr.Op0Register));
					}
					break;

				case Mnemonic.Add:
				case Mnemonic.Sub:
				case Mnemonic.Inc:
				case Mnemonic.Dec:
				case Mnemonic.And:
				case Mnemonic.Or:
				case Mnemonic.Shl:
				case Mnemonic.Shr:
				case Mnemonic.Sar:
				case Mnemonic.Neg:
				case Mnemonic.Not:
				case Mnemonic.Imul:
					if (instr.Op0Kind == OpKind.Register)
						regString.Remove(Canon(instr.Op0Register));
					break;
			}

			if (instr.Mnemonic == Mnemonic.Call && instr.Op0Kind == OpKind.NearBranch64)
			{
				ulong target = instr.NearBranchTarget;

				if (target == MainApp.InitUsagesRVA)
				{
					ClearVolatileStrings(regString);
					emulator.ClearVolatileOnCall();
					goto endLoop;
				}

				var rcxVal = emulator.GetRegister(Register.RCX);
				var rdxVal = emulator.GetRegister(Register.RDX);
				var r8Val = emulator.GetRegister(Register.R8);
				var r9Val = emulator.GetRegister(Register.R9);

				regString.TryGetValue(Register.RCX, out var rcxStr);
				regString.TryGetValue(Register.RDX, out var rdxStr);
				regString.TryGetValue(Register.R8, out var r8Str);
				regString.TryGetValue(Register.R9, out var r9Str);

				if (verbose)
				{
					Console.Write($"  CALL 0x{target:X} | RCX={rcxVal?.ToString("X") ?? "?"} RDX={rdxVal?.ToString("X") ?? "?"} R8={r8Val?.ToString("X") ?? "?"}");
					if (rcxStr != null) Console.Write($" rcxS=\"{rcxStr.Substring(0, Math.Min(20, rcxStr.Length))}\"");
					if (rdxStr != null) Console.Write($" rdxS=\"{rdxStr.Substring(0, Math.Min(20, rdxStr.Length))}\"");
					if (r8Str != null) Console.Write($" r8S=\"{r8Str.Substring(0, Math.Min(20, r8Str.Length))}\"");
					if (r9Str != null) Console.Write($" r9S=\"{r9Str.Substring(0, Math.Min(20, r9Str.Length))}\"");
					Console.WriteLine();
				}

				if (result == null && rcxStr != null && rdxStr != null)
				{
					string candidate;
					int argCount;
					if (r8Str != null && r9Str != null)
					{
						candidate = rcxStr + rdxStr + r8Str + r9Str;
						argCount = 4;
					}
					else if (r8Str != null)
					{
						candidate = rcxStr + rdxStr + r8Str;
						argCount = 3;
					}
					else
					{
						candidate = rcxStr + rdxStr;
						argCount = 2;
					}
					if (LooksLikeDescriptorBase64(candidate))
					{
						result = candidate;
						stringConcatVA = target;
						if (verbose)
							Console.WriteLine($"[DirectConcat] String.Concat({argCount} args) at 0x{target:X}, length={result.Length}");
						regString[Register.RAX] = result;
						ClearVolatileStrings(regString, exceptRax: true);
						emulator.ClearVolatileOnCall();
						goto endLoop;
					}
					else
					{
						regString[Register.RAX] = candidate;
						ClearVolatileStrings(regString, exceptRax: true);
						emulator.ClearVolatileOnCall();
						goto endLoop;
					}
				}

				if (!firstArrayAllocSeen && rdxVal.HasValue && rdxVal.Value >= 1 && rdxVal.Value <= 2000
					&& r8Str == null
					&& target != arrayStoreHelperVA
					&& !arrayNewTargets.Contains(target))
				{
					arrayNewVA = target;
					arrayNewTargets.Add(target);
					expectedArrayCount = (int)rdxVal.Value;
					firstArrayAllocSeen = true;
					ClearVolatileStrings(regString);
					emulator.ClearVolatileOnCall();
					arrayReg = Register.RAX;
					ulong synBase0 = 0x7FFF_0000_0000_0000UL + syntheticCounter++ * 0x1000UL;
					emulator.ForceSetRegister(Register.RAX, synBase0);
					emulator.WriteMemory(synBase0 + 0x18, (ulong)expectedArrayCount, 4);
					if (verbose)
						Console.WriteLine($"[ArrayNew] Detected array allocation at 0x{instr.IP:X}, target=0x{target:X}, count={rdxVal.Value}");
					goto endLoop;
				}

				if (verbose && !firstArrayAllocSeen && rdxVal.HasValue && rdxVal.Value >= 1 && rdxVal.Value <= 2000)
					Console.WriteLine($"  [ArrayNewCandidate] at 0x{instr.IP:X} target=0x{target:X} count={rdxVal.Value}");

				if (firstArrayAllocSeen && arrayStoreCount > 0
					&& arrayNewTargets.Contains(target)
					&& rdxVal.HasValue && rdxVal.Value >= 1 && rdxVal.Value <= 1000)
				{
					var candidate = BuildConcatResult(arrayStore);
					if (verbose)
					{
						Console.WriteLine($"[NewArrayAlloc] New array at 0x{instr.IP:X}, count={rdxVal.Value}. Previous had {arrayStoreCount} chunks, len={candidate.Length}");
					}
					if (LooksLikeDescriptorBase64(candidate))
					{
						result = candidate;
						if (verbose) Console.WriteLine($"  -> Valid descriptor! length={result.Length}");
						break;
					}
					arrayStore.Clear();
					arrayStoreCount = 0;
					expectedArrayCount = (int)rdxVal.Value;
					arrayReg = Register.RAX;
					interleavedHelperVA = null;
					ClearVolatileStrings(regString);
					emulator.ClearVolatileOnCall();
					ulong synBaseN = 0x7FFF_0000_0000_0000UL + syntheticCounter++ * 0x1000UL;
					emulator.ForceSetRegister(Register.RAX, synBaseN);
					emulator.WriteMemory(synBaseN + 0x18, (ulong)expectedArrayCount, 4);
					if (verbose) Console.WriteLine("  -> Not valid descriptor, resetting for new array.");
					goto endLoop;
				}

				if (firstArrayAllocSeen && r8Str != null)
				{
					int index = rdxVal.HasValue ? (int)rdxVal.Value : -1;
					if (index >= 0 && index < 1000)
					{
						if (arrayStoreHelperVA == null)
						{
							arrayStoreHelperVA = target;
							if (verbose)
								Console.WriteLine($"[ArrayStore] Detected store helper at 0x{target:X}");
						}

						if (target == arrayStoreHelperVA)
						{
							arrayStore[index] = r8Str;
							arrayStoreCount++;
							nextSecondaryStoreIndex = index + 1;
							if (verbose)
								Console.WriteLine($"  [{index}] = \"{r8Str.Substring(0, Math.Min(40, r8Str.Length))}...\"");
						}
					}
				}

				if (firstArrayAllocSeen && arrayStoreCount > 0
					&& rdxStr != null && r8Str == null
					&& target != arrayStoreHelperVA
					&& target != interleavedHelperVA
					&& !arrayNewTargets.Contains(target)
					&& nextSecondaryStoreIndex < 1000
					&& LooksLikeBase64(rdxStr))
				{
					arrayStore[nextSecondaryStoreIndex] = rdxStr;
					arrayStoreCount++;
					if (verbose)
						Console.WriteLine($"[SecondaryStore] at 0x{target:X}, [{nextSecondaryStoreIndex}] = \"{rdxStr.Substring(0, Math.Min(40, rdxStr.Length))}...\"");
					nextSecondaryStoreIndex++;
				}

				if (firstArrayAllocSeen && arrayStoreCount > 0
					&& target != arrayStoreHelperVA
					&& !arrayNewTargets.Contains(target)
					&& r8Str == null && rdxStr == null && rcxStr == null)
				{
					if (interleavedHelperVA == null)
					{
						interleavedHelperVA = target;
						if (verbose)
							Console.WriteLine($"[InterleavedHelper] Detected helper at 0x{target:X}");
					}
				}

				if (firstArrayAllocSeen && arrayStoreCount > 0 &&
					arrayStoreCount >= expectedArrayCount &&
					expectedArrayCount > 0 &&
					target != arrayStoreHelperVA &&
					!arrayNewTargets.Contains(target) &&
					target != interleavedHelperVA &&
					stringConcatVA == null)
				{
					var candidate = BuildConcatResult(arrayStore);
					if (verbose)
					{
						Console.WriteLine($"[Concat] String.Concat candidate at 0x{target:X}");
						Console.WriteLine($"  Reconstructed {arrayStoreCount} chunks, total length = {candidate.Length}");
					}

					if (LooksLikeDescriptorBase64(candidate))
					{
						stringConcatVA = target;
						result = candidate;

						regString[Register.RAX] = result;
						emulator.ClearVolatileOnCall();
						ClearVolatileStrings(regString, exceptRax: true);
						goto endLoop;
					}
					else
					{
						if (verbose)
							Console.WriteLine("  Candidate is not valid descriptor base64, resetting state...");
						firstArrayAllocSeen = false;
						arrayNewVA = null;
						stringConcatVA = null;
						arrayStore.Clear();
						arrayStoreCount = 0;
						expectedArrayCount = 0;
						arrayReg = null;
						interleavedHelperVA = null;
					}
				}

				if (result == null)
				{
					foreach (var checkVal in new[] { rcxVal, rdxVal, r8Val }.OfType<ulong>())
					{
						var memChunks = new SortedDictionary<int, string>();
						for (int slot = 0; slot < 2000; slot++)
						{
							ulong elemAddr = checkVal + 32UL + (ulong)(slot * 8);
							if (memString.TryGetValue(elemAddr, out var s))
								memChunks[slot] = s;
							else
								break;
						}
						if (memChunks.Count > 0)
						{
							var mergedChunks = new SortedDictionary<int, string>(memChunks);

							foreach (var kv in arrayStore)
								mergedChunks[kv.Key] = kv.Value;

							bool complete =
								!firstArrayAllocSeen ||
								expectedArrayCount <= 0 ||
								(mergedChunks.Count >= expectedArrayCount &&
								 Enumerable.Range(0, expectedArrayCount).All(mergedChunks.ContainsKey));

							if (!complete)
							{
								if (verbose)
									Console.WriteLine($"[MemArrayConcat] Skipping partial candidate: {mergedChunks.Count}/{expectedArrayCount} chunks");

								continue;
							}

							var candidate = string.Concat(mergedChunks.Values);

							if (LooksLikeDescriptorBase64(candidate))
							{
								result = candidate;
								stringConcatVA = target;
								if (verbose) Console.WriteLine($"[MemArrayConcat] {memChunks.Count} inline chunks at 0x{target:X}, length={result.Length}");
								regString[Register.RAX] = result;
								ClearVolatileStrings(regString, exceptRax: true);
								emulator.ClearVolatileOnCall();
								goto endLoop;
							}
						}
					}
				}

				if (rcxStr != null && rdxStr == null && r8Str == null && r9Str == null)
					regString[Register.RAX] = rcxStr;

				else if (result == null && rcxStr == null && rdxStr != null && r8Str == null && r9Str == null)
				{
					regString[Register.RAX] = rdxStr;
					ClearVolatileStrings(regString, exceptRax: true);
					emulator.ClearVolatileOnCall();
					goto endLoop;
				}

				ClearVolatileStrings(regString, exceptRax: rcxStr != null && rdxStr == null && r8Str == null && r9Str == null);
				emulator.ClearVolatileOnCall();
			}

		endLoop:
			if (jumpTarget.HasValue && instrByIP.TryGetValue(jumpTarget.Value, out int jumpIdx))
				i = jumpIdx;
			else
				i++;
		}

		if (result == null && arrayStoreCount > 0)
		{
			var candidate = BuildConcatResult(arrayStore);
			if (verbose)
				Console.WriteLine($"[EndFallback] Trying {arrayStoreCount} remaining chunks, total length = {candidate.Length}");
			if (LooksLikeDescriptorBase64(candidate))
				result = candidate;
		}

		if (result == null)
		{
			Console.WriteLine("Failed to locate descriptor base64 in static constructor.");
			Console.WriteLine($"  Array stores found: {arrayStoreCount}");
			Console.WriteLine($"  Array store helper VA: {(arrayStoreHelperVA.HasValue ? $"0x{arrayStoreHelperVA.Value:X}" : "not found")}");
			return null;
		}

		if (verbose)
			Console.WriteLine($"Recovered base64 has length {result.Length}.");

		return result;
	}

	private static Register Canon(Register r) => r switch
	{
		Register.RCX or Register.ECX or Register.CL => Register.RCX,
		Register.RDX or Register.EDX or Register.DL => Register.RDX,
		Register.R8 or Register.R8D or Register.R8W or Register.R8L => Register.R8,
		Register.R9 or Register.R9D => Register.R9,
		Register.RAX or Register.EAX or Register.AL or Register.AH => Register.RAX,
		Register.RBX or Register.EBX or Register.BL or Register.BH => Register.RBX,
		Register.RSI or Register.ESI or Register.SIL => Register.RSI,
		Register.RDI or Register.EDI or Register.DIL => Register.RDI,
		Register.RBP or Register.EBP or Register.BPL => Register.RBP,
		Register.RSP or Register.ESP or Register.SPL => Register.RSP,
		Register.R10 or Register.R10D => Register.R10,
		Register.R11 or Register.R11D => Register.R11,
		Register.R12 or Register.R12D => Register.R12,
		Register.R13 or Register.R13D => Register.R13,
		Register.R14 or Register.R14D => Register.R14,
		Register.R15 or Register.R15D => Register.R15,
		_ => r
	};

	private static void ClearVolatileStrings(Dictionary<Register, string> regs, bool exceptRax = false)
	{
		regs.Remove(Register.RCX);
		regs.Remove(Register.RDX);
		regs.Remove(Register.R8);
		regs.Remove(Register.R9);
		if (!exceptRax) regs.Remove(Register.RAX);
		regs.Remove(Register.R10);
		regs.Remove(Register.R11);
	}

	private static string BuildConcatResult(SortedDictionary<int, string> arrayStore)
	{
		var sb = new System.Text.StringBuilder();
		foreach (var kvp in arrayStore)
			sb.Append(kvp.Value);
		return sb.ToString();
	}

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
			if (bytes.Length < 3 || bytes[0] != 0x0a) return false;
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
		catch { return false; }
	}
}

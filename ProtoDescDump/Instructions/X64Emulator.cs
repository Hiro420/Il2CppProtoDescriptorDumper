using Iced.Intel;

namespace ProtoDescDump;

internal sealed class X64Emulator
{
	private readonly byte[] _moduleBytes;
	private readonly ulong _imageBase;
	private readonly IReadOnlyList<PEHeader.SectionTable> _sections;

	private readonly Dictionary<Register, ulong?> _regs = new();
	private readonly Dictionary<ulong, byte> _syntheticMemory = new();
	private bool? _zf;
	private bool? _sf;
	private bool? _cf;
	private bool? _of;

	public X64Emulator(byte[] moduleBytes, ulong imageBase, IReadOnlyList<PEHeader.SectionTable> sections)
	{
		_moduleBytes = moduleBytes ?? throw new ArgumentNullException(nameof(moduleBytes));
		_imageBase = imageBase;
		_sections = sections ?? throw new ArgumentNullException(nameof(sections));
	}

	public ulong? GetRegister(Register register)
	{
		register = Canonicalize(register);
		_regs.TryGetValue(register, out var value);
		return value;
	}

	public void ClearVolatileOnCall()
	{
		Clear(Register.RCX);
		Clear(Register.RDX);
		Clear(Register.R8);
		Clear(Register.R9);
		Clear(Register.RAX);
		Clear(Register.R10);
		Clear(Register.R11);
	}

	public void ForceSetRegister(Register register, ulong value) => SetRegister(register, value);

	public void WriteMemory(ulong address, ulong value, int size = 8)
	{
		for (int i = 0; i < size; i++)
			_syntheticMemory[address + (ulong)i] = (byte)(value >> (i * 8));
	}

	private void Clear(Register reg)
	{
		reg = Canonicalize(reg);
		_regs.Remove(reg);
	}

	private void SetRegister(Register register, ulong? value)
	{
		register = Canonicalize(register);
		if (value.HasValue)
		{
			_regs[register] = value.Value;
		}
		else
		{
			_regs.Remove(register);
		}
	}

	public static Register Canonicalize(Register reg)
	{
		return reg switch
		{
			Register.RCX or Register.ECX or Register.CX or Register.CL or Register.CH => Register.RCX,
			Register.RDX or Register.EDX or Register.DX or Register.DL or Register.DH => Register.RDX,
			Register.R8 or Register.R8D or Register.R8W or Register.R8L => Register.R8,
			Register.R9 or Register.R9D or Register.R9W or Register.R9L => Register.R9,
			Register.RAX or Register.EAX or Register.AX or Register.AL or Register.AH => Register.RAX,
			Register.RBX or Register.EBX or Register.BX or Register.BL or Register.BH => Register.RBX,
			Register.RSI or Register.ESI or Register.SI or Register.SIL => Register.RSI,
			Register.RDI or Register.EDI or Register.DI or Register.DIL => Register.RDI,
			Register.RBP or Register.EBP or Register.BP or Register.BPL => Register.RBP,
			Register.RSP or Register.ESP or Register.SP or Register.SPL => Register.RSP,
			Register.R10 or Register.R10D or Register.R10W or Register.R10L => Register.R10,
			Register.R11 or Register.R11D or Register.R11W or Register.R11L => Register.R11,
			Register.R12 or Register.R12D or Register.R12W or Register.R12L => Register.R12,
			Register.R13 or Register.R13D or Register.R13W or Register.R13L => Register.R13,
			Register.R14 or Register.R14D or Register.R14W or Register.R14L => Register.R14,
			Register.R15 or Register.R15D or Register.R15W or Register.R15L => Register.R15,
			_ => reg
		};
	}

	public ulong? Step(Instruction instruction)
	{
		switch (instruction.Mnemonic)
		{
			case Mnemonic.Mov: ExecuteMov(instruction); break;
			case Mnemonic.Lea: ExecuteLea(instruction); break;
			case Mnemonic.Add: ExecuteAdd(instruction); break;
			case Mnemonic.Sub: ExecuteSub(instruction); break;
			case Mnemonic.Xor: ExecuteXor(instruction); break;
			case Mnemonic.Inc: ExecuteInc(instruction); break;
			case Mnemonic.Dec: ExecuteDec(instruction); break;
			case Mnemonic.Cmp: ExecuteCmp(instruction); break;
			case Mnemonic.Test: ExecuteTest(instruction); break;
			case Mnemonic.Je: return EvalJump(instruction, _zf == true);
			case Mnemonic.Jne: return EvalJump(instruction, _zf == false);
			case Mnemonic.Jbe: return EvalJump(instruction, _cf == true || _zf == true);
			case Mnemonic.Ja: return EvalJump(instruction, _cf == false && _zf == false);
			case Mnemonic.Jl: return EvalJump(instruction, _sf.HasValue && _of.HasValue && _sf != _of);
			case Mnemonic.Jge: return EvalJump(instruction, _sf.HasValue && _of.HasValue && _sf == _of);
			case Mnemonic.Jle: return EvalJump(instruction, (_sf.HasValue && _of.HasValue && _sf != _of) || _zf == true);
			case Mnemonic.Jg: return EvalJump(instruction, _sf.HasValue && _of.HasValue && _sf == _of && _zf == false);
			case Mnemonic.Jb: return EvalJump(instruction, _cf == true);
			case Mnemonic.Jae: return EvalJump(instruction, _cf == false);
			case Mnemonic.Js: return EvalJump(instruction, _sf == true);
			case Mnemonic.Jns: return EvalJump(instruction, _sf == false);
			case Mnemonic.Jmp:
				if (instruction.Op0Kind == OpKind.NearBranch64)
					return instruction.NearBranchTarget;
				break;
			default: break;
		}
		return null;
	}

	private void ExecuteMov(Instruction instruction)
	{
		if (instruction.Op0Kind == OpKind.Register)
		{
			var dst = instruction.Op0Register;
			ulong? value = null;
			switch (instruction.Op1Kind)
			{
				case OpKind.Register:
					value = GetRegister(instruction.Op1Register);
					break;
				case OpKind.Immediate8:
				case OpKind.Immediate16:
				case OpKind.Immediate32:
				case OpKind.Immediate64:
					value = unchecked((ulong)instruction.Immediate64);
					break;
				case OpKind.Memory:
					{
						var addr = EvaluateMemoryAddress(instruction);
						if (addr.HasValue)
						{
							value = ReadSyntheticMemory(addr.Value, instruction.MemorySize)
								?? ReadFromModuleSize(addr.Value, instruction.MemorySize);
						}
						break;
					}
			}
			SetRegister(dst, value);
		}
		else if (instruction.Op0Kind == OpKind.Memory)
		{
			var addr = EvaluateMemoryAddress(instruction);
			if (addr.HasValue)
			{
				ulong? value = null;
				if (instruction.Op1Kind == OpKind.Register)
					value = GetRegister(instruction.Op1Register);
				else if (instruction.Op1Kind == OpKind.Immediate8 || instruction.Op1Kind == OpKind.Immediate16 ||
						 instruction.Op1Kind == OpKind.Immediate32 || instruction.Op1Kind == OpKind.Immediate64 ||
						 instruction.Op1Kind == OpKind.Immediate8to16 || instruction.Op1Kind == OpKind.Immediate8to32 ||
						 instruction.Op1Kind == OpKind.Immediate8to64 || instruction.Op1Kind == OpKind.Immediate32to64)
					value = unchecked((ulong)instruction.Immediate64);
				if (value.HasValue)
					WriteMemory(addr.Value, value.Value, instruction.MemorySize switch
					{
						MemorySize.UInt8 or MemorySize.Int8 => 1,
						MemorySize.UInt16 or MemorySize.Int16 => 2,
						MemorySize.UInt32 or MemorySize.Int32 => 4,
						MemorySize.UInt64 or MemorySize.Int64 => 8,
						_ => 8
					});
			}
		}
	}

	private void ExecuteLea(Instruction instruction)
	{
		if (instruction.Op0Kind != OpKind.Register || instruction.Op1Kind != OpKind.Memory)
			return;

		var dst = instruction.Op0Register;
		var addr = EvaluateMemoryAddress(instruction);
		SetRegister(dst, addr);
	}

	private void ExecuteAdd(Instruction instruction)
	{
		if (instruction.Op0Kind != OpKind.Register)
			return;

		var dst = instruction.Op0Register;
		var cur = GetRegister(dst);
		if (!cur.HasValue)
			return;

		ulong addend;
		if (instruction.Op1Kind == OpKind.Immediate8 ||
			instruction.Op1Kind == OpKind.Immediate16 ||
			instruction.Op1Kind == OpKind.Immediate32 ||
			instruction.Op1Kind == OpKind.Immediate64)
		{
			addend = unchecked((ulong)instruction.Immediate64);
		}
		else if (instruction.Op1Kind == OpKind.Register)
		{
			var srcVal = GetRegister(instruction.Op1Register);
			if (!srcVal.HasValue)
				return;
			addend = srcVal.Value;
		}
		else
		{
			return;
		}

		SetRegister(dst, cur.Value + addend);
		InvalidateFlags();
	}

	private void ExecuteSub(Instruction instruction)
	{
		if (instruction.Op0Kind != OpKind.Register)
			return;

		var dst = instruction.Op0Register;
		var cur = GetRegister(dst);
		if (!cur.HasValue)
			return;

		ulong subtrahend;
		if (instruction.Op1Kind == OpKind.Immediate8 ||
			instruction.Op1Kind == OpKind.Immediate16 ||
			instruction.Op1Kind == OpKind.Immediate32 ||
			instruction.Op1Kind == OpKind.Immediate64)
		{
			subtrahend = unchecked((ulong)instruction.Immediate64);
		}
		else if (instruction.Op1Kind == OpKind.Register)
		{
			var srcVal = GetRegister(instruction.Op1Register);
			if (!srcVal.HasValue)
				return;
			subtrahend = srcVal.Value;
		}
		else
		{
			return;
		}

		SetRegister(dst, cur.Value - subtrahend);
		InvalidateFlags();
	}

	private void ExecuteXor(Instruction instruction)
	{
		if (instruction.Op0Kind != OpKind.Register || instruction.Op1Kind != OpKind.Register)
			return;

		var dst = instruction.Op0Register;
		if (dst == instruction.Op1Register)
		{
			SetRegister(dst, 0);
			_zf = true; _sf = false; _cf = false; _of = false;
		}
		else
		{
			InvalidateFlags();
		}
	}

	private void ExecuteInc(Instruction instruction)
	{
		if (instruction.Op0Kind != OpKind.Register)
			return;
		var dst = instruction.Op0Register;
		var cur = GetRegister(dst) ?? 0UL;
		SetRegister(dst, cur + 1UL);
		InvalidateFlags();
	}

	private void ExecuteDec(Instruction instruction)
	{
		if (instruction.Op0Kind != OpKind.Register)
			return;
		var dst = instruction.Op0Register;
		var cur = GetRegister(dst) ?? 0UL;
		SetRegister(dst, cur - 1UL);
		InvalidateFlags();
	}

	public ulong? EvaluateMemoryAddress(Instruction instruction)
	{
		if (instruction.IsIPRelativeMemoryOperand)
		{
			return instruction.IPRelativeMemoryAddress;
		}

		ulong baseVal = 0;
		if (instruction.MemoryBase != Register.None)
		{
			var val = GetRegister(instruction.MemoryBase);
			if (!val.HasValue)
				return null;
			baseVal = val.Value;
		}

		ulong indexVal = 0;
		if (instruction.MemoryIndex != Register.None)
		{
			var val = GetRegister(instruction.MemoryIndex);
			if (!val.HasValue)
				return null;
			indexVal = val.Value;
		}

		var disp = unchecked((ulong)instruction.MemoryDisplacement64);
		var scale = (ulong)instruction.MemoryIndexScale;
		return baseVal + indexVal * scale + disp;
	}

	private ulong? ReadPointer(ulong va)
	{
		if (!TryVaToOffset(va, out var offset))
			return null;

		if (offset + 8 > (ulong)_moduleBytes.Length)
			return null;

		return BitConverter.ToUInt64(_moduleBytes, (int)offset);
	}

	private ulong? ReadUInt32(ulong va)
	{
		if (!TryVaToOffset(va, out var offset))
			return null;

		if (offset + 4 > (ulong)_moduleBytes.Length)
			return null;

		return BitConverter.ToUInt32(_moduleBytes, (int)offset);
	}

	private ulong? ReadUInt16(ulong va)
	{
		if (!TryVaToOffset(va, out var offset))
			return null;

		if (offset + 2 > (ulong)_moduleBytes.Length)
			return null;

		return BitConverter.ToUInt16(_moduleBytes, (int)offset);
	}

	private ulong? ReadByte(ulong va)
	{
		if (!TryVaToOffset(va, out var offset))
			return null;

		if (offset >= (ulong)_moduleBytes.Length)
			return null;

		return _moduleBytes[(int)offset];
	}

	private bool TryVaToOffset(ulong va, out ulong offset)
	{
		var rva = (uint)(va - _imageBase);
		foreach (var s in _sections)
		{
			uint start = s.virtualAddr;
			uint end = start + s.virtualSize;
			if (rva >= start && rva < end)
			{
				offset = s.ptrToRawData + (ulong)(rva - start);
				return true;
			}
		}
		offset = 0;
		return false;
	}

	private ulong? ReadFromModuleSize(ulong addr, MemorySize size)
	{
		if (size == MemorySize.UInt64 || size == MemorySize.Int64) return ReadPointer(addr);
		if (size == MemorySize.UInt32 || size == MemorySize.Int32) return ReadUInt32(addr);
		if (size == MemorySize.UInt16 || size == MemorySize.Int16) return ReadUInt16(addr);
		if (size == MemorySize.UInt8 || size == MemorySize.Int8) return ReadByte(addr);
		return ReadPointer(addr);
	}

	private ulong? ReadOpValue(Instruction instruction, int opIndex)
	{
		var kind = opIndex == 0 ? instruction.Op0Kind : instruction.Op1Kind;
		switch (kind)
		{
			case OpKind.Register:
				{
					var reg = opIndex == 0 ? instruction.Op0Register : instruction.Op1Register;
					return GetRegister(reg);
				}
			case OpKind.Immediate8:
			case OpKind.Immediate16:
			case OpKind.Immediate32:
			case OpKind.Immediate64:
			case OpKind.Immediate8to16:
			case OpKind.Immediate8to32:
			case OpKind.Immediate8to64:
			case OpKind.Immediate32to64:
				return GetImmediate(instruction);
			case OpKind.Memory:
				{
					var addr = EvaluateMemoryAddress(instruction);
					if (!addr.HasValue) return null;
					return ReadSyntheticMemory(addr.Value, instruction.MemorySize)
						?? ReadFromModuleSize(addr.Value, instruction.MemorySize);
				}
			default: return null;
		}
	}

	private void ExecuteCmp(Instruction instruction)
	{
		var a = ReadOpValue(instruction, 0);
		var b = ReadOpValue(instruction, 1);
		if (!a.HasValue || !b.HasValue) { InvalidateFlags(); return; }

		ulong r = unchecked(a.Value - b.Value);
		_zf = r == 0;
		_cf = a.Value < b.Value;
		_sf = (long)r < 0;
		long sa = (long)a.Value, sb = (long)b.Value, sr = (long)r;
		_of = (sa > 0 && sb < 0 && sr < 0) || (sa < 0 && sb > 0 && sr > 0);
	}

	private void ExecuteTest(Instruction instruction)
	{
		var a = ReadOpValue(instruction, 0);
		var b = ReadOpValue(instruction, 1);
		if (!a.HasValue || !b.HasValue) { InvalidateFlags(); return; }
		ulong r = a.Value & b.Value;
		_zf = r == 0;
		_sf = (long)r < 0;
		_cf = false;
		_of = false;
	}

	private void InvalidateFlags() { _zf = null; _sf = null; _cf = null; _of = null; }

	private ulong? EvalJump(Instruction instruction, bool? taken)
	{
		if (taken == true && instruction.Op0Kind == OpKind.NearBranch64)
			return instruction.NearBranchTarget;

		return null;
	}

	private ulong? ReadSyntheticMemory(ulong address, MemorySize size)
	{
		int byteCount = size switch
		{
			MemorySize.UInt8 or MemorySize.Int8 => 1,
			MemorySize.UInt16 or MemorySize.Int16 => 2,
			MemorySize.UInt32 or MemorySize.Int32 => 4,
			MemorySize.UInt64 or MemorySize.Int64 => 8,
			_ => 8
		};

		ulong value = 0;

		for (int i = 0; i < byteCount; i++)
		{
			if (!_syntheticMemory.TryGetValue(address + (ulong)i, out var b))
				return null;

			value |= (ulong)b << (i * 8);
		}

		return value;
	}

	private static ulong GetImmediate(Instruction instruction)
	{
		return instruction.Op1Kind switch
		{
			OpKind.Immediate8 => instruction.Immediate8,
			OpKind.Immediate16 => instruction.Immediate16,
			OpKind.Immediate32 => instruction.Immediate32,
			OpKind.Immediate64 => instruction.Immediate64,

			OpKind.Immediate8to16 => unchecked((ulong)(short)instruction.Immediate8to16),
			OpKind.Immediate8to32 => unchecked((ulong)(int)instruction.Immediate8to32),
			OpKind.Immediate8to64 => unchecked((ulong)instruction.Immediate8to64),
			OpKind.Immediate32to64 => unchecked((ulong)instruction.Immediate32to64),

			_ => unchecked((ulong)instruction.Immediate64)
		};
	}
}

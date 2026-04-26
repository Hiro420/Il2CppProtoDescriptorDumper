namespace ProtoDescDump;

public class Il2cppFunctionAddressData
{
	public uint RVA { get; set; }
	public uint Offset { get; set; }
	public ulong VA { get; set; }

	public Il2cppFunctionAddressData(uint _RVA)
	{
		PEHeader.SectionTable? il2cpp_section = null;
		foreach (var section in MainApp.sectionTables)
		{
			uint sectionStart = section.virtualAddr;
			uint sectionEnd = sectionStart + section.virtualSize;

			if (_RVA >= sectionStart && _RVA < sectionEnd)
			{
				il2cpp_section = section;
			}
		}
		if (il2cpp_section == null)
		{
			Console.WriteLine($"Couldnt find section for method at RVA 0x{_RVA:X}");
			Environment.Exit(0);
			return;
		}
		RVA = _RVA;
		Offset = il2cpp_section.Value.ptrToRawData + (_RVA - il2cpp_section.Value.virtualAddr);
		VA = MainApp.baseAddress + _RVA;
	}
}

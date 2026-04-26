namespace ProtoDescDump;

public class Misc
{
	public static string ReadStr(StreamReader inStream, int count)
	{
		char[] chArray = new char[count];
		inStream.Read(chArray, 0, count);
		return new string(chArray);
	}

	public static byte Read8Le(StreamReader inStream)
	{
		return (byte)inStream.Read();
	}

	public static ushort Read16Le(StreamReader inStream)
	{
		ushort value = (byte)inStream.Read();
		value |= (ushort)(inStream.Read() << 8);
		return value;
	}

	public static uint Read32Le(StreamReader inStream)
	{
		uint value = (byte)inStream.Read();
		value |= (uint)(inStream.Read() << 8);
		value |= (uint)(inStream.Read() << 16);
		value |= (uint)(inStream.Read() << 24);
		return value;
	}

	public static ulong Read64Le(StreamReader inStream)
	{
		ulong value = (byte)inStream.Read();
		value |= ((ulong)inStream.Read() << 8);
		value |= ((ulong)inStream.Read() << 16);
		value |= ((ulong)inStream.Read() << 24);
		value |= ((ulong)inStream.Read() << 32);
		value |= ((ulong)inStream.Read() << 40);
		value |= ((ulong)inStream.Read() << 48);
		value |= ((ulong)inStream.Read() << 56);
		return value;
	}

	public static void PrintSections(PEHeader.DosHeader dosHeader)
	{
		PEHeader.SectionTable[]? sections = dosHeader.section_table;
		Console.WriteLine("\nSections: ");

		for (int idx = 0; idx < dosHeader.pe.numberOfSections; idx++)
		{
			Console.WriteLine($"   Name: {sections![idx].name}");
			Console.WriteLine($"       VirtualAddress:        {sections[idx].virtualAddr:X}");
			Console.WriteLine($"       VirtualSize:           {sections[idx].virtualSize:X}");
			Console.WriteLine($"       SizeOfRawData:         {sections[idx].sizeOfRawData:X}");
			Console.WriteLine($"       PointerToRawData:      {sections[idx].ptrToRawData:X}");
			Console.WriteLine($"       PointerToRelocations:  {sections[idx].ptrToReloc:X}");
			Console.WriteLine($"       PointerToLineNumbers:  {sections[idx].ptrToLineNum:X}");
			Console.WriteLine($"       NumberOfRelocations:   {sections[idx].numberOfReloc:X}");
			Console.WriteLine($"       NumberOfLineNumbers:   {sections[idx].numberOfLineNum:X}");
			Console.WriteLine($"       characteristics:       {sections[idx].characteristics:X}");
			PEHeader.PrintSectionCharacteristics(sections[idx].characteristics);
		}
	}

	public static void LoadFile(int argc, string[] argv)
	{
		for (int idx = 1; idx <= argc; idx++)
		{
			using (FileStream? fs = new FileStream(argv[idx - 1], FileMode.Open, FileAccess.Read))
			{
				if (fs == null)
				{
					Console.WriteLine($"Can't open '{argv[idx]}' file, exiting");
					continue;
				}

				using (BinaryReader? reader = new BinaryReader(fs))
				{
					PEHeader.DosHeader dosHeader = new PEHeader.DosHeader();

					PEHeader.ReadDos(reader, ref dosHeader);
					PEHeader.ReadPe(reader, ref dosHeader);

					if (dosHeader.pe.signature != 0x4550)
					{
						Console.WriteLine("invalid pe signature, file is likely corrupt pe, or not a valid pe file.");
						return;
					}

					PEHeader.ReadDataDir(reader, ref dosHeader);
					PEHeader.ReadSections(reader, ref dosHeader);
					PEHeader.ReadDataOffset(ref dosHeader);
					PEHeader.ReadExportDir(reader, ref dosHeader);
					PEHeader.ReadImportDir(reader, ref dosHeader);

					Console.WriteLine($"Parsing File: {argv[idx - 1]} \n");

					PrintHeaders(ref dosHeader);
					PrintDataTables(ref dosHeader);
					PrintSections(dosHeader);
					PrintExports(ref dosHeader);
					PrintImports(ref dosHeader);

					PEHeader.Cleanup(ref dosHeader);
				}
			}
		}
	}

	public static List<PEHeader.SectionTable> GetPeHeaderSectionTables(string argv)
	{
		using (FileStream? fs = new FileStream(argv, FileMode.Open, FileAccess.Read))
		{
			if (fs == null)
			{
				Console.WriteLine($"Can't open '{argv}' file, exiting");
				Environment.Exit(1);
			}

			using (BinaryReader? reader = new BinaryReader(fs))
			{
				PEHeader.DosHeader dosHeader = new PEHeader.DosHeader();

				PEHeader.ReadDos(reader, ref dosHeader);
				PEHeader.ReadPe(reader, ref dosHeader);

				if (dosHeader.pe.signature != 0x4550)
				{
					Console.WriteLine("invalid pe signature, file is likely corrupt pe, or not a valid pe file.");
					Environment.Exit(1);
				}

				PEHeader.ReadDataDir(reader, ref dosHeader);
				PEHeader.ReadSections(reader, ref dosHeader);
				PEHeader.ReadDataOffset(ref dosHeader);
				PEHeader.ReadExportDir(reader, ref dosHeader);
				PEHeader.ReadImportDir(reader, ref dosHeader);


				PEHeader.SectionTable[]? sections = dosHeader.section_table;


				return sections?.ToList() ?? new List<PEHeader.SectionTable>();
			}
		}
	}

	public static ulong GetVA(string argv)
	{
		using (FileStream? fs = new FileStream(argv, FileMode.Open, FileAccess.Read))
		{
			if (fs == null)
			{
				Console.WriteLine($"Can't open '{argv}' file, exiting");
				Environment.Exit(1);
			}

			using (BinaryReader? reader = new BinaryReader(fs))
			{
				PEHeader.DosHeader dosHeader = new PEHeader.DosHeader();

				PEHeader.ReadDos(reader, ref dosHeader);
				PEHeader.ReadPe(reader, ref dosHeader);

				if (dosHeader.pe.signature != 0x4550)
				{
					Console.WriteLine("invalid pe signature, file is likely corrupt pe, or not a valid pe file.");
					Environment.Exit(1);
				}

				PEHeader.ReadDataDir(reader, ref dosHeader);
				PEHeader.ReadSections(reader, ref dosHeader);
				PEHeader.ReadDataOffset(ref dosHeader);
				PEHeader.ReadExportDir(reader, ref dosHeader);
				PEHeader.ReadImportDir(reader, ref dosHeader);

				return dosHeader.pe.optionalHeader.imageBase;
			}
		}
	}

	private static ulong ImageBase(ref PEHeader.DosHeader dosHeader)
	{
		return dosHeader.pe.optionalHeader.imageBase - 1;
	}

	public static void PrintHeaders(ref PEHeader.DosHeader dosHeader)
	{
		Console.WriteLine($"magic bytes: \t\t{(char)(0xff & dosHeader.magic)}{(char)(dosHeader.magic >> 8)}");
		Console.WriteLine($"pe Offset    \t\t{dosHeader.e_lfanew:X}");

		Console.WriteLine("\nPE header information");
		Console.WriteLine($" signature:   \t\t0x{dosHeader.pe.signature:X} {(char)(0xff & dosHeader.pe.signature)}{(char)(0xff & (dosHeader.pe.signature >> 8))} ");
		Console.Write(" Machine:  \t\t");
		PEHeader.PrintMachine(dosHeader.pe.machine);
		Console.WriteLine($" Sections: \t\t{dosHeader.pe.numberOfSections}");
		Console.WriteLine($" Time Stamp: \t\t0x{dosHeader.pe.timeStamp:X}");
		Console.WriteLine($" Symbol Table Pointer:  0x{dosHeader.pe.symTablePtr:X}");
		Console.WriteLine($" Symbols:               {dosHeader.pe.numberOfSym}");
		Console.WriteLine($" optionalHeader Size:    {dosHeader.pe.optionalHeaderSize} (0x{dosHeader.pe.optionalHeaderSize:X})");
		Console.WriteLine($" characteristics:       0x{dosHeader.pe.characteristics:X}");
		PEHeader.PrintPeCharacteristics(dosHeader.pe.characteristics);

		Console.WriteLine("\nOptional Header");
		Console.Write("magic:      ");
		PEHeader.PrintMagic(dosHeader.pe.optionalHeader.magic);
		Console.WriteLine($"MajorLinkerVersion:      0x{dosHeader.pe.optionalHeader.majorLinkerVer:X}");
		Console.WriteLine($"MinorLinkerVersion:      0x{dosHeader.pe.optionalHeader.minorLinkerVer:X}");
		Console.WriteLine($"SizeOfCode:              0x{dosHeader.pe.optionalHeader.sizeOfCode:X}");
		Console.WriteLine($"SizeOfInitializedData:   0x{dosHeader.pe.optionalHeader.sizeOfInitializedData:X}");
		Console.WriteLine($"SizeOfUninitializedData: 0x{dosHeader.pe.optionalHeader.sizeOfUninitializedData:X}");
		Console.WriteLine($"EntryPoint:              0x{dosHeader.pe.optionalHeader.entryPoint:X}");
		Console.WriteLine($"BaseOfCode:              0x{dosHeader.pe.optionalHeader.baseOfCode:X}");
		if (dosHeader.pe.optionalHeader.magic == 0x10b)
		{
			Console.WriteLine($"BaseOfData:              0x{dosHeader.pe.optionalHeader.baseOfData:X}");
		}
		Console.WriteLine($"ImageBase:               {dosHeader.pe.optionalHeader.imageBase - 1:X}");
		Console.WriteLine($"SectionAlignment:        0x{dosHeader.pe.optionalHeader.sectionAlignment:X}");
		Console.WriteLine($"FileAlignment:           0x{dosHeader.pe.optionalHeader.fileAlignment:X}");
		Console.WriteLine($"MajorOSVersion:          0x{dosHeader.pe.optionalHeader.majorOSVer:X}");
		Console.WriteLine($"MinorOSVersion:          0x{dosHeader.pe.optionalHeader.minorOSVer:X}");
		Console.WriteLine($"MajorImageVersion:       0x{dosHeader.pe.optionalHeader.majorImageVer:X}");
		Console.WriteLine($"MinorImageVersion:       0x{dosHeader.pe.optionalHeader.minorImageVer:X}");
		Console.WriteLine($"MajorSubsysVersion:      0x{dosHeader.pe.optionalHeader.majorSubsystemVer:X}");
		Console.WriteLine($"MinorSubsysVersion:      0x{dosHeader.pe.optionalHeader.minorSubsystemVer:X}");
		Console.WriteLine($"Win32VersionValue:       0x{dosHeader.pe.optionalHeader.win32VersionVal:X}");
		Console.WriteLine($"SizeOfImage:             0x{dosHeader.pe.optionalHeader.sizeOfImage:X}");
		Console.WriteLine($"SizeOfHeaders:           0x{dosHeader.pe.optionalHeader.sizeOfHeaders:X}");
		Console.WriteLine($"CheckSum:                0x{dosHeader.pe.optionalHeader.checkSum:X}");
		Console.Write("Subsystem:             ");
		PEHeader.PrintSubsystem(dosHeader.pe.optionalHeader.subsystem);
		Console.WriteLine("DllCharacteristics:           ");
		PEHeader.PrintDllCharacteristics(dosHeader.pe.optionalHeader.dllCharacteristics);

		Console.WriteLine($"SizeOfStackReserve:      {dosHeader.pe.optionalHeader.sizeOfStackReserve:X}");
		Console.WriteLine($"SizeOfStackCommit:       {dosHeader.pe.optionalHeader.sizeOfStackCommit:X}");
		Console.WriteLine($"SizeOfHeapReserve:       {dosHeader.pe.optionalHeader.sizeOfHeapReserve:X}");
		Console.WriteLine($"SizeOfHeapCommit:        {dosHeader.pe.optionalHeader.sizeOfHeapCommit:X}");

		Console.WriteLine($"LoaderFlags:             0x{dosHeader.pe.optionalHeader.loaderFlags:X}");
		Console.WriteLine($"NumberOfRvaAndSizes:     {dosHeader.pe.optionalHeader.numberOfRvaAndSizes}");
	}

	public static void PrintDataTables(ref PEHeader.DosHeader dosHeader)
	{
		string[] dataTable = { "Export Table", "Import Table",
							   "Resource Table", "Exception Table",
							   "Certificate ", "Base Relocation",
							   "Debug Table", "Architecture",
							   "Global Ptr Table", "TLS Table",
							   "Load Config ", "Bound Import",
							   "Import Address", "Delay Import Desc.",
							   "CLR Runtime Header", "Reserved, must be zero" };

		uint offset, vAddress, sections, tables;
		sections = dosHeader.pe.numberOfSections;

		tables = dosHeader.pe.optionalHeader.numberOfRvaAndSizes;

		Console.WriteLine("\nData Tables: ");
		for (int idx = 0; idx < tables; idx++)
		{
			vAddress = dosHeader.dataDirectory![idx].virtualAddr;

			if (vAddress == 0) continue;

			Console.WriteLine($"  {dataTable[idx]}: ");

			offset = (uint)PEHeader.RvaToOffset(sections, vAddress, dosHeader.section_table!);

			Console.WriteLine($"     Address: 0x{vAddress:X} \tOffset: 0x{offset:X}");
			Console.WriteLine($"        Size: 0x{dosHeader.dataDirectory[idx].size:X} ");
		}
	}

	public static void PrintExports(ref PEHeader.DosHeader dosHeader)
	{
		Console.WriteLine("\nExport Directory ");
		Console.WriteLine($"    Flags:           0x{dosHeader.exportDir.exportFlags:X}");
		Console.WriteLine($"    TimeStamp:       0x{dosHeader.exportDir.timeStamp:X}");
		Console.WriteLine($"    MajorVersion:    0x{dosHeader.exportDir.majorVer:X}");
		Console.WriteLine($"    MinorVersion:    0x{dosHeader.exportDir.minorVer:X}");
		Console.WriteLine($"    Name RVA:        0x{dosHeader.exportDir.nameRVA:X}");
		Console.WriteLine($"    OrdinalBase:     0x{dosHeader.exportDir.ordinalBase:X}");
		Console.WriteLine($"    AddressTable Entries:  0x{dosHeader.exportDir.addrTableEntries:X}");
		Console.WriteLine($"    NumberOfNames:         0x{dosHeader.exportDir.numberOfNamePointers:X}");
		Console.WriteLine($"    ExportTable Entries:   0x{dosHeader.exportDir.exportAddrTableRVA:X}");
		Console.WriteLine($"    AddressOfNames:        0x{dosHeader.exportDir.namePtrRVA:X}");
		Console.WriteLine($"    OrdinalTable RVA:      0x{dosHeader.exportDir.ordinalTableRVA:X}");

		Console.WriteLine("\nExported functions: ");

		if ((dosHeader.pe.characteristics & 0x2000) == 0) return;

		for (int i = 0; i < dosHeader.exportDir.numberOfNamePointers; i++)
		{
			Console.WriteLine($"   {dosHeader.exportDir.exportAddr_name_t![i].names}");
		}
	}

	public static void PrintImports(ref PEHeader.DosHeader dosHeader)
	{
		uint? tableEntries;

		tableEntries = (dosHeader.dataDirectory![1].size / 20) - 1;
		Console.WriteLine("\nImport Directory ");

		for (uint idx = 0; idx < tableEntries; idx++)
		{
			Console.WriteLine($"  Import Lookup table RVA: {dosHeader.importDir![idx].importLookupTableRVA:X}");
			Console.WriteLine($"  Time Stamp:              {dosHeader.importDir[idx].timeStamp:X}");
			Console.WriteLine($"  Forwarder Chain:         {dosHeader.importDir[idx].forwarderChain:X}");
			Console.WriteLine($"  Name RVA:                {dosHeader.importDir[idx].nameRVA:X}");
			Console.WriteLine($"  Import Address table RVA: {dosHeader.importDir[idx].importAddressRVA:X}");
		}
	}
}


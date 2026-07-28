Il2CppProtoDescriptorDumper

Retrieves Protobuf descriptors dumped from IL2CPP binaries by emulating their descriptor initialization codepaths with a custom x64 VM interpreter.

---

Usage

* Build with **Visual Studio 2022 or later**
* Put your target `GameAssembly.dll` into your build output directory
* Create a `Dump` folder and copy paste:

  * `script.json`
  * `Dll` \(there go the DummyDll\) \
    (from [Il2CppInspectorRedux](https://github.com/LukeFZ/Il2CppInspectorRedux) output)
* Run the program
  * You can also add `--output-descriptors` to make the tool spit out raw descriptors
  * And you can also add `--no-dependencies` to make the tool skip dependencies check
  * If everything works, Protobuf descriptors will be recovered automatically

---

Disclaimer

* Tool assumes target using the default Google.Protobuf implementation
* Descriptors **must not be stripped from their string literals**
* Worked on very few binaries - **results may vary** / unreliability may occur

\> Disclaimer: This software is provided for educational use. Author will not be liable for any misuse.

---

How does it work?

1. Tool scans IL2CPP metadata for Protobuf Reflection Types
2. Fetches related descriptor getter methods (`get_Descriptor`)
3. Emulates their x64 initialization code through a custom VM
4. Fetches the reconstructed Base64 descriptor data blob
5. Decodes the data and retrieves the original `.proto` definitions

---

Copyright © Hiro420
using System.Collections;
using System.Runtime.InteropServices;
using LLVMSharp;
using LLVMSharp.Interop;

namespace CILTools;
/// <summary>
/// LLVM IR Reader
/// </summary>
public class IRReader
{
    /// <summary>
    /// Read LLVM IR from a file
    /// </summary>
    /// <param name="path">Path to the file</param>
    /// <returns>LLVM IR</returns>
    public static string Read(string path)
    {
        return File.ReadAllText(path);
    }

    static LLVMModuleRef moduleRef;
    public static unsafe LModule ReadModule(string path, bool forceEntryPoint = false)
    {
        var context = new LLVMContext();
        
        LLVMOpaqueMemoryBuffer* bufferRef = null;
        sbyte* errorMessage = null;

        // Convert the managed string path to a pointer
        var pathPtr = (sbyte*)Marshal.StringToHGlobalAnsi(path);
        
        var result = LLVM.CreateMemoryBufferWithContentsOfFile(pathPtr, &bufferRef, &errorMessage);
        if (result != 0)
        {
            throw new Exception($"Failed to read file: {path}");
        }
        
        var success = context.Handle.TryParseIR(bufferRef, out moduleRef, out string message);
        if (!success)
        {
            throw new Exception($"Failed to parse IR: {message}");
        }

        if (forceEntryPoint)
        {
            // Check if a "main" function exists
            var entryPoint = moduleRef.GetNamedFunction("main");
            if (entryPoint.Handle == IntPtr.Zero)
            {
                // Check if a "_start" function exists
                entryPoint = moduleRef.GetNamedFunction("_start");
                if (entryPoint.Handle == IntPtr.Zero)
                {
                    throw new Exception("No entry point found!");
                }
            }
        }
        
        var mod = new LModule();
        
        mod.ModuleRef = moduleRef;
        mod.Context = context;
        mod.FunctionCount = moduleRef.GetFunctionCount();
        mod.GlobalVariableCount = moduleRef.GetGlobalVariableCount();
        mod.BasicBlockCount = moduleRef.GetBasicBlockCount();
        mod.InstructionCount = moduleRef.GetInstructionCount();
        mod.ExecutionEngine = moduleRef.CreateExecutionEngine();
        mod.Source = Read(path);
        
        return mod;
    }

    public class LModule
    {
        public LLVMModuleRef ModuleRef { get; set; }
        public LLVMContext Context { get; set; }
        public int FunctionCount { get; set; }
        public int GlobalVariableCount { get; set; }
        public int BasicBlockCount { get; set; }
        public int InstructionCount { get; set; }
        public string Source;
        
        public unsafe List<Tuple<string, LLVMValueRef> > FunctionNames
        {
            get
            {
                var names = new List<Tuple<string, LLVMValueRef>>();
                var current = ModuleRef.FirstFunction;
                while (current.Handle != IntPtr.Zero)
                {
                    var name = current.Name;
                    var func = current;
                    names.Add(new Tuple<string, LLVMValueRef>(name, func));
                    current = LLVM.GetNextFunction((LLVMOpaqueValue*)current.Handle);
                }
                
                return names;
            }
        }

        public unsafe List<Tuple<string, LLVMValueRef>> GlobalVariables
        {
            get
            {
                var globals = new List<Tuple<string, LLVMValueRef>>();
                var current = ModuleRef.FirstGlobal;
                while (current.Handle != IntPtr.Zero)
                {
                    var name = current.Name;
                    var global = current;
                    globals.Add(new Tuple<string, LLVMValueRef>(name, global));
                    current = LLVM.GetNextGlobal((LLVMOpaqueValue*)current.Handle);
                }
                
                return globals;
            }
        }

        public unsafe LLVMOpaqueExecutionEngine* ExecutionEngine { get; set; }

        public LLVMValueRef? GetEntryPoint()
        {
            // First try and check if a function named "main" exists
            var main = ModuleRef.GetNamedFunction("main");
            if (main.Handle != IntPtr.Zero)
            {
                return main;
            }
            
            // If not, try and get the _start function
            var start = ModuleRef.GetNamedFunction("_start");
            if (start.Handle != IntPtr.Zero)
            {
                return start;
            }
            
            // If not, there is no entry point
            return null;
        }

        public unsafe LLVMBasicBlockRef[] GetBasicBlocks()
        {
            var blocks = new List<LLVMBasicBlockRef>();
            var current = ModuleRef.FirstFunction;
            while (current.Handle != IntPtr.Zero)
            {
                var bb = current.FirstBasicBlock;
                while (bb.Handle != IntPtr.Zero)
                {
                    blocks.Add(bb);
                    bb = LLVM.GetNextBasicBlock(bb);
                }
                
                current = LLVM.GetNextFunction((LLVMOpaqueValue*)current.Handle);
            }
            
            return blocks.ToArray();
        }
    }
}
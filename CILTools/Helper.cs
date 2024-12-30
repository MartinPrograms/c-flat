using System.CodeDom.Compiler;
using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using System.Text;
using LLVMSharp;
using LLVMSharp.Interop;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CSharp;
using Metadata = LLVMSharp.Metadata;
using Type = System.Type;

namespace CILTools;

public static class Helper
{
    public static unsafe sbyte* ToSbyte(this string str)
    {
        fixed (char* p = str)
        {
            return (sbyte*)p;
        }
    }
    
    public static unsafe string FromSbyte(sbyte* p)
    {
        return new string((char*)p);
    }
    
    public static Process StartIlasm(string args)
    {
        var p = Process.Start("ilasm", args);
        if (p == null)
            throw new Exception("Failed to start ilasm process");
        
        return p;
    }

    public static bool IlasmExists()
    {
        var p = Process.Start("ilasm", "");
        if ( p == null)
            return false;
        
        p.WaitForExit();
        return true;
    }

    public static void Compile(string[] args)
    {
        if (args.Length < 3)
        {
            Console.WriteLine("Usage: CILTools <input> <output> <-exe|-dll>");
            return;
        }

        if (!File.Exists(args[0]))
        {
            Console.WriteLine("File not found!");
            return;
        }

        // Check if the args has a -exe or -dll flag
        if (args.Length == 3 && args[2] != "-exe" && args[2] != "-dll")
        {
            Console.WriteLine("Invalid flag! Use -exe or -dll");
            return;
        }
        
        
        try
        {
            string cilCode = File.ReadAllText(args[0]);

            File.WriteAllText("dump.il", cilCode);

            if (!Helper.IlasmExists()) // Install it https://github.com/kekyo/ILAsm.Managed
            {
                Console.WriteLine("ilasm not found!");
                return;
            }

            Console.WriteLine("ilasm available...");

            // Call ilasm /dll /output=dump.dll dump.il
            using (var p = Helper.StartIlasm($"/{args[2].Substring(1)} /output={args[1]} {Path.GetFullPath("dump.il")}"))
            {
                p.WaitForExit();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"An error occurred during compilation: {ex.Message}");
        }
    }

    public static unsafe void Analyze(string[] toArray)
    {
        if (toArray.Length < 1)
        {
            Console.WriteLine("Usage: CILTools analyze <input>");
            return;
        }

        if (!File.Exists(toArray[0]))
        {
            Console.WriteLine("File not found!");
            return;
        }

        try
        {
            var stuff = IRReader.ReadModule(toArray[0]);
            Console.WriteLine(stuff.FunctionCount + " functions");
            Console.WriteLine(stuff.GlobalVariableCount + " global variables");
            Console.WriteLine(stuff.BasicBlockCount + " basic blocks");
            Console.WriteLine(stuff.InstructionCount + " instructions");
            
            var names = stuff.FunctionNames;
            foreach (var name in names)
            {
                Console.WriteLine("Func: " + name.Item1 + " " + string.Join(", ", name.Item2.Params));
            }
            
            var globals = stuff.GlobalVariables;
            foreach (var global in globals)
            {
                Console.WriteLine("Global: " + global.Item1);
            }
            
            var bb = stuff.GetBasicBlocks();
            foreach (var block in bb)
            {
                Console.WriteLine("Basic block: " + block.PrintToString());
            }
            
            // Try to get the entry point
            var entry = stuff.GetEntryPoint();
            if (entry != null)
            {
                Console.WriteLine("Entry point: " + entry.Value.Name + " " + string.Join(", ", entry.Value.Params));
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"An error occurred during analysis: {ex.Message}");
        }
    }
    
    public static unsafe int GetFunctionCount(this LLVMModuleRef module)
    {
        int funcCount = 0;
        var current = module.FirstFunction;
        while (current.Handle != IntPtr.Zero)
        {
            funcCount++;
            current = LLVM.GetNextFunction((LLVMOpaqueValue*)current.Handle);
        }
        
        return funcCount;
    }
    
    public static unsafe int GetGlobalVariableCount(this LLVMModuleRef module)
    {
        int globalVarCount = 0;
        var current = module.FirstGlobal;
        while (current.Handle != IntPtr.Zero)
        {
            globalVarCount++;
            current = LLVM.GetNextGlobal((LLVMOpaqueValue*)current.Handle);
        }
        
        return globalVarCount;
    }
    
    public static unsafe int GetBasicBlockCount(this LLVMModuleRef module)
    {
        int bbCount = 0;
        var current = module.FirstFunction;
        while (current.Handle != IntPtr.Zero)
        {
            var bb = current.FirstBasicBlock;
            while (bb.Handle != IntPtr.Zero)
            {
                bbCount++;
                bb = bb.Next;
            }
            
            current = LLVM.GetNextFunction((LLVMOpaqueValue*)current.Handle);
        }
        
        return bbCount;
    }
    
    public static unsafe int GetInstructionCount(this LLVMModuleRef module)
    {
        int instrCount = 0;
        var current = module.FirstFunction;
        while (current.Handle != IntPtr.Zero)
        {
            var bb = current.FirstBasicBlock;
            while (bb.Handle != IntPtr.Zero)
            {
                var instr = bb.FirstInstruction;
                while (instr.Handle != IntPtr.Zero)
                {
                    instrCount++;
                    instr = instr.NextInstruction;
                }
                
                bb = bb.Next;
            }
            
            current = LLVM.GetNextFunction((LLVMOpaqueValue*)current.Handle);
        }
        
        return instrCount;
    }

    public static void Transpile(string[] toArray)
    {
        if (toArray.Length < 3)
        {
            Console.WriteLine("Missing arguments, usage: CILTools transpile <input> <classname> <-exe>");
            return;
        }
        var mod = IRReader.ReadModule(toArray[0], toArray[2] == "-exe");
        var transpiler = new Transpiler();
        transpiler.Start(mod, toArray[1], toArray[2] == "-exe");
        transpiler.Transpile();
        
        var source = transpiler.GetSource();
        Console.WriteLine("\nOUTPUT:");
        Console.WriteLine(source);
    }

    public static Type FromLLVM(this LLVMTypeKind kind)
    {
        switch (kind)
        {
            case LLVMTypeKind.LLVMVoidTypeKind:
                return typeof(void);
            case LLVMTypeKind.LLVMHalfTypeKind:
                return typeof(short);
            case LLVMTypeKind.LLVMFloatTypeKind:
                return typeof(float);
            case LLVMTypeKind.LLVMDoubleTypeKind:
                return typeof(double);
            case LLVMTypeKind.LLVMPointerTypeKind:
                return typeof(IntPtr);
            case LLVMTypeKind.LLVMIntegerTypeKind:
                return typeof(int);
            case LLVMTypeKind.LLVMFunctionTypeKind:
                return typeof(void);
        }
        
        return typeof(void);
    }

    public static void EmitConstant(ILGenerator il, object getValue)
    {
        // If int use OpCodes.Ldc_I4
        if (getValue is int)
        {
            il.Emit(OpCodes.Ldc_I4, (int)getValue);
        }
        else if (getValue is float)
        {
            il.Emit(OpCodes.Ldc_R4, (float)getValue);
        }
        else if (getValue is double)
        {
            il.Emit(OpCodes.Ldc_R8, (double)getValue);
        }
        else if (getValue is long)
        {
            il.Emit(OpCodes.Ldc_I8, (long)getValue);
        }
        else if (getValue is short)
        {
            il.Emit(OpCodes.Ldc_I4, (short)getValue);
        }
        else if (getValue is byte)
        {
            il.Emit(OpCodes.Ldc_I4, (byte)getValue);
        }
        else if (getValue is bool)
        {
            il.Emit(OpCodes.Ldc_I4, (bool)getValue ? 1 : 0);
        }
        else if (getValue is char)
        {
            il.Emit(OpCodes.Ldc_I4, (char)getValue);
        }
        else if (getValue is string)
        {
            il.Emit(OpCodes.Ldstr, (string)getValue);
        }
        else if (getValue is IntPtr)
        {
            il.Emit(OpCodes.Ldc_I4, (int)(IntPtr)getValue);
        }
        else if (getValue is null)
        {
            il.Emit(OpCodes.Ldnull);
        }
        else
        {
            throw new ArgumentException("Invalid constant type " + getValue.GetType());
        }
    }

    public static void CompileC(string[] toArray)
    {
        if (toArray.Length < 4)
        {
            Console.WriteLine(
                "Missing arguments, usage: CILTools compilec <input> <output> <classname> <-exe|-dll> <-silent>");
            return;
        }

        var input = toArray[0];
        var output = toArray[1];
        var classname = toArray[2];
        var isExe = toArray[3] == "-exe";
        var isDll = toArray[3] == "-dll";
        var isSilent = toArray.Length == 5 && toArray[4] == "-silent";

        var currentStream = Console.Out;
        if (isSilent)
        {
            Console.SetOut(TextWriter.Null);
        }
        
        if (!isExe && !isDll)
        {
            Console.WriteLine("Invalid flag! Use -exe or -dll");
            return;
        }

        if (!File.Exists(input))
        {
            Console.WriteLine("File not found!");
            return;
        }

        // clang -S -emit-llvm "%INPUT_FILE%" -o "%OUTPUT_FILE%"
        var temp = Path.GetTempFileName(); // Our output file
        using (var p = Process.Start("clang", $"-S -emit-llvm -fno-discard-value-names \"{input}\" -o \"{temp}\"")) // -fno-discard-value-names is important, creates better llvm ir
        {
            p.WaitForExit();
        }

        // Use Transpiler to transpile the llvm ir to CIL
        var mod = IRReader.ReadModule(temp, isExe);
        var transpiler = new Transpiler();
        transpiler.Start(mod, classname, isExe);

        // Transpile the llvm ir
        transpiler.Transpile();

        var source = transpiler.GetSource();

        File.WriteAllText(output, source);
#if DEBUG
        if (!isSilent)
        {
            Console.WriteLine("Source:");
            Console.WriteLine(source);
        }
        
        Console.SetOut(currentStream);
        Console.WriteLine("Done!");
#endif
    }

    static void DirectoryCopy(string sourceDirName, string destDirName, bool copySubDirs)
    {
        // Get the subdirectories for the specified directory.
        DirectoryInfo dir = new DirectoryInfo(sourceDirName);

        if (!dir.Exists)
        {
            throw new DirectoryNotFoundException(
                "Source directory does not exist or could not be found: "
                + sourceDirName);
        }

        DirectoryInfo[] dirs = dir.GetDirectories();
        // If the destination directory doesn't exist, create it.
        if (!Directory.Exists(destDirName))
        {
            Directory.CreateDirectory(destDirName);
        }

        // Get the files in the directory and copy them to the new location.
        FileInfo[] files = dir.GetFiles();
        foreach (FileInfo file in files)
        {
            string temppath = Path.Combine(destDirName, file.Name);
            file.CopyTo(temppath, false);
        }

        // If copying subdirectories, copy them and their contents to new location.
        if (copySubDirs)
        {
            foreach (DirectoryInfo subdir in dirs)
            {
                string temppath = Path.Combine(destDirName, subdir.Name);
                DirectoryCopy(subdir.FullName, temppath, copySubDirs);
            }
        }
    }

    public static unsafe Type GetFunctionReturnType(LLVMValueRef function)
    {
        List<LLVMBasicBlockRef> blocks = new List<LLVMBasicBlockRef>();
        
        var bb = function.FirstBasicBlock;
        while (bb.Handle != IntPtr.Zero)
        {
            blocks.Add(bb);
            bb = bb.Next;
        }
        
        if (blocks.Count == 0)
            return typeof(void);
        
        List<LLVMBasicBlockRef> returnBlocks = new List<LLVMBasicBlockRef>();
        foreach (var block in blocks)
        {
            var instr = block.LastInstruction;
            while (instr.Handle != IntPtr.Zero)
            {
                var opcode = LLVM.GetInstructionOpcode(instr);
                if (opcode == LLVMOpcode.LLVMRet)
                {
                    returnBlocks.Add(block);
                    break;
                }
                
                instr = instr.PreviousInstruction;
            }
        }
        
        if (returnBlocks.Count == 0)
            return typeof(void);
        
        var lastBlock = returnBlocks[0];
        var lastInstr = lastBlock.LastInstruction;
        if (lastInstr.OperandCount == 0)
        {
            // Causes horrible issues if we don't return void
            // The entire IDE crashes lol took like a good hour to figure out
            return typeof(void);
        }
        
        var retValue = lastInstr.GetOperand(0);
        Type retType = GetTypeFromValue(retValue);
        
        return retType;
    }

    public static Type[] GetFunctionParamTypes(LLVMValueRef function)
    {
        var paramCount = function.ParamsCount;
        var types = new Type[paramCount];
        for (int i = 0; i < paramCount; i++)
        {
            types[i] = function.GetParam((uint)i).TypeOf.Kind.FromLLVM();
        }
        
        return types;
    }

    public static void EmitDefault(ILGenerator il, Type returnType)
    {
        if (returnType == typeof(void))
        {
            return;
        }
        
        if (returnType == typeof(int))
        {
            il.Emit(OpCodes.Ldc_I4_0);
        }
        else if (returnType == typeof(float))
        {
            il.Emit(OpCodes.Ldc_R4, 0.0f);
        }
        else if (returnType == typeof(double))
        {
            il.Emit(OpCodes.Ldc_R8, 0.0);
        }
        else if (returnType == typeof(long))
        {
            il.Emit(OpCodes.Ldc_I8, 0L);
        }
        else if (returnType == typeof(short))
        {
            il.Emit(OpCodes.Ldc_I4_0);
        }
        else if (returnType == typeof(byte))
        {
            il.Emit(OpCodes.Ldc_I4_0);
        }
        else if (returnType == typeof(bool))
        {
            il.Emit(OpCodes.Ldc_I4_0);
        }
        else if (returnType == typeof(char))
        {
            il.Emit(OpCodes.Ldc_I4_0);
        }
        else if (returnType == typeof(string))
        {
            il.Emit(OpCodes.Ldstr, "");
        }
        else if (returnType == typeof(IntPtr))
        {
            il.Emit(OpCodes.Ldc_I4_0);
        }
        else
        {
            throw new NotSupportedException($"Type {returnType} is not supported");
        }
    }

    public static unsafe object GetValue(LLVMValueRef value, Type csType)
    {
        if (value.IsConstant == null)
        {
            // Return the default value
            return csType.IsValueType ? Activator.CreateInstance(csType) : null;
        }
        
        if (csType.IsByRef)
        {
            csType = csType.GetElementType();
        }
        
        if (csType.IsPointer)
        {
            csType = csType.GetElementType();
        }
        
        if (csType == typeof(int))
        {
            var v = value.ConstIntSExt;
            return (int)v;
        }
        
        if (csType == typeof(float))
        {
            var v = value.GetConstRealDouble(out bool _);
            return (float)v;
        }
        
        if (csType == typeof(double))
        {
            var v = value.GetConstRealDouble(out bool _);
            return v;
        }
        
        if (csType == typeof(long))
        {
            var v = value.ConstIntSExt;
            return (long)v;
        }
        
        if (csType == typeof(short))
        {
            var v = value.ConstIntSExt;
            return (short)v;
        }
        
        if (csType == typeof(byte))
        {
            var v = value.ConstIntSExt;
            return (byte)v;
        }
        
        if (csType == typeof(bool))
        {
            var v = value.ConstIntSExt;
            return v == 1;
        }
        
        if (csType == typeof(char))
        {
            var v = value.ConstIntSExt;
            return (char)v;
        }
        
        if (csType == typeof(string))
        {
            UIntPtr size;
            var v = LLVM.GetAsString(value.Initializer, & size);
            var str = new string(v, 0, (int)size - 1);
            return str;
        }
        
        if (csType == typeof(IntPtr))
        {
            var v = value.ConstIntSExt;
            return (IntPtr)v;
        }

        if (csType == typeof(object))
        {
            // Get the type of the value
            LLVMTypeRef llvmType = LLVM.TypeOf(value);
            var kind = llvmType.Kind;
            return kind.FromLLVM();
        }

        if (csType == typeof(System.Object[]))
        {
            var arr = new ArrayList();
            

            return arr.ToArray();
        }

        if (csType == typeof(char*))
        {
            UIntPtr size;
            var v = LLVM.GetAsString(value.Initializer, & size);
            return new string(v, 0, (int)size - 1);
        }
        
        throw new NotSupportedException($"Type {csType} is not supported");
    }

    public static void Emit(ILGenerator il, object sourceValue, Type csType)
    {
        if (csType == typeof(int))
        {
            il.Emit(OpCodes.Ldc_I4, (int)sourceValue);
        }
        else if (csType == typeof(float))
        {
            il.Emit(OpCodes.Ldc_R4, (float)sourceValue);
        }
        else if (csType == typeof(double))
        {
            il.Emit(OpCodes.Ldc_R8, (double)sourceValue);
        }
        else if (csType == typeof(long))
        {
            il.Emit(OpCodes.Ldc_I8, (long)sourceValue);
        }
        else if (csType == typeof(short))
        {
            il.Emit(OpCodes.Ldc_I4, (short)sourceValue);
        }
        else if (csType == typeof(byte))
        {
            il.Emit(OpCodes.Ldc_I4, (byte)sourceValue);
        }
        else if (csType == typeof(bool))
        {
            il.Emit((bool)sourceValue ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
        }
        else if (csType == typeof(char))
        {
            il.Emit(OpCodes.Ldc_I4, (char)sourceValue);
        }
        else if (csType == typeof(string))
        {
            il.Emit(OpCodes.Ldstr, (string)sourceValue);
        }
        else if (csType == typeof(IntPtr))
        {
            il.Emit(OpCodes.Ldc_I4, (int)(IntPtr)sourceValue);
        }
        else if (csType == typeof(object[]))
        {
            var arr = (object[])sourceValue;
            
            il.Emit(OpCodes.Ldc_I4, arr.Length);
            il.Emit(OpCodes.Newarr, typeof(object));
            for (int i = 0; i < arr.Length; i++)
            {
                il.Emit(OpCodes.Dup);
                il.Emit(OpCodes.Ldc_I4, i);
                Emit(il, arr[i], arr[i].GetType());
                il.Emit(OpCodes.Stelem_Ref);
            }
        }
        else
        {
            throw new NotSupportedException($"Type {csType} is not supported");
        }
    }

    public static unsafe Type GetTypeFromValue(LLVMValueRef inst)
    {
        
        var type = inst.TypeOf;
        var kind = type.Kind;
        return kind.FromLLVM();
    }

    public static bool IsConstant(LLVMValueRef lhs)
    {
        return lhs.IsAConstant != null;
    }

    public static bool IsSameBasicBlock(LLVMBasicBlockRef currentBlock, LLVMBasicBlockRef incomingBlock)
    {
        return currentBlock.Handle == incomingBlock.Handle;
    }

    public static Type TypeFromString(string s)
    {
        // Because LLVMSharp bindings aren't good we have to do this manually...
        s = s.Trim();
        if (s.StartsWith("%"))
        {
            Console.WriteLine("Struct: " + s);
            return typeof(TypeBuilder);
        }
        
        if (s == "i32")
        {
            return typeof(int);
        }
        
        if (s == "i64")
        {
            return typeof(long);
        }
        
        if (s == "i16")
        {
            return typeof(short);
        }
        
        if (s == "i8")
        {
            return typeof(byte);
        }
        
        if (s == "float")
        {
            return typeof(float);
        }
        
        if (s == "double")
        {
            return typeof(double);
        }
        
        if (s == "void")
        {
            return typeof(void);
        }

        if (s == "ptr")
        {
            return typeof(IntPtr);
        }
        
        return typeof(void);
    }

    public static unsafe bool IsStruct(LLVMValueRef inst, Transpiler transpiler, out TypeBuilder? var)
    {
        var = null;

        if (!TypeFromAlloca(inst, out string name)) return false;
        
        var = transpiler.GetStruct(name);
        if (var == null)
            return false;

        return true;
    }

    public static bool IsStruct(Type t)
    {
        // Check if t is from System
        if (t.Namespace != null && t.Namespace.StartsWith("System"))
            return false;
        return t.BaseType == typeof(ValueType);
    }

    private static bool TypeFromAlloca(LLVMValueRef inst,out string name)
    {
        var op = inst.ToString();
        name = "";
        
        // Discard *everything* before the =
        var equals = op.IndexOf('=');
        if (equals == -1)
            return false;
        
        op = op.Substring(equals + 1);
        op = op.Trim();
        
        // Discard "alloca" and the space
        var alloca = op.IndexOf("alloca");
        if (alloca == -1)
            return false;
        
        op = op.Substring(alloca + 6);
        op = op.Trim();
        
        // Discard everything after the first space
        var space = op.IndexOf(' ');
        if (space == -1)
            return false;
        
        op = op.Substring(0, space);
        op = op.Trim();
        op = op.Replace(",", "");
        
        name = op;
        
        return true;
    }

    public static Type ToPointerType(Type fieldFieldType)
    {
        if (fieldFieldType == typeof(int))
        {
            return typeof(int*);
        }
        
        if (fieldFieldType == typeof(long))
        {
            return typeof(long*);
        }
        
        if (fieldFieldType == typeof(short))
        {
            return typeof(short*);
        }
        
        if (fieldFieldType == typeof(byte))
        {
            return typeof(byte*);
        }
        
        if (fieldFieldType == typeof(float))
        {
            return typeof(float*);
        }
        
        if (fieldFieldType == typeof(double))
        {
            return typeof(double*);
        }
        
        if (fieldFieldType == typeof(char))
        {
            return typeof(char*);
        }
        
        if (fieldFieldType == typeof(IntPtr))
        {
            return typeof(IntPtr*);
        }

        return fieldFieldType;
    }

    public static unsafe void EmitConstantPtr(ILGenerator il, object constValue)
    {
        // Use stind.i4 for int
        if (constValue is int)
        {
            il.Emit(OpCodes.Ldc_I4, (int)constValue);
        }
        else if (constValue is float)
        {
            il.Emit(OpCodes.Ldc_R4, (float)constValue);
        }
        else if (constValue is double)
        {
            il.Emit(OpCodes.Ldc_R8, (double)constValue);
        }
        else if (constValue is long)
        {
            il.Emit(OpCodes.Ldc_I8, (long)constValue);
        }
        else if (constValue is short)
        {
            il.Emit(OpCodes.Ldc_I4, (short)constValue);
        }
        else if (constValue is byte)
        {
            il.Emit(OpCodes.Ldc_I4, (byte)constValue);
        }
        else if (constValue is bool)
        {
            il.Emit(OpCodes.Ldc_I4, (bool)constValue ? 1 : 0);
        }
        else if (constValue is char)
        {
            il.Emit(OpCodes.Ldc_I4, (char)constValue);
        }
        else if (constValue is string)
        {
            il.Emit(OpCodes.Ldstr, (string)constValue);
        }
        else if (constValue is IntPtr)
        {
            il.Emit(OpCodes.Ldc_I4, (int)(IntPtr)constValue);
        }
        else
        {
            throw new ArgumentException("Invalid constant type " + constValue.GetType());
        }
    }

    public static void Stfld(ILGenerator il, object constValue)
    {
        
    }

    public static void InitStruct(TypeBuilder structType, ILGenerator il, LocalBuilder alloca)
    {
        // This goes through all fields, and calls newobj on them
        var fields = structType.GetFields();
        foreach (var field in fields)
        {
            var constructor = field.FieldType.GetConstructor(Type.EmptyTypes);
            if (constructor == null)
                continue;
            il.Emit(OpCodes.Ldloc, alloca);
            il.Emit(OpCodes.Newobj, constructor);
            il.Emit(OpCodes.Stfld, field);
        }
    }
}
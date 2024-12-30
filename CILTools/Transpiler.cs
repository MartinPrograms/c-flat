using LLVMSharp.Interop;

using System.Reflection.Emit;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.CSharp;
using ICSharpCode.Decompiler.Metadata;
using ICSharpCode.Decompiler.TypeSystem;
using LLVMSharp;
using Object = System.Object;
using Type = System.Type;

namespace CILTools;
/// <summary>
/// LLVM IR to CIL Transpiler.
/// </summary>
public class Transpiler
{

    IRReader.LModule module;
    string name;
    
    private bool isExe;
    
    PersistedAssemblyBuilder asmBuilder;
    ModuleBuilder modBuilder;
    TypeBuilder typeBuilder;
    MethodBuilder mainMethod;
    private bool _modifiedMain = false;
    DecompilerSettings settings;
    CSharpDecompiler decompiler;
    ILGenerator ilStrArray;
    
    public void Start(IRReader.LModule mod, string name, bool isExe = false)
    {
        this.name = name;
        this.isExe = isExe;
        module = mod;
        // Use the coreclr assembly 
        PersistedAssemblyBuilder m = new PersistedAssemblyBuilder(new AssemblyName(name), typeof(Object).Assembly);
        
        asmBuilder = m;
        modBuilder = asmBuilder.DefineDynamicModule(name);
        typeBuilder = modBuilder.DefineType(name);
        
        ilStrArray = typeBuilder.DefineTypeInitializer().GetILGenerator(); // Get the constructor
        
        // Create a main function, make it do nothing
        if (isExe)
            mainMethod = typeBuilder.DefineMethod("Main", MethodAttributes.Public | MethodAttributes.Static, typeof(void), new Type[] { typeof(string[]) });
        
        // Inject the standard library
        StandardLibrary.InjectSTD(typeBuilder, ilStrArray);
    }

    public void Transpile()
    {
        TranspileStructs();
        TranspileGlobals();
        TranspileFunctions();
        TranspileBasicBlocks();
        
        if (isExe)
        {
            // We need to add a return statement to the main method
            var il = mainMethod.GetILGenerator();
            // call the c main function
            if (!Methods.ContainsKey("main"))
                throw new Exception("No main function found");
            il.Emit(OpCodes.Call, Methods["main"]);
            il.Emit(OpCodes.Ret);
            _modifiedMain = true;
        }
    }
    private Dictionary<string, TypeBuilder> structMap = new();

    public TypeBuilder? GetStruct(string name)
    {
        if (!structMap.ContainsKey(name))
        {
            // Try to remove the %struct., if it exists
            if (name.StartsWith("%struct."))
            {
                name = name.Substring(8);
                if (structMap.ContainsKey(name))
                    return structMap[name];
            }
            
            return null;
        }
        
        return structMap[name];
    }
    
    private unsafe void TranspileStructs()
    {
        // a struct in LLVM IR is declared as follows:
        // %struct.thing = type { i32, i32 }
        // This is a struct with two i32 fields, we will represent this as a class in CIL, and the fields will be the fields of the class

        // As you can see by the %, it is a variable, so we will need to get all the variables in the module
        //var variables = module.GetVariables();

        // But because the LLVMSharp bindings dont support getting structs, well have to use strings :(
        var source = module.Source;
        var lines = source.Split('\n');
        
        // Find every line beginning with %struct and including the text " type {" to get the struct 
        string[] structLines = lines.Where(l => l.StartsWith("%struct") && l.Contains("type {")).ToArray();
        
        List<string> structNames = new();
        List<string[]> structFields = new();
        
        foreach (var line in structLines)
        {
            // Get the name of the struct
            var name = line.Split(" ")[0];
            structNames.Add(name);
            
            // Get the fields of the struct
            var fields = line.Split("{")[1].Split("}")[0].Split(",");
            structFields.Add(fields);
        }
        
        // First pass for struct names, and standard fields
        List<Tuple<string, Type, TypeBuilder, string, ILGenerator>> tracker = new();
        List<Tuple<TypeBuilder, ILGenerator>> structBuilders = new();
        int j = 0;
        for (int i = 0; i < structNames.Count; i++)
        {
            var fields = structFields[i].Select(Helper.TypeFromString).ToList();
            Console.WriteLine("Creating struct: " + structNames[i] + " with fields: " + string.Join(", ", fields.Select(f => f.Name)));
            var structType = typeBuilder.DefineNestedType(structNames[i], TypeAttributes.Public | TypeAttributes.SequentialLayout | TypeAttributes.AnsiClass | TypeAttributes.Sealed, typeof(ValueType));
            structMap.Add(structNames[i], structType);
            
            var il = structType.DefineTypeInitializer().GetILGenerator();
            
            foreach (var field in fields)
            {
                tracker.Add(new Tuple<string, Type, TypeBuilder, string,  ILGenerator>("_" + j.ToString(),field, structType, structFields[i][fields.IndexOf(field)], il));
                j++;
            }
            
            structBuilders.Add(new Tuple<TypeBuilder, ILGenerator>(structType, il));
        }
        
        foreach ((var name, var field, var structType, var otherName, var il) in tracker)
        {
            var structName = otherName.Trim();

            if (field == typeof(TypeBuilder))
            {
                if (structMap.ContainsKey(structName))
                {
                    // Get the struct
                    var structTypeField = structMap[structName];
                    var afield = structType.DefineField(name, structTypeField, FieldAttributes.Public);
                }
            }
            else
            {
                structType.DefineField(name, field, FieldAttributes.Public);
            }
            
            Console.WriteLine($"Added field {name} to struct {structType.Name}");
        }


        // Now we need to add the struct to the type builder
        foreach (var structType in structBuilders)
        {
            structType.Item2.Emit(OpCodes.Ret);
            structType.Item1.CreateType();
        }
    }

    private Stack<Tuple<string, LLVMValueRef,MethodBuilder, LLVMBasicBlockRef[]> > functionStack = new ();
    public Dictionary<string,MethodInfo> Methods = new ();

    public MethodInfo GetMethod(string name)
    {
        // Check if the standard library has the function
        if (StandardLibrary.HasFunction(name))
        {
            return StandardLibrary.GetMethod(name);
        }
        
        return Methods[name];
    }
    
    private unsafe void TranspileBasicBlocks()
    {
        while (functionStack.TryPop(out var tuple))
        {
            var (name, function, method, blocks) = tuple;
            var instructions = new List<LLVMValueRef>();
            foreach (var block in blocks)
            {
                var current = block.FirstInstruction;
                while (current.Handle != IntPtr.Zero)
                {
                    instructions.Add(current);
                    current = current.NextInstruction;
                }
            }
            
            var il = method.GetILGenerator();
            
            // Get the type of the function
            var returnType = method.ReturnType;
            var paramTypes = method.GetParameters();

            if (returnType != typeof(void) &&
                instructions.Count == 1) // This is a simple return statement, a constant as a function
            {
                ReturnStatic(instructions, returnType, il);
            }
            else
            {
                // Here we need to convert the LLVM instructions to CIL
                // This is the hard part, there is a new class called Converter that will do this
                Console.WriteLine("Transpiling function: " + name);
                Converter.ConvertInstructions(instructions, function, returnType, paramTypes, this, il, name);
            }
            il.Emit(OpCodes.Ret);
        }
    }

    private static unsafe void ReturnStatic(List<LLVMValueRef> instructions, Type returnType, ILGenerator il)
    {
        var operand = instructions[0].GetOperand(0);

        var value = Helper.GetValue(operand, returnType);
        Helper.EmitConstant(il, value);
    }

    private unsafe void TranspileFunctions()
    {
        var functions = module.FunctionNames;

        foreach (var functionTuple in functions)
        {
            var name = functionTuple.Item1;
            
            if (StandardLibrary.HasFunction(name))
            {
                continue;
            }
            
            var function = functionTuple.Item2; // Includes metadata, does not include the actual code.
            Console.WriteLine("Transpiling function: " + name);
            if (function.IsAFunction != null)
            {
                var blocks = function.BasicBlocks;
                Type returnType = Helper.GetFunctionReturnType(function);
                Type[] paramTypes = Helper.GetFunctionParamTypes(function);
                var method = typeBuilder.DefineMethod(name, MethodAttributes.Public | MethodAttributes.Static, returnType, paramTypes);
                Methods.Add(name, method);
                functionStack.Push(new Tuple<string, LLVMValueRef, MethodBuilder, LLVMBasicBlockRef[]>(name, function, method, blocks));
            }
        }
    }
    
    Stack<string> stringStack = new Stack<string>();
    
    private unsafe void TranspileGlobals()
    {
        var globalVars = module.GlobalVariables;
        foreach (var globalVar in globalVars)
        {
            var name = globalVar.Item1;
            var value = globalVar.Item2;

            LLVMOpaqueValue* val = (LLVMOpaqueValue*)value.Handle;
            
            var type = value.Initializer.Kind;
            Console.WriteLine($"{name}'s type: " + type);
            switch (type)
            {
                case LLVMValueKind.LLVMConstantIntValueKind:
                    var valInt = value.Initializer.ConstIntSExt;
                    FieldHelper.CreateField(typeBuilder, ilStrArray, name, valInt);
                    break;
                case LLVMValueKind.LLVMConstantFPValueKind:
                    var valFloat = LLVM.ConstRealGetDouble(val, null);
                    FieldHelper.CreateField(typeBuilder, ilStrArray, name, valFloat);
                    break;
                case LLVMValueKind.LLVMConstantPointerNullValueKind:
                    var valPtr = LLVM.GetPointerAddressSpace(LLVM.TypeOf(val));
                    FieldHelper.CreateField(typeBuilder, ilStrArray, name, valPtr);
                    break;
                case LLVMValueKind.LLVMConstantDataArrayValueKind:
                    if (name.StartsWith("??_C@_"))
                    {
                        // This is a string
                        UIntPtr size;
                        sbyte* valStr = LLVM.GetAsString(value.Initializer, &size);
                        string str = new string(valStr, 0, (int)size - 1); // Ignore the \0 at the end
                        stringStack.Push(str);
                    }
                    else
                    {
                        // This is an array, we need to get the type of the array
                        var arrayType = value.Initializer.TypeOf.ElementType;
                        var csType = arrayType.Kind.FromLLVM();
                        
                        var arraySize = value.Initializer.TypeOf.ArrayLength;

                        // Create an array with type csType and size arraySize
                        var array = Array.CreateInstance(csType, arraySize);
                        
                        for (int i = 0; i < arraySize; i++)
                        {
                            var arrayValue = value.Initializer.GetElementAsConstant((uint)i);
                            switch (arrayValue.Kind)
                            {
                                case LLVMValueKind.LLVMConstantIntValueKind:
                                    array.SetValue((int)arrayValue.ConstIntSExt, i);
                                    break;
                                case LLVMValueKind.LLVMConstantFPValueKind:
                                    array.SetValue(LLVM.ConstRealGetDouble(val, null), i);
                                    break;
                                case LLVMValueKind.LLVMConstantPointerNullValueKind:
                                    array.SetValue(LLVM.GetPointerAddressSpace(LLVM.TypeOf(val)), i);
                                    break;
                                case LLVMValueKind.LLVMGlobalVariableValueKind:
                                default:
                                    Console.WriteLine("Unknown type");
                                    break;
                            }
                        }

                        // Because arrays can not be constants in CIL, we will store the array in a field
                        var fieldArray = typeBuilder.DefineField(name, array.GetType(), FieldAttributes.Public | FieldAttributes.Static);
                        var il = ilStrArray;
                        
                        il.Emit(OpCodes.Ldc_I4, arraySize);
                        il.Emit(OpCodes.Newarr, csType);
                        
                        for (int i = 0; i < arraySize; i++)
                        {
                            il.Emit(OpCodes.Dup);
                            il.Emit(OpCodes.Ldc_I4, i);
                            Helper.EmitConstant(il, array.GetValue(i));
                            il.Emit(OpCodes.Stelem, csType);
                        }
                        
                        il.Emit(OpCodes.Stsfld, fieldArray);
                    }

                    break;
                case LLVMValueKind.LLVMConstantArrayValueKind:
                    // This is most likely a string array
                    // We will treat it as a string array
                    // TODO: Handle other types
                    var arraySizeStr = value.Initializer.TypeOf.ArrayLength;
                    var arrayStr = new string[arraySizeStr];
                    
                    // Pop the most recent strings from the stack
                    for (int i = 0; i < arraySizeStr; i++)
                    {
                        arrayStr[i] = stringStack.Pop();
                    }
                    // Reverse the array
                    Array.Reverse(arrayStr);
                    
                    // Create a field for the array
                    var fieldStrArray = typeBuilder.DefineField(name, arrayStr.GetType(), FieldAttributes.Public | FieldAttributes.Static);
                    
                    ilStrArray.Emit(OpCodes.Ldc_I4, arraySizeStr);
                    ilStrArray.Emit(OpCodes.Newarr, typeof(string));
                    
                    for (int i = 0; i < arraySizeStr; i++)
                    {
                        ilStrArray.Emit(OpCodes.Dup);
                        ilStrArray.Emit(OpCodes.Ldc_I4, i);
                        ilStrArray.Emit(OpCodes.Ldstr, arrayStr[i]);
                        ilStrArray.Emit(OpCodes.Stelem, typeof(string));
                    }
                    
                    ilStrArray.Emit(OpCodes.Stsfld, fieldStrArray);
                    
                    break;
                case LLVMValueKind.LLVMGlobalVariableValueKind:
                    // Most likely a string, but could be a pointer. However we will treat it as a string
                    // TODO: Handle pointers
                    // Get the string from the stack
                    var strf = stringStack.Pop();
                    FieldHelper.CreateField(typeBuilder, ilStrArray, name, strf);
                    break;
                
                default:
                    break;
            }
        }
    }

    public string? GetSource()
    {
        ilStrArray.Emit(OpCodes.Ret);
        
        typeBuilder.CreateType();
        var filename = Path.GetTempFileName();
        
        asmBuilder.Save(filename);
        
        var source = DecompileAssembly(filename);
        // Prepend using System; to the source
        
        // Prepend // Transpiled from LLVM IR \nDon't modify this without understanding the implications
        
        source = "// Transpiled from LLVM IR\n// Don't modify this without understanding the implications\n// It is not unreadable on purpose, it is unreadable because LLVM IR is\n\n" + source;
        
        
        return source;
    }
    
    public string DecompileAssembly(string filename, string outputFilePath = null)
    {
        var settings = new DecompilerSettings()
        {
            AutoLoadAssemblyReferences = true,
            ThrowOnAssemblyResolveErrors = false,
        };

        var resolver = new UniversalAssemblyResolver(typeof(object).Assembly.Location, true, null);
        var decompiler = new CSharpDecompiler(filename, resolver, settings);

        // Decompile the entire module
        var syntaxTree = decompiler.DecompileType(new FullTypeName(name));
    
        // Save to a file if specified
        if (!string.IsNullOrEmpty(outputFilePath))
        {
            File.WriteAllText(outputFilePath, syntaxTree.ToString());
        }
    
        // Return as string
        return syntaxTree.ToString();
    }
}
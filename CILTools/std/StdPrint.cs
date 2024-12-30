using System.Reflection;
using System.Reflection.Emit;

namespace CILTools.std;

public class StdPrintN : IInject
{
    // In C void print(const char* str, int value);
    public string Name { get; }

    public MethodInfo Method { get; private set; }
    
    public StdPrintN()
    {
        Name = "printn";
    }
    
    public void Inject(TypeBuilder typeBuilder, ILGenerator il)
    {        
        var method = typeBuilder.DefineMethod("print", MethodAttributes.Public | MethodAttributes.Static, typeof(void), new Type[] { typeof(string), typeof(int) });
        Method = method;
        var gen = method.GetILGenerator();
        gen.Emit(OpCodes.Ldarg_0);
        gen.Emit(OpCodes.Call, typeof(System.Console).GetMethod("Write", new Type[] { typeof(string) }));
        gen.Emit(OpCodes.Ldarg_1);
        gen.Emit(OpCodes.Call, typeof(System.Console).GetMethod("Write", new Type[] { typeof(int) }));
        gen.Emit(OpCodes.Ret);
    }
}

public class StdPrint : IInject
{
    // In C void print(const char* str);
    public string Name { get; }

    public MethodInfo Method { get; private set; }
    
    public StdPrint()
    {
        Name = "print";
    }
    
    public void Inject(TypeBuilder typeBuilder, ILGenerator il)
    {        
        var method = typeBuilder.DefineMethod("print", MethodAttributes.Public | MethodAttributes.Static, typeof(void), new Type[] { typeof(string) });
        Method = method;
        var gen = method.GetILGenerator();
        gen.Emit(OpCodes.Ldarg_0);
        gen.Emit(OpCodes.Call, typeof(System.Console).GetMethod("Write", new Type[] { typeof(string) }));
        gen.Emit(OpCodes.Ret);
    }
}

// Print a string and a string
public class StdPrintS : IInject
{
    // In C void print(const char* str, const char* str);
    public string Name { get; }

    public MethodInfo Method { get; private set; }
    
    public StdPrintS()
    {
        Name = "prints";
    }
    
    public void Inject(TypeBuilder typeBuilder, ILGenerator il)
    {        
        var method = typeBuilder.DefineMethod("print", MethodAttributes.Public | MethodAttributes.Static, typeof(void), new Type[] { typeof(string), typeof(char*) });
        Method = method;
        var gen = method.GetILGenerator();
        
        // Now print the string
        gen.Emit(OpCodes.Ldarg_0);
        gen.Emit(OpCodes.Call, typeof(System.Console).GetMethod("Write", new Type[] { typeof(string) }));
        
        gen.Emit(OpCodes.Ldarg_1);
        gen.Emit(OpCodes.Call, typeof(System.Runtime.InteropServices.Marshal).GetMethod("PtrToStringAnsi", new Type[] { typeof(IntPtr) }));
        
        gen.Emit(OpCodes.Call, typeof(System.Console).GetMethod("Write", new Type[] { typeof(string) }));
        
        gen.Emit(OpCodes.Ret);
    }
}

// in c: println
public class StdPrintln : IInject
{
    // In C void print(const char* str);
    public string Name { get; }

    public MethodInfo Method { get; private set; }
    
    public StdPrintln()
    {
        Name = "println";
    }
    
    public void Inject(TypeBuilder typeBuilder, ILGenerator il)
    {        
        var method = typeBuilder.DefineMethod("println", MethodAttributes.Public | MethodAttributes.Static, typeof(void), new Type[] { typeof(string) });
        Method = method;
        var gen = method.GetILGenerator();
        gen.Emit(OpCodes.Ldarg_0);
        gen.Emit(OpCodes.Call, typeof(System.Console).GetMethod("WriteLine", new Type[] { typeof(string) }));
        gen.Emit(OpCodes.Ret);
    }
}

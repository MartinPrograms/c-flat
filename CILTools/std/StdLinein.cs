using System.Reflection;
using System.Reflection.Emit;

namespace CILTools.std;

// In C char* linein();
public class StdLinein : IInject
{
    public string Name { get; }

    public MethodInfo Method { get; private set; }
    
    public StdLinein()
    {
        Name = "linein";
    }
    
    public void Inject(TypeBuilder typeBuilder, ILGenerator il)
    {        
        var method = typeBuilder.DefineMethod("linein", MethodAttributes.Public | MethodAttributes.Static, typeof(char*), new Type[] { });
        method.SetReturnType(typeof(char*));
        Method = method;
        var gen = method.GetILGenerator();
        var readLine = typeof(System.Console).GetMethod("ReadLine", Type.EmptyTypes);
        // Use Marshal.StringToHGlobalAnsi to convert the string to a char*
        gen.Emit(OpCodes.Call, readLine);
        gen.Emit(OpCodes.Call, typeof(System.Runtime.InteropServices.Marshal).GetMethod("StringToHGlobalAnsi"));
        gen.Emit(OpCodes.Ret);
    }
}

// in C: char* getchar();
public class StdGetchar : IInject
{
    public string Name { get; }

    public MethodInfo Method { get; private set; }

    public StdGetchar()
    {
        Name = "getchar";
    }

    public void Inject(TypeBuilder typeBuilder, ILGenerator il)
    {
        // Define the getchar method with public static visibility
        var method = typeBuilder.DefineMethod("getchar", MethodAttributes.Public | MethodAttributes.Static, typeof(char*), new Type[] { });
        method.SetReturnType(typeof(char*));
        Method = method;
        var gen = method.GetILGenerator();
        var readkey = typeof(System.Console).GetMethod("ReadKey", Type.EmptyTypes);
        var keyCharProperty = typeof(System.ConsoleKeyInfo).GetProperty("KeyChar").GetGetMethod();

        // Create an output variable for the key char
        var keyChar = gen.DeclareLocal(typeof(ConsoleKeyInfo));
        gen.Emit(OpCodes.Call, readkey);
        gen.Emit(OpCodes.Stloc, keyChar);
        gen.Emit(OpCodes.Ldloca, keyChar);
        gen.Emit(OpCodes.Call, keyCharProperty);
        gen.Emit(OpCodes.Call, typeof(char).GetMethod("ToString", new Type[] { typeof(char) }));
        // Use Marshal.StringToHGlobalAnsi to convert the string to a char*
        gen.Emit(OpCodes.Call, typeof(System.Runtime.InteropServices.Marshal).GetMethod("StringToHGlobalAnsi"));

        gen.Emit(OpCodes.Ret);
    }
}
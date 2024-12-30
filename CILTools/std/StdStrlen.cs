using System.Reflection;
using System.Reflection.Emit;

namespace CILTools.std;

public class StdStrlen : IInject
{
    // strlen (const char* str)
    public string Name { get; }
    public MethodInfo Method { get; private set; }

    public StdStrlen()
    {
        Name = "strlen";
    }
    
    public void Inject(TypeBuilder typeBuilder, ILGenerator il)
    {
        var method = typeBuilder.DefineMethod("strlen", MethodAttributes.Public | MethodAttributes.Static, typeof(int), new Type[] { typeof(char*) });
        Method = method;
        var gen = method.GetILGenerator();
        gen.Emit(OpCodes.Ldarg_0);
        // Convert the pointer to a string
        gen.Emit(OpCodes.Call, typeof(System.Runtime.InteropServices.Marshal).GetMethod("PtrToStringAnsi", new Type[] { typeof(IntPtr) }));
        // Get the length of the string
        gen.Emit(OpCodes.Call, typeof(System.String).GetMethod("get_Length"));
        gen.Emit(OpCodes.Ret);
    }
}
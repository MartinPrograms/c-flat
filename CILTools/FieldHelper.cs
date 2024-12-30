using System.Reflection;
using System.Reflection.Emit;

namespace CILTools;

public class FieldHelper
{
    public static void CreateField(TypeBuilder typeBuilder, ILGenerator il, string name, object value)
    {
        // We need to create a static field for the value
        var field = typeBuilder.DefineField(name, value.GetType(), FieldAttributes.Static | FieldAttributes.Public);
        
        // Now we need to emit the value
        if (value.GetType() == typeof(int))
        {
            il.Emit(OpCodes.Ldc_I4, (int)value);
        }
        else if (value.GetType() == typeof(string))
        {
            il.Emit(OpCodes.Ldstr, (string)value);
        }
        else if (value.GetType() == typeof(bool))
        {
            il.Emit((bool)value ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
        }
        else if (value.GetType() == typeof(char))
        {
            il.Emit(OpCodes.Ldc_I4, (char)value);
        }
        else if (value.GetType() == typeof(byte))
        {
            il.Emit(OpCodes.Ldc_I4, (byte)value);
        }
        else if (value.GetType() == typeof(long))
        {
            il.Emit(OpCodes.Ldc_I8, (long)value);
        }
        else if (value.GetType() == typeof(double))
        {
            il.Emit(OpCodes.Ldc_R8, (double)value);
        }
        else if (value.GetType() == typeof(short))
        {
            il.Emit(OpCodes.Ldc_I4, (short)value);
        }
        else if (value.GetType() == typeof(ushort))
        {
            il.Emit(OpCodes.Ldc_I4, (ushort)value);
        }
        else if (value.GetType() == typeof(float))
        {
            il.Emit(OpCodes.Ldc_R4, (float)value);
        }
        else
        {
            throw new NotSupportedException($"Type {value.GetType()} is not supported");
        }
        il.Emit(OpCodes.Stsfld, field);
    }
}
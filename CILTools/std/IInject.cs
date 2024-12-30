using System.Reflection;
using System.Reflection.Emit;

namespace CILTools.std;

public interface IInject
{
    string Name { get; }
    public MethodInfo Method { get; }
    void Inject(TypeBuilder typeBuilder, ILGenerator il);
}
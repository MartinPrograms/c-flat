using System.Reflection;
using System.Reflection.Emit;
using CILTools.std;

namespace CILTools;

public class StandardLibrary
{
    private static bool _initialized = false;
    private static List<IInject> _injected = new List<IInject>();
    public static void InjectSTD(TypeBuilder typeBuilder, ILGenerator il)
    {
        // Get all types inheriting from IInject
        var types = Assembly.GetExecutingAssembly().GetTypes().Where(t => t.GetInterfaces().Contains(typeof(IInject)));
        
        // Create an instance of each type and call Inject
        foreach (var type in types)
        {
            var instance = (IInject)Activator.CreateInstance(type);
            instance.Inject(typeBuilder, il);
            _injected.Add(instance);
            
            Console.WriteLine($"Injected {instance.Name}");
        }
        
        
    }

    public static bool HasFunction(string name)
    {
        return _injected.Any(i => i.Name == name);
    }

    public static MethodInfo GetMethod(string name)
    {
        return _injected.First(i => i.Name == name).Method;
    }
}
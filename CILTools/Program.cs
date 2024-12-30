using System.Diagnostics;
using System.Reflection;
using CILTools;

namespace CILTools;

class Program
{
    public static void Main(string[] args)
    {
        
#if DEBUG
        if (args.Length == 0) // most likely running from the IDE
            args = new string[] { "compilec", "C:\\Users\\marti\\CLionProjects\\CIR\\main.c","test","ClassName", "-exe", };
#endif
        
        try
        {
            Console.WriteLine("CILTools v1.0 by Martin.");
            Console.WriteLine(string.Join(" ", args));
            if (args.Length == 0)
            {
                Console.WriteLine("Usage: CILTools <operation> <args>");
                Console.WriteLine("Example: CILTools compile main.il main.exe -exe, compiles CIL to an executable");
                Console.WriteLine("Example: CILTools analyze main.ll, analyzes LLVM IR");
                Console.WriteLine("Example: CILTools transpile main.ll main, transpiles LLVM IR to CIL (the big one)");
                Console.WriteLine(
                    "Example: CILTools compilec main.c main.cs ClassName <-exe|-dll>, compiles C code into LLVM IR, transpiles to CIL, and outputs C# code.");
                return;
            }

            if (args[0] == "compile")
            {
                Helper.Compile(args.Skip(1).ToArray());
                return;
            }

            if (args[0] == "analyze")
            {
                Helper.Analyze(args.Skip(1).ToArray());
                return;
            }

            if (args[0] == "transpile")
            {
                Helper.Transpile(args.Skip(1).ToArray());
                return;
            }

            if (args[0] == "compilec")
            {
                // Takes in a c file, uses clang to make llvm ir, then uses Helper.Transpile to transpile the llvm ir to an exe or dll
                Helper.CompileC(args.Skip(1).ToArray());
                return;
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(e.Message + "\n" + e.StackTrace);
        }
    }
}
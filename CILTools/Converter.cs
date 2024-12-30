using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using LLVMSharp.Interop;
using Exception = System.Exception;

namespace CILTools;

public class Converter
{
    private static Dictionary<string, LocalBuilder> allocaMap = new Dictionary<string, LocalBuilder>();
    private static Dictionary<string, Type> typeMap = new();
    private static Dictionary<string, Type> structMap = new();
    private static Stack<Tuple<object, Type, LocalBuilder>> stack = new();
    
    private static Dictionary<string, Label> labelQueue = new();
    private static Tuple<string, Label> currentLabel;
    
    private static Dictionary<string, LLVMValueRef> phiMap = new();
    
    private static LLVMValueRef currentInst;
    private static Transpiler transpiler;
    private static string currentMethodName;
    private static Type returnType;
    public static void ConvertInstructions(List<LLVMValueRef> instructions, LLVMValueRef function, Type returnType, ParameterInfo[] paramsType,
        Transpiler transpiler,ILGenerator il, string currentMethodName)
    {
        allocaMap.Clear();
        stack.Clear();
        labelQueue.Clear();
        names.Clear();
        phiMap.Clear();
        typeMap.Clear();
        structMap.Clear();
        
        currentInst = function;
        currentLabel = new Tuple<string, Label>("entry", il.DefineLabel());

        il.MarkLabel(currentLabel.Item2); // Start at the first label
    
        Converter.returnType = returnType;
        Converter.transpiler = transpiler;
        Converter.currentMethodName = currentMethodName;
        
        CreateInitialLocals(paramsType, il); // This initializes the parameters as locals
        CreateVariables(instructions, il); // This creates the variables (a,b and result in the LLVM example)
        CreateLabels(instructions, il); // This creates the labels
        CreateInstructions(instructions, il); // This creates the instructions (laod, store, add, ret)
    }

    private static unsafe void CreateLabels(List<LLVMValueRef> instructions, ILGenerator il)
    {
        foreach (var inst in instructions)
        {
            var opcode = LLVM.GetInstructionOpcode(inst);

            if (opcode == LLVMOpcode.LLVMBr)
            {
                var count = LLVM.GetNumOperands(inst); // If 1 it's a jump, if 3 it's a conditional branch
                if (count == 1)
                {
                    // Get the label name
                    var target = LLVM.GetOperand(inst, 0);
                    var targetLabel = il.DefineLabel();
                    var name = LLVM.GetValueName(target);
                    var strName = new string(name);
                    // Check if it already exists
                    if (!labelQueue.ContainsKey(strName))
                    {
                        labelQueue[strName] = targetLabel;
                        Console.WriteLine($"Created label {strName}");
                    }
                }
                else
                {
                    // Get the label name   
                    var trueTarget = LLVM.GetOperand(inst, 2);
                    var falseTarget = LLVM.GetOperand(inst, 1);
                    var trueLabel = il.DefineLabel();
                    var falseLabel = il.DefineLabel();
                    
                    var trueName = LLVM.GetValueName(trueTarget);
                    var falseName = LLVM.GetValueName(falseTarget);
                    
                    var strTrueName = new string(trueName);
                    var strFalseName = new string(falseName);
                    
                    // Check if true label already exists
                    if (!labelQueue.ContainsKey(strTrueName))
                    {
                        labelQueue[strTrueName] = trueLabel;
                        Console.WriteLine($"Created label {new string(trueName)}");
                    }
                    
                    // Check if false label already exists
                    if (!labelQueue.ContainsKey(strFalseName))
                    {
                        labelQueue[strFalseName] = falseLabel;
                        Console.WriteLine($"Created label {new string(falseName)}");
                    }
                }
            }

            if (opcode == LLVMOpcode.LLVMPHI)
            {
                // Phi node, we prepare the labels
                var count = LLVM.CountIncoming(inst);
                for (uint i = 0; i < count; i++)
                {
                    LLVMBasicBlockRef label = LLVM.GetIncomingBlock(inst, i);
                    LLVMValueRef value = LLVM.GetIncomingValue(inst, i);
                    var labelName = LLVM.GetValueName(label);
                    var strLabelName = new string(labelName);
                    if (!labelQueue.ContainsKey(strLabelName))
                    {
                        labelQueue[strLabelName] = il.DefineLabel();
                        Console.WriteLine($"Created phi label {strLabelName}");
                    }
                    
                    phiMap[strLabelName] = value;
                    Console.WriteLine($"Mapped phi node {GetName(inst)} to label {strLabelName}");
                    
                    // Create a local, that tracks if the phi node has been visited, call this the tracker
                    var tracker = il.DeclareLocal(typeof(bool));
                    il.Emit(OpCodes.Ldc_I4_0);
                    il.Emit(OpCodes.Stloc, tracker);
                    allocaMap[strLabelName + "_tracker"] = tracker;
                    Console.WriteLine($"Created tracker for phi node {strLabelName}");
                    
                    // It will be used later in the phi node operation
                }
                
                // And create the variable output for the phi node itself
                var phiName = LLVM.GetValueName(inst);
                var strPhiName = new string(phiName);
                allocaMap[strPhiName] = il.DeclareLocal(Helper.GetTypeFromValue(inst));
                
                Console.WriteLine($"Created phi variable {strPhiName}");
                
            }
        }
        
        Console.WriteLine("Created labels");
    }

    private static unsafe void CreateInstructions(List<LLVMValueRef> instructions, ILGenerator il)
    {
        foreach (var inst in instructions)
        {
            try
            {
                Operation(il, inst);
            }
            catch(Exception e)
            {
                Console.WriteLine(e + "\nat instruction " + inst);
                throw e;
            }
        }
    }

    private static unsafe void Operation(ILGenerator il, LLVMValueRef inst, bool mark = true)
    {
        var opcode = LLVM.GetInstructionOpcode(inst);
        
        if (mark)
            MarkCurrentLabel(inst, il);

        if (opcode == LLVMOpcode.LLVMStore)
        {
            OpStore(il, inst);
        }

        if (opcode == LLVMOpcode.LLVMLoad)
        {
            OpLoad(il, inst);
        }

        if (opcode == LLVMOpcode.LLVMAdd)
        {
            OpAdd(il, inst);
        }

        if (opcode == LLVMOpcode.LLVMFAdd)
        {
            // Floating point add
            OpAdd(il, inst);
        }

        if (opcode == LLVMOpcode.LLVMSub)
        {
            // Subtraction
            OpSub(il, inst);
        }

        if (opcode == LLVMOpcode.LLVMMul)
        {
            // Multiplication
            OpMul(il, inst);
        }

        if (opcode == LLVMOpcode.LLVMSDiv)
        {
            // Signed division
            OpDiv(il, inst);
        }

        if (opcode == LLVMOpcode.LLVMSRem)
        {
            // Signed remainder
            OpRem(il, inst);
        }

        if (opcode == LLVMOpcode.LLVMUDiv)
        {
            // Unsigned division
            OpUDiv(il, inst);
        }

        if (opcode == LLVMOpcode.LLVMURem)
        {
            // Unsigned remainder
            OpURem(il, inst);
        }

        if (opcode == LLVMOpcode.LLVMAnd)
        {
            // Bitwise AND
            OpAnd(il, inst);
        }

        if (opcode == LLVMOpcode.LLVMOr)
        {
            // Bitwise OR
            OpOr(il, inst);
        }

        if (opcode == LLVMOpcode.LLVMXor)
        {
            // Bitwise XOR
            OpXor(il, inst);
        }

        if (opcode == LLVMOpcode.LLVMShl)
        {
            // Left shift
            OpLsh(il, inst);
        }

        if (opcode == LLVMOpcode.LLVMICmp)
        {
            // Integer comparison (eq, ne, lt, gt, le, ge)
            OpICmp(il, inst);
        }

        if (opcode == LLVMOpcode.LLVMZExt)
        {
            // Zero extend
            OpZExt(il, inst);
        }

        if (opcode == LLVMOpcode.LLVMAShr)
        {
            // Arithmetic shift right
            OpRsh(il, inst);
        }

        if (opcode == LLVMOpcode.LLVMCall)
        {
            // Call a function
            OpCall(il, inst);
        }
        
        if (opcode == LLVMOpcode.LLVMRet)
        {
            OpRet(il, inst);
        }

        if (opcode == LLVMOpcode.LLVMBr)
        {
            // Branch
            OpBr(il, inst);
        }

        if (opcode == LLVMOpcode.LLVMPHI)
        {
            // Phi node
            OpPhi(il, inst);
        }

        if (opcode == LLVMOpcode.LLVMICmp)
        {
            // Integer comparison
            OpICmp(il, inst);
        }

        if (opcode == LLVMOpcode.LLVMGetElementPtr)
        {
            OpGetElementPtr(il, inst);
        }
    }

    private static void OpGetElementPtr(ILGenerator il, LLVMValueRef inst)
    {
        var type = LLVMTypeKind.LLVMPointerTypeKind;
        var ptr = inst.GetOperand(0);
        var index = inst.GetOperand(2);
        var ptrName = GetName(ptr);

        int offset = 0;
        if (index.IsConstant != null)
        {
            offset = (int)index.ConstIntSExt;
        }
        else
        {
            throw new Exception("GetElementPtr with non-constant index!");
        }

        if (!allocaMap.TryGetValue(ptrName, out var value))
        {
            throw new Exception("GetElementPtr with unmapped alloca!");
        }

        var valueLocal = allocaMap[ptrName]; // TypeBuilder 
        var fields = new FieldInfo[0];

        if (structMap.TryGetValue(ptrName, out var nonPointerType))
        {
            fields = nonPointerType.GetFields();
        }
        else
        {
            fields = valueLocal.LocalType.GetFields();
        }

        // Get field at offset, we can do this by calling

        // Get fields ending in _offset
        var field = fields.FirstOrDefault(f => f.Name == ($"_{offset}"));
        if (field == null)
        {
            throw new Exception("Field not found!");
        }
        
        if (Helper.IsStruct(field.FieldType))
        {
            // If it's a struct, we need to load the address of the struct
            il.Emit(OpCodes.Ldloca, valueLocal);
            il.Emit(OpCodes.Ldflda, field);
        }
        else
        {
            // If it's not a struct, we can load the value directly
            il.Emit(OpCodes.Ldloc, valueLocal);
            il.Emit(OpCodes.Ldfld, field);
        }
        
        // Store the result in a local
        var resultLocal = il.DeclareLocal(field.FieldType);
            
        il.Emit(OpCodes.Stloc, resultLocal);
        allocaMap[GetName(inst)] = resultLocal;
        
    }

    private static unsafe void OpPhi(ILGenerator il, LLVMValueRef inst)
    {
        Console.WriteLine($"Phi node {GetName(inst)}");
        
        var count = LLVM.CountIncoming(inst);
        
        List<Tuple<LLVMBasicBlockRef, LLVMValueRef, LocalBuilder>> incoming = new();
        for (uint i = 0; i < count; i++)
        {
            LLVMBasicBlockRef label = LLVM.GetIncomingBlock(inst, i);
            LLVMValueRef value = LLVM.GetIncomingValue(inst, i);
            var labelName = LLVM.GetValueName(label);
            var strLabelName = new string(labelName);
            var tracker = allocaMap[strLabelName + "_tracker"];
            var valueName = GetName(value);
            var strValueName = new string(valueName);
            
            incoming.Add(new Tuple<LLVMBasicBlockRef, LLVMValueRef, LocalBuilder>(label, value, tracker));
        }
        
        var phiName = LLVM.GetValueName(inst);
        var strPhiName = new string(phiName);
        var resultLocal = allocaMap[strPhiName];
        
        // Check if the phi node has been visited
        foreach (var tuple in incoming)
        {
            var label = tuple.Item1;
            var value = tuple.Item2;
            var tracker = tuple.Item3;
            var labelName = LLVM.GetValueName(label);
            var strLabelName = new string(labelName);
            var valueName = GetName(value);
            var strValueName = new string(valueName);
            
            // Based on the tracker, set the variable resultLocal to the value from the label
            il.Emit(OpCodes.Ldloc, tracker);
            var end = il.DefineLabel();
            
            il.Emit(OpCodes.Brfalse, end);
            il.Emit(OpCodes.Ldloc, resultLocal);
            il.Emit(OpCodes.Ldloc, allocaMap[strValueName]);
            il.Emit(OpCodes.Stloc, resultLocal);
            il.MarkLabel(end);
            
            Console.WriteLine($"Phi node {strPhiName} visited {strLabelName} and stored {strValueName}");
        }
    }

    private static unsafe LLVMBasicBlockRef GetCurrentBasicBlock()
    {
        return LLVM.GetInstructionParent(currentInst);
    }

    private static unsafe void MarkCurrentLabel(LLVMValueRef inst, ILGenerator il)
    {
        var parent = (inst.InstructionParent);
        var name = LLVM.GetValueName(parent);
        var strName = new string(name);
        
        if (labelQueue.TryGetValue(strName, out var label))
        {
            if (currentLabel.Item2 != label)
            {
                Console.WriteLine($"Marking label {strName}");
                il.MarkLabel(label);
                currentLabel = new Tuple<string, Label>(strName, label);
                
                // Check if the label is a phi node
                if (phiMap.TryGetValue(strName, out var phi))
                {
                    // If so, we need to insert il that marks the phi node as visited
                    var tracker = allocaMap[strName + "_tracker"];
                    il.Emit(OpCodes.Ldc_I4_1);
                    il.Emit(OpCodes.Stloc, tracker);
                    Console.WriteLine($"Marked phi node {strName} as visited");
                }
            }
        }
    }

    private static unsafe void OpBr(ILGenerator il, LLVMValueRef inst)
    {
        // Get the condition for the branch
        LLVMValueRef condition = LLVM.GetOperand(inst, 0);
        var count = LLVM.GetNumOperands(inst); // If 1 it's a jump, if 3 it's a conditional branch
        
        if (count == 1)
        {
            // Unconditional branch
            var target = LLVM.GetOperand(inst, 0);
            var targetName = LLVM.GetValueName(target);
            var strTargetName = new string(targetName);
            var targetLabel = labelQueue[strTargetName];
            il.Emit(OpCodes.Br, targetLabel);
            
            Console.WriteLine($"Unconditional branch to {strTargetName}");
        }
        else
        {
            // Conditional branch
            var trueTarget = LLVM.GetOperand(inst, 2);
            var falseTarget = LLVM.GetOperand(inst, 1);
            var trueName = LLVM.GetValueName(trueTarget);
            var falseName = LLVM.GetValueName(falseTarget);
            var strTrueName = new string(trueName);
            var strFalseName = new string(falseName);
            var trueLabel = labelQueue[strTrueName];
            var falseLabel = labelQueue[strFalseName];
            
            Console.WriteLine("Conditional branch(es) with name(s) " + strTrueName + " and " + strFalseName);
            Console.WriteLine("Condition is " + GetName(condition));
            
            if (phiMap.TryGetValue(strTrueName, out var truePhi))
            {
                Console.WriteLine($"True branch is a phi node {strTrueName}");
            }
            
            if (phiMap.TryGetValue(strFalseName, out var falsePhi))
            {
                Console.WriteLine($"False branch is a phi node {strFalseName}");
            }
            
            // Load the condition
            if (allocaMap.TryGetValue(GetName(condition), out var conditionLocal))
            {
                // Check if its a pointer, because if so we need to dereference it
                if (conditionLocal.LocalType.IsPointer)
                {
                    il.Emit(OpCodes.Ldloc, conditionLocal);
                    il.Emit(OpCodes.Ldind_I);
                }
                else
                {
                    il.Emit(OpCodes.Ldloc, conditionLocal);
                }
            }
            else if (Helper.IsConstant(condition))
            {
                var constValue = Helper.GetValue(condition, Helper.GetTypeFromValue(condition));
                Helper.EmitConstant(il, constValue);
            }
            else
            {
                throw new Exception("Branch with unmapped alloca!");
            }

            // Branch if true
            il.Emit(OpCodes.Brtrue, trueLabel);
            // Branch if false
            il.Emit(OpCodes.Br, falseLabel);
        }
    }

    private static unsafe void OpCall(ILGenerator il, LLVMValueRef inst)
    {
        LLVMValueRef target = LLVM.GetCalledValue(inst);

        var funcName = target.Name;
        MethodInfo
            m = transpiler.GetMethod(funcName); 

        var returnType = m.ReturnType;
        
        typeMap[GetName(inst)] = returnType;
        var paramTypes = m.GetParameters();

        var paramCount = paramTypes.Length;

        for (int i = 0; i < paramCount; i++)
        {
            var paramType = paramTypes[i].ParameterType;
            var param = inst.GetOperand((uint)i);

            if (allocaMap.TryGetValue(GetName(param), out var sourceLocal))
            {
                il.Emit(OpCodes.Ldloc, sourceLocal);
            }
            else if (Helper.IsConstant(param))
            {
                // If it's a constant, load it onto the stack directly
                var constValue = Helper.GetValue(param, paramType);
                Helper.Emit(il, constValue, constValue.GetType());
            }
            else
            {
                MapParameter(param, il, paramType);
            }
        }

        il.Emit(OpCodes.Call, m);

        if (returnType != typeof(void))
        {
            LocalBuilder resultLocal = il.DeclareLocal(returnType); // Declare a local to store the result
            il.Emit(OpCodes.Stloc, resultLocal); // Store the result in the local
            stack.Push(new Tuple<object, Type, LocalBuilder>(null, returnType,
                resultLocal)); // Push the result onto the stack
            allocaMap[GetName(inst)] = resultLocal; // Map the instruction to the result local
            
            Console.WriteLine($"Called function {funcName} and stored result in local {resultLocal?.LocalIndex}");
        }

    }

    private static void MapParameter(LLVMValueRef llvmValueRef, ILGenerator il, Type paramType)
    {
        if (allocaMap.TryGetValue(GetName(llvmValueRef), out var sourceLocal))
        {
            il.Emit(OpCodes.Ldloc, sourceLocal);
        }
        else if (Helper.IsConstant(llvmValueRef))
        {
            var constValue = Helper.GetValue(llvmValueRef, paramType);
            Helper.Emit(il, constValue, constValue.GetType());
        }
        else
        {
            throw new Exception("Parameter not found!");
        }
    }

    private static void OpZExt(ILGenerator il, LLVMValueRef inst)
    {
        var value = inst.GetOperand(0); // The value to zero extend
        var type = Helper.GetTypeFromValue(value); // The type of the value
var name = GetName(value);
        if (allocaMap.TryGetValue(name, out var sourceLocal))
        {
            // Create a new local for the result
            LocalBuilder resultLocal = il.DeclareLocal(type);
            il.Emit(OpCodes.Ldloc, sourceLocal); // Load the variable
            il.Emit(OpCodes.Conv_U); // Convert to unsigned
            il.Emit(OpCodes.Stloc, resultLocal); // Store the result
            stack.Push(new Tuple<object, Type, LocalBuilder>(null, type, resultLocal));
            Console.WriteLine($"Zero extended and stored result in local {resultLocal.LocalIndex}");
            allocaMap[GetName(inst)] = resultLocal;
        }
        else
        {
            throw new Exception("ZExt from unmapped alloca!");
        }
    }

    private static void OpUDiv(ILGenerator il, LLVMValueRef inst)
    {
        var lhs = inst.GetOperand(0); // First operand
        var rhs = inst.GetOperand(1); // Second operand
        var type = Helper.GetTypeFromValue(lhs);

        // Load the first operand
        GetOperand(il, lhs, type);

        // Load the second operand
        GetOperand(il, rhs, type);

        // Perform the division
        il.Emit(OpCodes.Div_Un);

        // Declare a result local and store the result
        LocalBuilder resultLocal = il.DeclareLocal(type);
        il.Emit(OpCodes.Stloc, resultLocal);
        stack.Push(new Tuple<object, Type, LocalBuilder>(null, type, resultLocal));
        allocaMap[GetName(inst)] = resultLocal;
        Console.WriteLine($"Unsigned divided and stored result in local {resultLocal.LocalIndex}");
    }

    private static void OpURem(ILGenerator il, LLVMValueRef inst)
    {
        var lhs = inst.GetOperand(0); // First operand
        var rhs = inst.GetOperand(1); // Second operand
        var type = Helper.GetTypeFromValue(lhs);

        // Load the first operand
        GetOperand(il, lhs, type);

        // Load the second operand
        GetOperand(il, rhs, type);

        // Perform the remainder operation
        il.Emit(OpCodes.Rem_Un);

        // Declare a result local and store the result
        LocalBuilder resultLocal = il.DeclareLocal(type);
        il.Emit(OpCodes.Stloc, resultLocal);
        stack.Push(new Tuple<object, Type, LocalBuilder>(null, type, resultLocal));
        allocaMap[GetName(inst)] = resultLocal;
        Console.WriteLine($"Unsigned remainder and stored result in local {resultLocal.LocalIndex}");
    }

    private static void OpAnd(ILGenerator il, LLVMValueRef inst)
    {
        var lhs = inst.GetOperand(0); // First operand
        var rhs = inst.GetOperand(1); // Second operand
        var type = Helper.GetTypeFromValue(lhs);
        
        // Load the first operand
        GetOperand(il, lhs, type);
        
        // Load the second operand
        GetOperand(il, rhs, type);
        
        // Perform the AND operation
        il.Emit(OpCodes.And);
        
        // Declare a result local and store the result
        LocalBuilder resultLocal = il.DeclareLocal(type);
        il.Emit(OpCodes.Stloc, resultLocal);
        stack.Push(new Tuple<object, Type, LocalBuilder>(null, type, resultLocal));
        allocaMap[GetName(inst)] = resultLocal;
        Console.WriteLine($"ANDed and stored result in local {resultLocal.LocalIndex}");
    }

    private static void OpOr(ILGenerator il, LLVMValueRef inst)
    {
        var lhs = inst.GetOperand(0); // First operand
        var rhs = inst.GetOperand(1); // Second operand
        var type = Helper.GetTypeFromValue(lhs);
        
        // Load the first operand
        GetOperand(il, lhs, type);
        
        // Load the second operand
        GetOperand(il, rhs, type);
        
        // Perform the OR operation
        il.Emit(OpCodes.Or);
        
        // Declare a result local and store the result
        LocalBuilder resultLocal = il.DeclareLocal(type);
        il.Emit(OpCodes.Stloc, resultLocal);
        stack.Push(new Tuple<object, Type, LocalBuilder>(null, type, resultLocal));
        allocaMap[GetName(inst)] = resultLocal;
        Console.WriteLine($"ORed and stored result in local {resultLocal.LocalIndex}");
    }

    private static void OpXor(ILGenerator il, LLVMValueRef inst)
    {
        var lhs = inst.GetOperand(0); // First operand
        var rhs = inst.GetOperand(1); // Second operand
        var type = Helper.GetTypeFromValue(lhs);
        
        // Load the first operand
        GetOperand(il, lhs, type);
        
        // Load the second operand
        GetOperand(il, rhs, type);
        
        // Perform the XOR operation
        il.Emit(OpCodes.Xor);
        
        // Declare a result local and store the result
        LocalBuilder resultLocal = il.DeclareLocal(type);
        il.Emit(OpCodes.Stloc, resultLocal);
        stack.Push(new Tuple<object, Type, LocalBuilder>(null, type, resultLocal));
        allocaMap[GetName(inst)] = resultLocal;
        Console.WriteLine($"XORed and stored result in local {resultLocal.LocalIndex}");
    }

    private static void OpLsh(ILGenerator il, LLVMValueRef inst)
    {
        var lhs = inst.GetOperand(0); // First operand
        var rhs = inst.GetOperand(1); // Second operand
        var type = Helper.GetTypeFromValue(lhs);
        
        // Load the first operand
        GetOperand(il, lhs, type);
        
        // Load the second operand
        GetOperand(il, rhs, type);
        
        // Perform the left shift
        il.Emit(OpCodes.Shl);

        // Declare a result local and store the result
        LocalBuilder resultLocal = il.DeclareLocal(type);
        il.Emit(OpCodes.Stloc, resultLocal);
        stack.Push(new Tuple<object, Type, LocalBuilder>(null, type, resultLocal));
        allocaMap[GetName(inst)] = resultLocal;
        Console.WriteLine($"Left shifted and stored result in local {resultLocal.LocalIndex}");
    }

    private static void OpRsh(ILGenerator il, LLVMValueRef inst)
    {
        var lhs = inst.GetOperand(0); // First operand
        var rhs = inst.GetOperand(1); // Second operand
        var type = Helper.GetTypeFromValue(lhs);
        
        // Load the first operand
        GetOperand(il, lhs, type);
        
        // Load the second operand
        GetOperand(il, rhs, type);
        
        // Perform the right shift
        il.Emit(OpCodes.Shr);
        
        // Declare a result local and store the result
        LocalBuilder resultLocal = il.DeclareLocal(type);
        il.Emit(OpCodes.Stloc, resultLocal);
        stack.Push(new Tuple<object, Type, LocalBuilder>(null, type, resultLocal));
        allocaMap[GetName(inst)] = resultLocal;
        Console.WriteLine($"Right shifted and stored result in local {resultLocal.LocalIndex}");
    }

    private static unsafe void OpICmp(ILGenerator il, LLVMValueRef inst)
    {
        var lhs = inst.GetOperand(0); // First operand
        var rhs = inst.GetOperand(1); // Second operand
        var type = Helper.GetTypeFromValue(lhs);

        // Load the first operand
        GetOperand(il, lhs, type);

        // Load the second operand
        GetOperand(il, rhs, type);

        var predicate = LLVM.GetICmpPredicate(inst);
        
        switch (predicate)
        {
            case LLVMIntPredicate.LLVMIntEQ:
                il.Emit(OpCodes.Ceq);
                break;
            case LLVMIntPredicate.LLVMIntNE:
                il.Emit(OpCodes.Ceq);
                il.Emit(OpCodes.Ldc_I4_0);
                il.Emit(OpCodes.Ceq);
                break;
            case LLVMIntPredicate.LLVMIntUGT:
                il.Emit(OpCodes.Cgt_Un);
                break;
            case LLVMIntPredicate.LLVMIntUGE:
                il.Emit(OpCodes.Clt_Un);
                il.Emit(OpCodes.Ldc_I4_0);
                il.Emit(OpCodes.Ceq);
                break;
            case LLVMIntPredicate.LLVMIntULT:
                il.Emit(OpCodes.Clt_Un);
                break;
            case LLVMIntPredicate.LLVMIntULE:
                il.Emit(OpCodes.Cgt_Un);
                il.Emit(OpCodes.Ldc_I4_0);
                il.Emit(OpCodes.Ceq);
                break;
            case LLVMIntPredicate.LLVMIntSGT:
                il.Emit(OpCodes.Cgt);
                break;
            case LLVMIntPredicate.LLVMIntSGE:
                il.Emit(OpCodes.Clt);
                il.Emit(OpCodes.Ldc_I4_0);
                il.Emit(OpCodes.Ceq);
                break;
            case LLVMIntPredicate.LLVMIntSLT:
                il.Emit(OpCodes.Clt);
                break;
            case LLVMIntPredicate.LLVMIntSLE:
                il.Emit(OpCodes.Cgt);
                il.Emit(OpCodes.Ldc_I4_0);
                il.Emit(OpCodes.Ceq);
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
        
        // Declare a result local and store the result
        LocalBuilder resultLocal = il.DeclareLocal(type);
        il.Emit(OpCodes.Stloc, resultLocal);
        stack.Push(new Tuple<object, Type, LocalBuilder>(null, type, resultLocal));
        allocaMap[GetName(inst)] = resultLocal;
        Console.WriteLine($"Compared and stored result in local {resultLocal.LocalIndex}");
    }

    private static void OpMul(ILGenerator il, LLVMValueRef inst)
    {
        var lhs = inst.GetOperand(0); // First operand
        var rhs = inst.GetOperand(1); // Second operand
        var type = Helper.GetTypeFromValue(lhs);

        // Load the first operand
        GetOperand(il, lhs, type);

        // Load the second operand
        GetOperand(il, rhs, type);

        // Perform the multiplication
        il.Emit(OpCodes.Mul);

        // Declare a result local and store the result
        LocalBuilder resultLocal = il.DeclareLocal(type);
        il.Emit(OpCodes.Stloc, resultLocal);
        stack.Push(new Tuple<object, Type, LocalBuilder>(null, type, resultLocal));
        allocaMap[GetName(inst)] = resultLocal;
        Console.WriteLine($"Multiplied and stored result in local {resultLocal.LocalIndex}");
    }

    private static void GetOperand(ILGenerator il, LLVMValueRef lhs, Type type)
    {
        if (allocaMap.TryGetValue(GetName(lhs), out var lhsLocal))
        {
            il.Emit(OpCodes.Ldloc, lhsLocal);
        }
        else if (Helper.IsConstant(lhs))
        {
            var constValue = Helper.GetValue(lhs, type);
            Helper.EmitConstant(il, constValue);
        }
        else
        {
            throw new Exception("Operand not found!");
        }
    }


    private static void OpRem(ILGenerator il, LLVMValueRef inst)
    {
        var lhs = inst.GetOperand(0); // First operand
        var rhs = inst.GetOperand(1); // Second operand
        var type = Helper.GetTypeFromValue(lhs);

        // Load the first operand
        GetOperand(il, lhs, type);

        // Load the second operand
        GetOperand(il, rhs, type);

        // Perform the remainder operation
        il.Emit(OpCodes.Rem);

        // Declare a result local and store the result
        LocalBuilder resultLocal = il.DeclareLocal(type);
        il.Emit(OpCodes.Stloc, resultLocal);
        stack.Push(new Tuple<object, Type, LocalBuilder>(null, type, resultLocal));
        allocaMap[GetName(inst)] = resultLocal;
        Console.WriteLine($"Remainder and stored result in local {resultLocal.LocalIndex}");
    }

    private static void OpDiv(ILGenerator il, LLVMValueRef inst)
    {
        var lhs = inst.GetOperand(0); // First operand
        var rhs = inst.GetOperand(1); // Second operand
        var type = Helper.GetTypeFromValue(lhs);

        // Load the first operand
        GetOperand(il, lhs, type);

        // Load the second operand
        GetOperand(il, rhs, type);

        // Perform the division
        il.Emit(OpCodes.Div);

        // Declare a result local and store the result
        LocalBuilder resultLocal = il.DeclareLocal(type);
        il.Emit(OpCodes.Stloc, resultLocal);
        stack.Push(new Tuple<object, Type, LocalBuilder>(null, type, resultLocal));
        allocaMap[GetName(inst)] = resultLocal;
        Console.WriteLine($"Divided and stored result in local {resultLocal.LocalIndex}");
    }

    private static void OpSub(ILGenerator il, LLVMValueRef inst)
    {
        var lhs = inst.GetOperand(0); // First operand
        var rhs = inst.GetOperand(1); // Second operand
        var type = Helper.GetTypeFromValue(lhs);
        
        // Load the first operand
        GetOperand(il, lhs, type);
        
        // Load the second operand
        GetOperand(il, rhs, type);
        
        // Perform the subtraction
        il.Emit(OpCodes.Sub);
        
        // Declare a result local and store the result
        LocalBuilder resultLocal = il.DeclareLocal(type);
        il.Emit(OpCodes.Stloc, resultLocal);
        stack.Push(new Tuple<object, Type, LocalBuilder>(null, type, resultLocal));
        allocaMap[GetName(inst)] = resultLocal;
        Console.WriteLine($"Subtracted and stored result in local {resultLocal.LocalIndex}");
    }

    private static void OpStore(ILGenerator il, LLVMValueRef inst)
    {
        var value = inst.GetOperand(0);
        var operand = inst.GetOperand(1); // Pointer where the value is stored
        var name = GetName(operand);
        
        //var targetType = Helper.GetTypeFromValue(value);
        var targetType = Helper.GetTypeFromValue(value);
        bool isTargetTypePointer = targetType.IsPointer || targetType.IsByRef || targetType == typeof(IntPtr);

        if (structMap.TryGetValue(name, out var structType))
        {
            targetType = structType;
            
        }
        
        if (allocaMap.TryGetValue(name, out var targetLocal))
        {
            if (targetLocal == null)
            {
                throw new Exception("Store to null alloca!");
            }

            if (allocaMap.TryGetValue(GetName(value), out var sourceLocal))
            {
                if (isTargetTypePointer)
                {
                    // If the target is a pointer, we need to store the address of the value
                    il.Emit(OpCodes.Ldloc, sourceLocal);
                    il.Emit(OpCodes.Stloc, targetLocal);
                }
                else
                {
                    // If the target is not a pointer, we can store the value directly
                    il.Emit(OpCodes.Ldloc, sourceLocal);
                    il.Emit(OpCodes.Stloc, targetLocal);
                }
                
                Console.WriteLine($"Stored value in local {targetLocal.LocalIndex}");
            }
            else if (Helper.IsConstant(value))
            {
                var constValue = Helper.GetValue(value, targetType);
                Helper.EmitConstant(il, constValue);
                il.Emit(OpCodes.Stloc, targetLocal);
                Console.WriteLine($"Stored constant value in local {targetLocal.LocalIndex}");
            }
            else
            {
                throw new Exception("Store from unmapped alloca!");
            }
            
            Console.WriteLine($"Stored value in local {targetLocal.LocalIndex}");
        }
        else
        {
            if (name == "retval")
            {
                // Useless stupid unused variable that always shows up, never gets assigned to or returned
                return;
            }
            
            throw new Exception("Store to unmapped alloca!");
        }
    }

    private static void OpLoad(ILGenerator il, LLVMValueRef inst)
    {
        var ptr = inst.GetOperand(0); // The pointer to load from
        var name = GetName(ptr);
        
        if (allocaMap.TryGetValue(name, out var sourceLocal))
        {
            // Create a new local for the result
            LocalBuilder resultLocal = il.DeclareLocal(sourceLocal.LocalType);
            il.Emit(OpCodes.Ldloc, sourceLocal); // Load the variable
            il.Emit(OpCodes.Stloc, resultLocal); // Store the result
            stack.Push(new Tuple<object, Type, LocalBuilder>(null, sourceLocal.LocalType, resultLocal));
            Console.WriteLine($"Loaded and stored result in local {resultLocal.LocalIndex} with name {GetName(inst)}");
            allocaMap[GetName(inst)] = resultLocal;
        }
        else
        {
            throw new Exception("Load from unmapped alloca!");
        }
    }

    private static void OpRet(ILGenerator il, LLVMValueRef inst)
    {
        var value = inst.GetOperand(0); // The value to return
        
        // LLVMSharp is a slight bit unstable, so i check the return type of the function declared elsewhere
        if (returnType == typeof(void))
        {
            il.Emit(OpCodes.Ret);
            return;
        }
        
        var type = Helper.GetTypeFromValue(value); // The type of the value
        
        if (Helper.IsConstant(value))
        {
            var constValue = Helper.GetValue(value, type); // Get the constant value
            Helper.EmitConstant(il, constValue); // Push the constant onto the stack
        }
        else
        {
            var valueLocal = allocaMap[GetName(value)]; // Get the local for the value
            il.Emit(OpCodes.Ldloc, valueLocal); // Load the variable
        }

        il.Emit(OpCodes.Ret);
    }


    private static void OpAdd(ILGenerator il, LLVMValueRef inst)
    {
        var lhs = inst.GetOperand(0); // First operand
        var rhs = inst.GetOperand(1); // Second operand
        var type = Helper.GetTypeFromValue(lhs);

        // Load the first operand
        GetOperand(il, lhs, type);

        // Load the second operand
        GetOperand(il, rhs, type);

        // Perform the addition
        il.Emit(OpCodes.Add);

        // Declare a result local and store the result
        LocalBuilder resultLocal = il.DeclareLocal(type);
        il.Emit(OpCodes.Stloc, resultLocal);
        stack.Push(new Tuple<object, Type, LocalBuilder>(null, type, resultLocal));
        allocaMap[GetName(inst)] = resultLocal;
        Console.WriteLine($"Added and stored result in local {resultLocal.LocalIndex}");
    }

    private static unsafe void CreateVariables(List<LLVMValueRef> instructions, ILGenerator il)
    {
        foreach (var inst in instructions)
        {
            var opcode = LLVM.GetInstructionOpcode(inst);

            if (opcode == LLVMOpcode.LLVMAlloca)
            {
                var name = GetName(inst);
                if (allocaMap.ContainsKey(name))
                {
                    throw new Exception("Alloca already exists!");
                }

                if (name == "retval")
                {
                    // Useless stupid unused variable that always shows up, never gets assigned to or returned
                    continue;
                }

                if (Helper.IsStruct(inst, transpiler, out var structType))
                {
                    allocaMap[name] = il.DeclareLocal(structType);
                    Console.WriteLine($"Allocating struct alloca for {inst} with name {GetName(inst)} and type {structType}");
                    structMap[name] = structType;
                }
                else
                {
                    allocaMap[name] = il.DeclareLocal(Helper.GetTypeFromValue(inst));
                    Console.WriteLine($"Allocating alloca for {inst} with name {GetName(inst)}");
                }
            }
        }
    }

    private static Dictionary<LLVMValueRef, string> names = new Dictionary<LLVMValueRef, string>();
    public static unsafe string GetName(LLVMValueRef inst)
    {
        var name = new string(LLVM.GetValueName(inst));
        if (String.IsNullOrEmpty(name))
        {
            if (names.ContainsKey(inst))
            {
                return names[inst];
            }
            name = $"v{names.Count}";
            names[inst] = name;
        }
        return name;
    }
    
    private static void CreateInitialLocals(ParameterInfo[] paramsType, ILGenerator il)
    {
        for (int i = 0; i < currentInst.ParamsCount;i++)
        {
            var paramType = paramsType[i].ParameterType;
            var paramLocal = il.DeclareLocal(paramType);
            il.Emit(OpCodes.Ldarg, i);
            il.Emit(OpCodes.Stloc, paramLocal);
            allocaMap[GetName(currentInst.GetParam((uint)i))] = paramLocal;
            Console.WriteLine($"Allocating parameter {i} of type {paramType}");
        }
    }
}
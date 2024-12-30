# CILTools

An LLVM IR to CIL compiler! With a few built in commands.  
 - ciltools compilec Input.c OutputFile.cs className -exe
 - ciltools analyze llvm-ir.ll
 - ciltools transpile llvm-ir.ll className
 - ciltools compile cil.il exe.exe -exe

CompileC takes in a c file, an output file, a class name, and a flag -exe or -dll, -exe forces an entry point to exist in the program.   
Rest is outdated.

This program expects LLVM, Clang, dotnet and ilasm to be in your path. https://releases.llvm.org/download.html

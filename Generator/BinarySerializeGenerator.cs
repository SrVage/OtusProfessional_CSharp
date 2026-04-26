using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Generator;

[Generator]
public class BinarySerializeGenerator : ISourceGenerator
{
    public void Initialize(GeneratorInitializationContext context)
    {
        context.RegisterForSyntaxNotifications(() => new BinarySerializeGeneratorSyntaxReceiver());
    }

    public void Execute(GeneratorExecutionContext context)
    {
        var receiver = context.SyntaxReceiver as BinarySerializeGeneratorSyntaxReceiver;
        if (receiver == null)
            return;

        var compilation = context.Compilation;
        foreach (var classDecl in receiver.CandidateClasses)
        {
            var semanticModel = compilation.GetSemanticModel(classDecl.SyntaxTree);
            var declaredSymbol = semanticModel.GetDeclaredSymbol(classDecl) as INamedTypeSymbol;
            if (declaredSymbol is null)
                continue;

            var hasBinarySerializationAttribute = declaredSymbol.GetAttributes()
                .Any(attr => attr.AttributeClass?.Name == "GenerateBinarySerializerAttribute");

            if (!hasBinarySerializationAttribute)
                continue;

            var nameSpace = declaredSymbol.ContainingNamespace.IsGlobalNamespace
                ? string.Empty
                : declaredSymbol.ContainingNamespace.ToString();
            
            var source = GenerateSerializerCode(declaredSymbol, nameSpace);
            context.AddSource($"{declaredSymbol.Name}_BinarySerializer.g.cs", source);
        }
    }

    private static string GenerateSerializerCode(INamedTypeSymbol classSymbol, string nameSpace)
    {
        var className = classSymbol.Name;
        
        StringBuilder sb = new StringBuilder();
        sb.Append("using System.Text;\n");
        sb.Append($"namespace {nameSpace} {{\n");
        sb.Append($"public partial class {className} {{\n");
        sb.Append("public byte[] SerializeToBinary() {\n");
        sb.Append("int totalSize = 0;\n");
        int totalCountBytes = 0; //Размер всех полей примитивных типов

        foreach (var member in classSymbol.GetMembers().OfType<IPropertySymbol>())
        {
            var propertyType = member.Type;
            var propertyName = member.Name;
            
            if (propertyType.SpecialType == SpecialType.System_String)
            {
                //Вычисляем размер строки в байтах
                sb.Append($"int {propertyName}Length = Encoding.UTF8.GetByteCount({propertyName} ?? string.Empty);\n");
                sb.Append($"totalSize += (4 + {propertyName}Length);\n");
            }
            else
            {
                totalCountBytes += GetTypeByteCount(propertyType.SpecialType);
            }
        }
        sb.Append($"int totalBytesCount = {totalCountBytes};\n");
        sb.Append("totalBytesCount += totalSize;\n"); //Суммируем размеры
        
        sb.Append($"byte[] array = new byte[totalBytesCount];\n");
        sb.Append("int offset = 0;\n");
        sb.Append("Span<byte> span = new Span<byte>(array);\n");
        foreach (var member in classSymbol.GetMembers().OfType<IPropertySymbol>())
        {
            var propertyName = member.Name;
            var propertyType = member.Type;
            
            sb.Append($"//Serialize {propertyName} of type {propertyType}\n");
            if (propertyType.SpecialType == SpecialType.System_String)
            {
                sb.Append($"BitConverter.TryWriteBytes(span.Slice(offset, 4), {propertyName}Length);\n"); //Пишем размер строки для десериализации
                sb.Append("offset += 4;\n");
                sb.Append($"Encoding.UTF8.GetBytes({propertyName} ?? string.Empty, span.Slice(offset));\n");
                sb.Append($"offset += {propertyName}Length;\n");
            }
            else if (propertyType.SpecialType == SpecialType.System_DateTime)
            {
                sb.Append($"BitConverter.TryWriteBytes(span.Slice(offset, 8), {propertyName}.ToBinary());\n");
                sb.Append($"offset += 8;\n");
            }
            else
            {
                sb.Append(
                    $"BitConverter.TryWriteBytes(span.Slice(offset, {GetTypeByteCount(propertyType.SpecialType)}), {propertyName});\n");
                sb.Append($"offset += {GetTypeByteCount(propertyType.SpecialType)};\n");
            }
        }
        sb.Append("return array;\n");
        sb.Append("}\n");
        sb.Append("}\n");
        sb.Append("}");
        return sb.ToString();
    }

    private static int GetTypeByteCount(SpecialType type)
    {
        switch (type)
        {
            case SpecialType.System_Boolean:
            case SpecialType.System_SByte:
            case SpecialType.System_Byte:
                return 1;
        
            case SpecialType.System_Char:
            case SpecialType.System_Int16:
            case SpecialType.System_UInt16:
                return 2;
        
            case SpecialType.System_Int32:
            case SpecialType.System_UInt32:
            case SpecialType.System_Single:
                return 4;
        
            case SpecialType.System_Int64:
            case SpecialType.System_UInt64:
            case SpecialType.System_Double:
            case SpecialType.System_DateTime:
                return 8;
        
            case SpecialType.System_Decimal:
                return 16;
        
            case SpecialType.System_String:
            case SpecialType.System_Array:
            case SpecialType.System_Object:
            case SpecialType.System_ValueType:
            case SpecialType.System_Enum:
            case SpecialType.System_Nullable_T:
            case SpecialType.System_Collections_IEnumerable:
            case SpecialType.System_Collections_Generic_IEnumerable_T:
            case SpecialType.System_Collections_Generic_IList_T:
            case SpecialType.System_Collections_Generic_ICollection_T:
            case SpecialType.System_Collections_Generic_IReadOnlyList_T:
            case SpecialType.System_Collections_Generic_IReadOnlyCollection_T:
                return -1;
        
            case SpecialType.System_IntPtr:
            case SpecialType.System_UIntPtr:
                return -1; 
        
            case SpecialType.None:
            case SpecialType.System_Void:
            case SpecialType.System_MulticastDelegate:
            case SpecialType.System_Delegate:
            case SpecialType.System_Collections_IEnumerator:
            case SpecialType.System_Collections_Generic_IEnumerator_T:
            case SpecialType.System_Runtime_CompilerServices_IsVolatile:
            case SpecialType.System_IDisposable:
            case SpecialType.System_TypedReference:
            case SpecialType.System_ArgIterator:
            case SpecialType.System_RuntimeArgumentHandle:
            case SpecialType.System_RuntimeFieldHandle:
            case SpecialType.System_RuntimeMethodHandle:
            case SpecialType.System_RuntimeTypeHandle:
            case SpecialType.System_IAsyncResult:
            case SpecialType.System_AsyncCallback:
            case SpecialType.System_Runtime_CompilerServices_RuntimeFeature:
            case SpecialType.System_Runtime_CompilerServices_PreserveBaseOverridesAttribute:
            case SpecialType.System_Runtime_CompilerServices_InlineArrayAttribute:
                return 0;
        
            default:
                throw new ArgumentOutOfRangeException(nameof(type), type, null);
        }
    }
}

public class BinarySerializeGeneratorSyntaxReceiver : ISyntaxReceiver
{
    public List<ClassDeclarationSyntax> CandidateClasses { get; } = new();

    public void OnVisitSyntaxNode(SyntaxNode syntaxNode)
    {
        if (syntaxNode is ClassDeclarationSyntax classDeclarationSyntax &&
            classDeclarationSyntax.AttributeLists.Count > 0)
        {
            CandidateClasses.Add(classDeclarationSyntax);
        }
    }
}
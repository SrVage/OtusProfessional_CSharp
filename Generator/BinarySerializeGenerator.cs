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
        sb.Append("using var stream = new MemoryStream();\n");
        sb.Append("using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);\n");
        foreach (var member in classSymbol.GetMembers().OfType<IPropertySymbol>())
        {
            var propertyName = member.Name;
            var propertyType = member.Type;
            
            sb.Append($"//Serialize {propertyName} of type {propertyType}\n");
            if (propertyType.ToString().Equals("System.DateTime"))
            {
                sb.Append($"writer.Write({propertyName}.ToBinary());\n");
            }
            else
            {
                sb.Append($"writer.Write({propertyName});\n");
            }
        }
        sb.Append("writer.Flush();\n");
        sb.Append("return stream.ToArray();\n");
        sb.Append("}\n");
        sb.Append("}\n");
        sb.Append("}");
        return sb.ToString();
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
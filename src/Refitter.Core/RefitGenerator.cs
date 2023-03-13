﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NJsonSchema;
using NSwag;
using NSwag.CodeGeneration;
using NSwag.CodeGeneration.CSharp;

namespace Refitter.Core
{
    public class RefitGenerator
    {
        private readonly RefitGeneratorSettings settings;
        private readonly OpenApiDocument document;
        private readonly CSharpClientGeneratorFactory factory;
        private const string Separator = "    ";

        private RefitGenerator(RefitGeneratorSettings settings, OpenApiDocument document)
        {
            this.settings = settings;
            this.document = document;

            factory = new CSharpClientGeneratorFactory(settings, document);
        }

        public static async Task<RefitGenerator> CreateAsync(RefitGeneratorSettings settings) =>
            new(
                settings,
                await (settings.OpenApiPath.EndsWith("yaml") || settings.OpenApiPath.EndsWith("yml")
                    ? OpenApiYamlDocument.FromFileAsync(settings.OpenApiPath)
                    : OpenApiDocument.FromFileAsync(settings.OpenApiPath)));

        public string Generate()
        {
            var generator = factory.Create();
            var contracts = settings.GenerateContracts ? generator.GenerateFile() : string.Empty;
            var client = GenerateClient(generator);

            return new StringBuilder()
                .AppendLine(client)
                .AppendLine()
                .AppendLine(contracts)
                .ToString();
        }

        private string GenerateClient(CSharpClientGenerator generator)
        {
            var code = new StringBuilder();
            GenerateAutoGeneratedHeader(code);
            GenerateNamespaceImports(code);

            code.AppendLine("namespace " + settings.Namespace)
                .AppendLine("{");

            GenerateInterfaceDeclaration(code);
            GenerateInterfaceBody(generator, code);

            code.AppendLine("}");
            return code.ToString();
        }

        private void GenerateInterfaceBody(
            CSharpClientGenerator generator,
            StringBuilder code)
        {
            foreach (var kv in document.Paths)
            {
                foreach (var operations in kv.Value)
                {
                    var operation = operations.Value;

                    var returnTypeParameter = operation.Responses.ContainsKey("200")
                        ? generator.GetTypeName(operation.Responses["200"].Schema, true, null)
                        : null;

                    var returnType = returnTypeParameter is null or "void"
                        ? "Task"
                        : $"Task<{TrimImportedNamespaces(returnTypeParameter)}>";

                    var verb = CapitalizeFirstCharacter(operations.Key);
                    var name = ConvertKebabCaseToPascalCase(CapitalizeFirstCharacter(operation.OperationId));

                    var parameters = GetParameters(generator, operation);
                    var parametersString = string.Join(", ", parameters);

                    GenerateMethodXmlDocComments(operation, code);

                    code.AppendLine($"{Separator}{Separator}[{verb}(\"{kv.Key}\")]")
                        .AppendLine($"{Separator}{Separator}{returnType} {name}({parametersString});")
                        .AppendLine();
                }
            }

            code.AppendLine($"{Separator}}}");
        }

        private void GenerateMethodXmlDocComments(OpenApiOperation operation, StringBuilder code)
        {
            if (!settings.GenerateXmlDocCodeComments)
                return;
            
            if (!string.IsNullOrWhiteSpace(operation.Description))
            {
                code.AppendLine($"{Separator}{Separator}/// <summary>")
                    .AppendLine($"{Separator}{Separator}/// " + operation.Description)
                    .AppendLine($"{Separator}{Separator}/// </summary>");
            }
        }

        private void GenerateInterfaceDeclaration(StringBuilder code)
        {
            var title = settings.Naming.UseOpenApiTitle
                ? document.Info?.Title?
                      .Replace(" ", string.Empty)
                      .Replace("-", string.Empty)
                      .Replace(".", string.Empty) ??
                  "ApiClient"
                : settings.Naming.InterfaceName;

            code.AppendLine($"{Separator}public interface I{CapitalizeFirstCharacter(title)}")
                .AppendLine($"{Separator}{{");
        }

        private void GenerateNamespaceImports(StringBuilder code)
        {
            code.AppendLine(
                    string.Join(
                        Environment.NewLine,
                        "using Refit;",
                        "using System.Threading.Tasks;",
                        "using System.Collections.Generic;"))
                .AppendLine();
        }

        private void GenerateAutoGeneratedHeader(StringBuilder code)
        {
            if (!settings.AddAutoGeneratedHeader)
                return;
            
            code.AppendLine("// <auto-generated>")
                .AppendLine("//     This code was generated by Refitter.")
                .AppendLine("// </auto-generated>")
                .AppendLine();
        }

        private static string ConvertKebabCaseToPascalCase(string operationId)
        {
            var parts = operationId.Split('-');
            for (var i = 0; i < parts.Length; i++)
            {
                parts[i] = CapitalizeFirstCharacter(parts[i]);
            }

            return string.Join(string.Empty, parts);
        }

        private static IEnumerable<string> GetParameters(CSharpClientGenerator generator, OpenApiOperation operation)
        {
            var routeParameters = operation.Parameters
                .Where(p => p.Kind == OpenApiParameterKind.Path)
                .Select(p => $"{generator.GetTypeName(p.ActualTypeSchema, true, null)} {p.Name}")
                .ToList();

            var bodyParameters = operation.Parameters
                .Where(p => p.Kind == OpenApiParameterKind.Body)
                .Select(p => $"[Body]{GetBodyParameterType(generator, p)} {p.Name}")
                .ToList();

            var parameters = new List<string>();
            parameters.AddRange(routeParameters);
            parameters.AddRange(bodyParameters);
            return parameters;
        }

        private static string GetBodyParameterType(IClientGenerator generator, JsonSchema schema) =>
            TrimImportedNamespaces(
                FindSupportedType(
                    generator.GetTypeName(
                        schema.ActualTypeSchema,
                        true,
                        null)));

        private static string FindSupportedType(string typeName) =>
            typeName == "FileResponse" ? "StreamPart" : typeName;

        private static string TrimImportedNamespaces(string returnTypeParameter)
        {
            string[] wellKnownNamespaces = { "System.Collections.Generic" };
            foreach (var wellKnownNamespace in wellKnownNamespaces)
                if (returnTypeParameter.StartsWith(wellKnownNamespace, StringComparison.OrdinalIgnoreCase))
                    return returnTypeParameter.Replace(wellKnownNamespace + ".", string.Empty);
            return returnTypeParameter;
        }

        private static string CapitalizeFirstCharacter(string str)
        {
            return str.Substring(0, 1).ToUpperInvariant() +
                   str.Substring(1, str.Length - 1);
        }
    }
}
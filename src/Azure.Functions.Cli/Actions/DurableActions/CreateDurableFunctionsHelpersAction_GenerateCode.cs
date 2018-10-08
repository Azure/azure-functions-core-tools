using Buildalyzer;
using Buildalyzer.Workspaces;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.Azure.WebJobs;
using Microsoft.CodeAnalysis.Editing;
using Microsoft.CodeAnalysis.CSharp;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Azure.Functions.Cli.Actions.DurableActions
{
    // Putting this method in a separate file for the inclusion of the SyntaxFactory methods
    internal partial class CreateDurableFunctionsHelpersAction : BaseAction
    {
        private static CompilationUnitSyntax GenerateCode(Project project, IEnumerable<(SemanticModel model, IEnumerable<IMethodSymbol> methods)> methodsBySemanticModel)
        {
            var syntaxGenerator = SyntaxGenerator.GetGenerator(project);

            var namespaces = new HashSet<string>
            {
                "Microsoft.Azure.WebJobs",
                "System",
                "System.Collections.Generic",
                "System.Text",
                "System.Threading.Tasks"
            };
            UsingDirectiveSyntax NamespaceToUsingDirectiveSyntax(string @namespace)
            {
                NameSyntax buildNameSyntax()
                {

                    var hierarchy = @namespace.Split(".");
                    if (hierarchy.Length == 1)
                    {
                        return IdentifierName(hierarchy[0]);
                    }
                    var nameSyntax = QualifiedName(IdentifierName(hierarchy[0]), IdentifierName(hierarchy[1]));
                    for (int i = 2; i < hierarchy.Length; i++)
                    {
                        nameSyntax = QualifiedName(nameSyntax, IdentifierName(hierarchy[i]));
                    }
                    return nameSyntax;
                }
                return UsingDirective(buildNameSyntax());
            }
            var generatedMethods = methodsBySemanticModel.Select(o => GenerateMethodsForSemanticModel(o.model, o.methods, namespaces))
                                        .SelectMany(o => o)
                                        .Cast<MemberDeclarationSyntax>()
                                        .ToList();
            var usingDirectives = namespaces.Select(NamespaceToUsingDirectiveSyntax).ToList();

            return CompilationUnit()
                    .WithUsings(List(usingDirectives))
                    .WithMembers(
                        SingletonList<MemberDeclarationSyntax>(
                            NamespaceDeclaration(
                                IdentifierName("FunctionApp1")) // TODO - put this in the correct namespace
                            .WithMembers(
                                SingletonList<MemberDeclarationSyntax>(
                                    ClassDeclaration("DurableFunctionGeneratedExtensions")
                                    .WithModifiers(
                                        TokenList(
                                            new[]{
                                                Token(SyntaxKind.PublicKeyword),
                                                Token(SyntaxKind.StaticKeyword)}))
                                    .WithMembers(
                                        List(generatedMethods))))))
                    .NormalizeWhitespace();
        }

        private static IEnumerable<MethodDeclarationSyntax> GenerateMethodsForSemanticModel(SemanticModel model, IEnumerable<IMethodSymbol> methods, HashSet<string> namespaces)
        {
            return methods.Select(m => GenerateMethod(model, m, namespaces));
        }

        private static MethodDeclarationSyntax GenerateMethod(SemanticModel model, IMethodSymbol method, HashSet<string> namespaces)
        {
            AddNamespacesForMethodTypes(method, namespaces);

            var methodDeclaration = (MethodDeclarationSyntax)method.DeclaringSyntaxReferences[0].GetSyntax(); // TODO - What is the cost of this?

            // Ensure return type is Task/Task<T>
            var returnType = GetAsyncTypeFromType(methodDeclaration.ReturnType);
            var methodName = EnsureMethodNameEndsInAsync(method);
            var functionName = GetFunctionNameForMethod(method);

            var activityParameter = GetActivityTriggerParameter(method);

            var callActivityAsyncSyntax = GetCallActivityAsyncSyntax(returnType);

            return MethodDeclaration(returnType, methodName)
                    .WithModifiers(TokenList(new[] { Token(SyntaxKind.PublicKeyword), Token(SyntaxKind.StaticKeyword) }))
                    .WithParameterList(
                        ParameterList(
                            SeparatedList<ParameterSyntax>(
                                new SyntaxNodeOrToken[]{
                                        Parameter(Identifier("context"))
                                            .WithModifiers(
                                                TokenList(Token(SyntaxKind.ThisKeyword))
                                            )
                                            .WithType(
                                                IdentifierName("DurableOrchestrationContext")
                                            ),
                                        Token(SyntaxKind.CommaToken),
                                        Parameter(activityParameter.Identifier)
                                            .WithType(activityParameter.Type)
                                }
                            )
                        )
                    )
                    .WithBody(
                        Block(
                            SingletonList((StatementSyntax)ReturnStatement(
                                    InvocationExpression(
                                        MemberAccessExpression(
                                            SyntaxKind.SimpleMemberAccessExpression,
                                            IdentifierName("context"),
                                            callActivityAsyncSyntax
                                        )
                                    )
                                    .WithArgumentList(
                                        ArgumentList(
                                            SeparatedList<ArgumentSyntax>(
                                                new SyntaxNodeOrToken[]{
                                                        Argument(
                                                            LiteralExpression(
                                                                SyntaxKind.StringLiteralExpression,
                                                                Literal(functionName)
                                                            )
                                                        ),
                                                        Token(SyntaxKind.CommaToken),
                                                        Argument(
                                                            IdentifierName(activityParameter.Identifier.ValueText)
                                                        )
                                                }
                                            )
                                        )
                                    )
                                )
                            )
                        )
                    );
        }

        private static string EnsureMethodNameEndsInAsync(IMethodSymbol method)
        {
            var methodName = method.Name;
            if (!methodName.EndsWith("Async"))
            {
                methodName += "Async";
            }

            return methodName;
        }

        /// <summary>
        /// Generate the Syntax for calling CallActivityAsync, including the type argument list if required
        /// </summary>
        /// <param name="returnType"></param>
        /// <returns></returns>
        private static SimpleNameSyntax GetCallActivityAsyncSyntax(TypeSyntax returnType)
        {
            SimpleNameSyntax callActivityAsyncSyntax;

            var currentType = returnType;

            if (currentType is QualifiedNameSyntax qns)
            {
                currentType = qns.Right;
            }

            if (currentType is GenericNameSyntax gns
                && gns.TypeArgumentList.Arguments.Count == 1) // only add TypeArguments for Task<T> (for Task, leave alone)
            {
                // Have Task<T> - capture the T for the call to CallActivityAsync
                callActivityAsyncSyntax = GenericName(Identifier("CallActivityAsync"))
                                            .WithTypeArgumentList(gns.TypeArgumentList);
            }
            else
            {
                // just Task!
                callActivityAsyncSyntax = IdentifierName("CallActivityAsync");
            }

            return callActivityAsyncSyntax;
        }

        /// <summary>
        /// Add namespaces used in the specified method to the HashSet
        /// </summary>
        /// <param name="method"></param>
        /// <param name="namespaces"></param>
        private static void AddNamespacesForMethodTypes(IMethodSymbol method, HashSet<string> namespaces)
        {
            void AddNamespacesForType(ITypeSymbol typeSymbol)
            {
                namespaces.Add(typeSymbol.ContainingNamespace.ToString());
                if (typeSymbol is INamedTypeSymbol nts)
                    foreach (var typeArgument in nts.TypeArguments)
                        AddNamespacesForType(typeArgument);
            }
            AddNamespacesForType(method.ReturnType);
            foreach (var parameter in method.Parameters)
                AddNamespacesForType(parameter.Type);
            foreach (var typeArgument in method.TypeArguments)
                AddNamespacesForType(typeArgument);
        }

        /// <summary>
        /// Get the FunctionName for the method based on the FunctionNameAttribute
        /// </summary>
        /// <param name="method"></param>
        /// <returns></returns>
        private static string GetFunctionNameForMethod(IMethodSymbol method)
        {
            var functionNameAttribute = method.GetAttributes()
                                         .First(a => a.AttributeClass.ToString() == "Microsoft.Azure.WebJobs.FunctionNameAttribute");
            var functionName = (string)functionNameAttribute.ConstructorArguments[0].Value;
            return functionName;
        }

        /// <summary>
        /// Get the ParameterSyntax for the parameter that has the ActivityTriggerAttribute
        /// </summary>
        /// <param name="method"></param>
        /// <returns></returns>
        private static ParameterSyntax GetActivityTriggerParameter(IMethodSymbol method)
        {
            var parameter = method.Parameters.First(ParameterIsActivityTrigger);
            var syntax = (ParameterSyntax)parameter.DeclaringSyntaxReferences[0].GetSyntax();
            return syntax;
        }

        /// <summary>
        /// Get an async type (e.g. Task or Task&lt;T&gt;) from the specified TypeSyntax
        /// </summary>
        /// <param name="typeSyntax"></param>
        /// <returns></returns>
        private static TypeSyntax GetAsyncTypeFromType(TypeSyntax typeSyntax)
        {
            // The goal of this method is to return a type that represents an async method
            // E.g.s
            //     int       => Task<int>
            //     void      => Task
            //     Task      => Task
            //     Task<int> => Task<int>
            var result = typeSyntax;

            // common function to handle returnTypeSyntax directly and as part of a QualifiedNameSyntax
            TypeSyntax HandleSimpleNameSyntax(SimpleNameSyntax sns)
            {
                var snsResult = typeSyntax;
                if (sns is IdentifierNameSyntax ins)
                {
                    if (ins.Identifier.ValueText == "Task")
                    {
                        // nothing to do - already Task
                    }
                    else
                    {
                        // Convert T to Task<T>
                        snsResult = GenericName(
                                           Identifier("Task"))
                                               .WithTypeArgumentList(
                                                   TypeArgumentList(SingletonSeparatedList(typeSyntax))); // use returnTpeSyntax to keep name qualification
                    }
                }
                else if (sns is GenericNameSyntax gns)
                {
                    if (gns.Identifier.ValueText == "Task")
                    {
                        // nothing to do
                    }
                    else
                    {
                        snsResult = GenericName(
                                           Identifier("Task"))
                                               .WithTypeArgumentList(
                                                   TypeArgumentList(SingletonSeparatedList(typeSyntax))); // use returnTpeSyntax to keep name qualification
                    }
                }
                return snsResult;
            }

            if (typeSyntax is PredefinedTypeSyntax pts)
            {
                if (pts.Keyword.ValueText == "void")
                {
                    // convert void to Task
                    result = IdentifierName("Task");
                }
                else
                {
                    // convert T to Task<T>
                    result = GenericName(
                                        Identifier("Task"))
                                            .WithTypeArgumentList(
                                                TypeArgumentList(SingletonSeparatedList(typeSyntax)));
                }
            }
            else if (typeSyntax is SimpleNameSyntax sns)
            {
                result = HandleSimpleNameSyntax(sns);
            }
            else if (typeSyntax is QualifiedNameSyntax qns)
            {
                result = HandleSimpleNameSyntax(qns.Right);
            }
            return result;
        }
    }
}

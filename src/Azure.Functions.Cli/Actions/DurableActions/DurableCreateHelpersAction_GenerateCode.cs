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
using System.Collections.Immutable;

namespace Azure.Functions.Cli.Actions.DurableActions
{
    // Putting this method in a separate file for the inclusion of the SyntaxFactory methods
    internal partial class DurableCreateHelpersAction : BaseAction
    {
        private static CompilationUnitSyntax GenerateCode(Project project, IEnumerable<IMethodSymbol> activityMethods)
        {
            var syntaxGenerator = SyntaxGenerator.GetGenerator(project);


            NameSyntax buildNameSyntax(string @namespace)
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
            var activityMethodsByNamespace = activityMethods.GroupBy(m => m.ContainingNamespace);

            var generatedMethodsByNamespace = activityMethodsByNamespace.Select(
                group =>
                    {
                        var generatedMethods = group
                                                .SelectMany(m => (MemberDeclarationSyntax[])GenerateMethodForActivity(m)); 
                        return new
                        {
                            Namespace = group.Key,
                            GeneratedMethods = generatedMethods
                        };
                    })
                    .ToList();


            var namespacesWithGeneratedMethods = generatedMethodsByNamespace.Select(groupOfMethods =>
            {
                return NamespaceDeclaration(
                    buildNameSyntax(groupOfMethods.Namespace.ToString())
                )
                .WithMembers(
                    SingletonList<MemberDeclarationSyntax>(
                        ClassDeclaration("DurableFunctionGeneratedExtensions")
                        .WithModifiers(
                            TokenList(
                                new[]{
                                    Token(SyntaxKind.PublicKeyword),
                                    Token(SyntaxKind.StaticKeyword)
                                }
                            )
                        )
                        .WithMembers(
                            List(groupOfMethods.GeneratedMethods))
                        )
                    );
            });

            return CompilationUnit()
                    .WithUsings(
                        List(
                            new UsingDirectiveSyntax[] {
                                UsingDirective(
                                    QualifiedName(
                                        QualifiedName(
                                            IdentifierName("System"), 
                                            IdentifierName("Collections")
                                        ), 
                                        IdentifierName("Generic")
                                    )
                                ),
                                UsingDirective(
                                    QualifiedName(
                                        QualifiedName(
                                            IdentifierName("System"), 
                                            IdentifierName("Net")
                                        ), 
                                        IdentifierName("Http")
                                    )
                                ),
                                UsingDirective(
                                    QualifiedName(
                                        QualifiedName(
                                            IdentifierName("System"), 
                                            IdentifierName("Threading")
                                        ), 
                                        IdentifierName("Tasks")
                                    )
                                ),
                                UsingDirective(
                                    QualifiedName(
                                        QualifiedName(
                                            IdentifierName("Microsoft"), 
                                            IdentifierName("Azure")
                                        ), 
                                        IdentifierName("WebJobs")
                                    )
                                ),
                                UsingDirective(
                                    QualifiedName(
                                        QualifiedName(
                                            QualifiedName(
                                                QualifiedName(
                                                    IdentifierName("Microsoft"), 
                                                    IdentifierName("Azure")
                                                ), 
                                                IdentifierName("WebJobs")
                                            ), 
                                            IdentifierName("Extensions")
                                        ), 
                                        IdentifierName("Http")
                                    )
                                ),
                                UsingDirective(
                                    QualifiedName(
                                        QualifiedName(
                                            QualifiedName(
                                                IdentifierName("Microsoft"), 
                                                IdentifierName("Azure")
                                            ), 
                                            IdentifierName("WebJobs")
                                        ), 
                                        IdentifierName("Host")
                                    )
                                )
                            }
                        )
                    )
                    .WithMembers(
                        List(
                            namespacesWithGeneratedMethods
                                .Prepend(GetContextExtensionPoint())
                        )
                    )
                    .NormalizeWhitespace();
        }


        private static MemberDeclarationSyntax GetContextExtensionPoint()
        {
            return NamespaceDeclaration(
                        QualifiedName(QualifiedName(IdentifierName("Microsoft"), IdentifierName("Azure")), IdentifierName("WebJobs"))
                    )
                    .WithMembers(
                        List(new MemberDeclarationSyntax[]
                            {
                                ClassDeclaration("DurableFunctionActivityHelpers")
                                    .WithModifiers(TokenList(Token(SyntaxKind.PublicKeyword)))
                                    .WithMembers(
                                        List(new MemberDeclarationSyntax[]
                                            {
                                                ConstructorDeclaration(
                                                    Identifier("DurableFunctionActivityHelpers"))
                                                        .WithModifiers(TokenList(Token(SyntaxKind.PublicKeyword)))
                                                        .WithParameterList(
                                                            ParameterList(
                                                                SingletonSeparatedList(
                                                                    Parameter(Identifier("context")).WithType(IdentifierName("DurableOrchestrationContext"))
                                                                )
                                                            )
                                                        ).WithBody(
                                                            Block(
                                                                SingletonList<StatementSyntax>(
                                                                    ExpressionStatement(
                                                                        AssignmentExpression(
                                                                            SyntaxKind.SimpleAssignmentExpression,
                                                                            IdentifierName("Context"),
                                                                            IdentifierName("context")
                                                                        )
                                                                    )
                                                                )
                                                            )
                                                        ),
                                                PropertyDeclaration(
                                                        IdentifierName("DurableOrchestrationContext"),
                                                        Identifier("Context")
                                                ).WithModifiers(TokenList(Token(SyntaxKind.PublicKeyword)))
                                                .WithAccessorList(
                                                    AccessorList(
                                                        List(new AccessorDeclarationSyntax[] {
                                                            AccessorDeclaration(SyntaxKind.GetAccessorDeclaration)
                                                                .WithSemicolonToken(Token(SyntaxKind.SemicolonToken)),
                                                            AccessorDeclaration(SyntaxKind.SetAccessorDeclaration)
                                                                .WithModifiers(TokenList(Token(SyntaxKind.PrivateKeyword)))
                                                                .WithSemicolonToken(Token(SyntaxKind.SemicolonToken))
                                                        })
                                                    )
                                                )
                                            }
                                        )
                                    ),
                                ClassDeclaration("DurableFunctionActivityExtensions")
                                    .WithModifiers(TokenList(new[] {Token(SyntaxKind.PublicKeyword),Token(SyntaxKind.StaticKeyword)})
                                    ).WithMembers(
                                        SingletonList<MemberDeclarationSyntax>(
                                            MethodDeclaration(
                                                IdentifierName("DurableFunctionActivityHelpers"),
                                                Identifier("Activities")
                                            ).WithModifiers(TokenList(new[] { Token(SyntaxKind.PublicKeyword), Token(SyntaxKind.StaticKeyword) }))
                                            .WithParameterList(
                                                    ParameterList(
                                                        SingletonSeparatedList(
                                                            Parameter(Identifier("context"))
                                                                .WithModifiers(TokenList(Token(SyntaxKind.ThisKeyword)))
                                                                .WithType(IdentifierName("DurableOrchestrationContext"))
                                                        )
                                                    )
                                            ).WithBody(
                                                Block(
                                                    SingletonList<StatementSyntax>(
                                                        ReturnStatement(
                                                            ObjectCreationExpression(IdentifierName("DurableFunctionActivityHelpers"))
                                                                .WithArgumentList(
                                                                    ArgumentList(
                                                                        SingletonSeparatedList(Argument(IdentifierName("context")))
                                                                    )
                                                                )
                                                        )
                                                    )
                                                )
                                            )
                                        )
                                    )
                            }
                        )
                    );
        }
        private static MethodDeclarationSyntax[] GenerateMethodForActivity(IMethodSymbol method)
        {
            // Ensure return type is Task/Task<T>
            var returnType = GetAsyncTypeFromType(method.ReturnType);
            var methodName = EnsureMethodNameEndsInAsync(method);
            var methodNameWithRetry = methodName
                                        .Substring(0, methodName.Length - 5) // strip Async
                                        + "WithRetryAsync";
            var functionName = GetFunctionNameForMethod(method);

            var activityParameter = GetActivityTriggerParameter(method);

            var callActivityAsyncSyntax = GetCallActivityAsyncSyntax(returnType);
            var callActivityWithRetryAsyncSyntax = GetCallActivityAsyncSyntax(returnType, "CallActivityWithRetryAsync");

            return new[]
                {
                    MethodDeclaration(returnType, methodName)
                        .WithModifiers(TokenList(new[] { Token(SyntaxKind.PublicKeyword), Token(SyntaxKind.StaticKeyword) }))
                        .WithParameterList(
                            ParameterList(
                                SeparatedList<ParameterSyntax>(
                                    new SyntaxNodeOrToken[]{
                                            Parameter(Identifier("activityHelper"))
                                                .WithModifiers(
                                                    TokenList(Token(SyntaxKind.ThisKeyword))
                                                )
                                                .WithType(
                                                    IdentifierName("DurableFunctionActivityHelpers")
                                                ),
                                            Token(SyntaxKind.CommaToken),
                                            activityParameter
                                    }
                                )
                            )
                        )
                        .WithBody(
                            Block(
                                SingletonList<StatementSyntax>(
                                    ReturnStatement(
                                        InvocationExpression(
                                           MemberAccessExpression(
                                               SyntaxKind.SimpleMemberAccessExpression,
                                               MemberAccessExpression(
                                                    SyntaxKind.SimpleMemberAccessExpression,
                                                    IdentifierName("activityHelper"),
                                                    IdentifierName("Context")
                                                ),
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
                        ),
                    MethodDeclaration(returnType, methodNameWithRetry )
                        .WithModifiers(TokenList(new[] { Token(SyntaxKind.PublicKeyword), Token(SyntaxKind.StaticKeyword) }))
                        .WithParameterList(
                            ParameterList(
                                SeparatedList<ParameterSyntax>(
                                    new SyntaxNodeOrToken[]{
                                            Parameter(Identifier("activityHelper"))
                                                .WithModifiers(
                                                    TokenList(Token(SyntaxKind.ThisKeyword))
                                                )
                                                .WithType(
                                                    IdentifierName("DurableFunctionActivityHelpers")
                                                ),
                                            Token(SyntaxKind.CommaToken),
                                            Parameter(Identifier("retryOptions"))
                                                .WithType(IdentifierName("RetryOptions")),
                                            Token(SyntaxKind.CommaToken),
                                            activityParameter
                                    }
                                )
                            )
                        )
                        .WithBody(
                            Block(
                                SingletonList<StatementSyntax>(
                                    ReturnStatement(
                                        InvocationExpression(
                                           MemberAccessExpression(
                                               SyntaxKind.SimpleMemberAccessExpression,
                                               MemberAccessExpression(
                                                    SyntaxKind.SimpleMemberAccessExpression,
                                                    IdentifierName("activityHelper"),
                                                    IdentifierName("Context")
                                                ),
                                               callActivityWithRetryAsyncSyntax
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
                                                                IdentifierName("retryOptions")
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
                        ),
                };
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
        private static SimpleNameSyntax GetCallActivityAsyncSyntax(TypeSyntax returnType, string methodName = "CallActivityAsync")
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
                callActivityAsyncSyntax = GenericName(Identifier(methodName))
                                            .WithTypeArgumentList(gns.TypeArgumentList);
            }
            else
            {
                // just Task!
                callActivityAsyncSyntax = IdentifierName(methodName);
            }

            return callActivityAsyncSyntax;
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
            return Parameter(Identifier(parameter.Name))
                        .WithType(GetQualifiedName(parameter.Type));
        }

        /// <summary>
        ///  Get an async type (e.g. Task or Task&lt;T&gt;) from the specified ITypeSymbol
        /// </summary>
        /// <param name="typeSymbol"></param>
        /// <returns></returns>
        private static TypeSyntax GetAsyncTypeFromType(ITypeSymbol typeSymbol)
        {
            // The goal of this method is to return a type that represents an async method
            // E.g.s
            //     int       => Task<int>
            //     void      => Task
            //     Task      => Task
            //     Task<int> => Task<int>
            if (IsTask(typeSymbol))
            {
                // Already task - retain as is
                return GetQualifiedName(typeSymbol);
            }
            else
            {
                if (typeSymbol.SpecialType == SpecialType.System_Void)
                {
                    return QualifiedName(
                                QualifiedName(
                                    QualifiedName(
                                        IdentifierName("System"),
                                        IdentifierName("Threading")
                                    ),
                                    IdentifierName("Tasks")
                                ),
                                IdentifierName("Task")
                            );
                }
                else
                {
                    return QualifiedName(
                                QualifiedName(
                                    QualifiedName(
                                        IdentifierName("System"),
                                        IdentifierName("Threading")
                                    ),
                                    IdentifierName("Tasks")
                                ), GenericName(Identifier("Task"))
                                        .WithTypeArgumentList(
                                            TypeArgumentList(
                                                SingletonSeparatedList<TypeSyntax>(GetQualifiedName(typeSymbol))
                                            )
                                        )
                    );
                }

            }
        }

        private static bool IsTask(ITypeSymbol typeSymbol)
        {
            if (typeSymbol.Name == "Task"
                && typeSymbol.ContainingNamespace?.Name == "Tasks"
                && typeSymbol.ContainingNamespace?.ContainingNamespace?.Name == "Threading"
                && typeSymbol.ContainingNamespace?.ContainingNamespace?.ContainingNamespace?.Name == "System"
                )
            {
                return true;
            }
            return false;
        }
        private static NameSyntax GetQualifiedName(ISymbol symbol)
        {
            SimpleNameSyntax getIdentiferWithGenericArgsIfApplicable(ISymbol s)
            {
                INamedTypeSymbol namedTypeSymbol = symbol as INamedTypeSymbol;
                if (namedTypeSymbol != null && namedTypeSymbol.IsGenericType)
                {
                    return GenericName(Identifier(s.Name))
                                .WithTypeArgumentList(GetTypeArgumentList(namedTypeSymbol.TypeArguments));
                }
                else
                {
                    return IdentifierName(s.Name);
                }
            }

            if (symbol.ContainingSymbol != null)
            {
                var namespaceSymbol = symbol.ContainingSymbol as INamespaceSymbol;
                if (namespaceSymbol == null
                    || !namespaceSymbol.IsGlobalNamespace)
                {
                    return QualifiedName(
                                GetQualifiedName(symbol.ContainingSymbol),
                                getIdentiferWithGenericArgsIfApplicable(symbol)
                            );
                }
            }
            return getIdentiferWithGenericArgsIfApplicable(symbol);
        }

        private static TypeArgumentListSyntax GetTypeArgumentList(ImmutableArray<ITypeSymbol> typeArguments)
        {
            var list = new List<SyntaxNodeOrToken>();
            bool addSeparator = false;
            foreach (var typeArgument in typeArguments)
            {
                if (addSeparator)
                {
                    list.Add(Token(SyntaxKind.CommaToken));
                }
                list.Add(GetQualifiedName(typeArgument));
                addSeparator = true;
            }
            return TypeArgumentList(
                        SeparatedList<TypeSyntax>(list)
                    );
        }
    }
}

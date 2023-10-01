using System.Collections.Immutable;
using System.Composition;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;
using Microsoft.CodeAnalysis.Simplification;
using Microsoft.CodeAnalysis.Text;
using static ComputeSharp.SourceGeneration.Diagnostics.DiagnosticDescriptors;

namespace ComputeSharp.D2D1.CodeFixers;

/// <summary>
/// A code fixer that adds the <c>[D2DGeneratedPixelShaderDescriptor]</c> to D2D1 shader types with no descriptor.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp)]
[Shared]
public sealed class MissingPixelShaderDescriptorOnPixelShaderCodeFixer : CodeFixProvider
{
    /// <inheritdoc/>
    public override ImmutableArray<string> FixableDiagnosticIds { get; } = ImmutableArray.Create(MissingPixelShaderDescriptorOnPixelShaderTypeId);

    /// <inheritdoc/>
    public override FixAllProvider? GetFixAllProvider()
    {
        return WellKnownFixAllProviders.BatchFixer;
    }

    /// <inheritdoc/>
    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        Diagnostic diagnostic = context.Diagnostics[0];
        TextSpan diagnosticSpan = context.Span;

        SyntaxNode? root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);

        // Get the struct declaration from the target diagnostic
        if (root!.FindNode(diagnosticSpan) is StructDeclarationSyntax structDeclaration)
        {
            // Register the code fix to update the return type to be Task instead
            context.RegisterCodeFix(
                CodeAction.Create(
                    title: "Add [D2DGeneratedPixelShaderDescriptor] attribute",
                    createChangedDocument: token => ChangeReturnType(context.Document, root, structDeclaration, token),
                    equivalenceKey: "Add [D2DGeneratedPixelShaderDescriptor] attribute"),
                diagnostic);
        }
    }

    /// <summary>
    /// Applies the code fix to add the [D2DGeneratedPixelShaderDescriptor] attribute to a target type.
    /// </summary>
    /// <param name="document">The original document being fixed.</param>
    /// <param name="root">The original tree root belonging to the current document.</param>
    /// <param name="structDeclaration">The <see cref="StructDeclarationSyntax"/> to update.</param>
    /// <param name="cancellationToken">The cancellation token for the operation.</param>
    /// <returns>An updated document with the applied code fix, and the return type of the method being <see cref="Task"/>.</returns>
    private static async Task<Document> ChangeReturnType(Document document, SyntaxNode root, StructDeclarationSyntax structDeclaration, CancellationToken cancellationToken)
    {
        // Get the semantic model (bail if it's not available)
        if (await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false) is not SemanticModel semanticModel)
        {
            return document;
        }

        // Also bail if we can't resolve the [D2DGeneratedPixelShaderDescriptor] attribute symbol (this should really never happen)
        if (semanticModel.Compilation.GetTypeByMetadataName("ComputeSharp.D2D1.D2DGeneratedPixelShaderDescriptorAttribute") is not INamedTypeSymbol attributeSymbol)
        {
            return document;
        }

        int index = 0;

        // Find the index to use to insert the attribute. We want to make it so that if the struct declaration
        // has a bunch of D2D attributes, the new one will be inserted right after that. This way the final list
        // will be nicely sorted, instead of having D2D attributes interleaving other unrelated attributes, if any.
        foreach (AttributeListSyntax attributeList in structDeclaration.AttributeLists)
        {
            // Make sure we have an attribute to check
            if (attributeList.Attributes is not [AttributeSyntax attribute, ..])
            {
                continue;
            }

            // Make sure we find a readable identifier for the attribute
            if (attribute.Name is not IdentifierNameSyntax { Identifier.Value: string identifier })
            {
                break;
            }

            // If the attribute is D2D one, increment the index and continue
            if (identifier.Contains("D2D"))
            {
                index++;
            }
            else
            {
                // Otherwise, stop here, we reached the end of the sequence
                break;
            }
        }

        SyntaxGenerator syntaxGenerator = SyntaxGenerator.GetGenerator(document);

        // Create the attribute syntax for the new attribute. Also annotate it
        // to automatically add using directives to the document, if needed.
        // Then create the attribute syntax and insert it at the right position.
        SyntaxNode attributeTypeSyntax = syntaxGenerator.TypeExpression(attributeSymbol).WithAdditionalAnnotations(Simplifier.AddImportsAnnotation);
        SyntaxNode attributeSyntax = syntaxGenerator.Attribute(attributeTypeSyntax);
        SyntaxNode updatedStructDeclarationSyntax = syntaxGenerator.InsertAttributes(structDeclaration, index, attributeSyntax);

        // Replace the node in the document tree
        return document.WithSyntaxRoot(root.ReplaceNode(structDeclaration, updatedStructDeclarationSyntax));
    }
}

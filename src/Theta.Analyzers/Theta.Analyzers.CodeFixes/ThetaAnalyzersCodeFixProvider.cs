using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Theta.Analyzers;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(ThetaAnalyzersCodeFixProvider)), Shared]
public class ThetaAnalyzersCodeFixProvider : CodeFixProvider
{
	public sealed override ImmutableArray<string> FixableDiagnosticIds => [
		ThetaAnalyzersAnalyzer.DiagnosticID_Sealed
	];

	public sealed override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

	public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
	{
		SyntaxNode root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);

		foreach (Diagnostic diagnostic in context.Diagnostics)
		{
			TypeDeclarationSyntax declaration = root.FindToken(diagnostic.Location.SourceSpan.Start).Parent.AncestorsAndSelf().OfType<TypeDeclarationSyntax>().First();

			switch (diagnostic.Id)
			{
				case ThetaAnalyzersAnalyzer.DiagnosticID_Sealed: context.RegisterCodeFix(CodeAction.Create("Mark class as sealed", c => ApplySealedModifierAsync(context.Document, declaration, c), "MarkAsSealed"), diagnostic);
					break;
			}
		}
	}

	private static async Task<Document> ApplySealedModifierAsync(Document document, TypeDeclarationSyntax typeDecl, CancellationToken cancellationToken)
	{
		SyntaxNode root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);

		SyntaxTokenList modifiers = typeDecl.Modifiers;

		if (!modifiers.Any(m => m.IsKind(SyntaxKind.SealedKeyword)))
		{
			List<SyntaxKind> visibilityKinds = [SyntaxKind.PublicKeyword, SyntaxKind.InternalKeyword, SyntaxKind.PrivateKeyword, SyntaxKind.ProtectedKeyword];

			modifiers = modifiers.Insert(modifiers.Max(m => visibilityKinds.IndexOf(m.Kind())) + 1, SyntaxFactory.Token(SyntaxKind.SealedKeyword).WithTrailingTrivia(SyntaxFactory.Space));
		}

		return document.WithSyntaxRoot(root.ReplaceNode(typeDecl, typeDecl.WithModifiers(modifiers)));
	}
}

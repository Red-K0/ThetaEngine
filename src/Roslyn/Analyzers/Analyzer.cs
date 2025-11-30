using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Theta.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class Analyzer : DiagnosticAnalyzer
{
	public const string DiagnosticID_Sealed = "THTA0001";
	public const string DiagnosticID_DuplicateID = "THTA0002";

	private static readonly DiagnosticDescriptor SealedRule = new(
		DiagnosticID_Sealed,
		"Entity classes must be sealed",
		"The class '{0}' is marked with [EntityMarker], but is not sealed. Marked entities should not be inheritable.",
		"Usage",
		DiagnosticSeverity.Warning,
		true
	);

	private static readonly DiagnosticDescriptor DuplicateIDRule = new(
		DiagnosticID_DuplicateID,
		"Duplicate Entity ID",
		"'{0}' is annotated with type ID {1}, which is already used by '{2}'. Conflicting type IDs will lead to serialization failures.",
		"Usage",
		DiagnosticSeverity.Error,
		true
	);

	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [SealedRule, DuplicateIDRule];

	public override void Initialize(AnalysisContext context)
	{
		context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
		context.EnableConcurrentExecution();

		context.RegisterCompilationStartAction(compilationStartContext =>
		{
			Dictionary<ushort, string> seenEntityIDs = [];

			compilationStartContext.RegisterSymbolAction(symbolContext => AnalyzeSymbol(symbolContext, seenEntityIDs), SymbolKind.NamedType);
		});
	}

	private static void AnalyzeSymbol(SymbolAnalysisContext context, Dictionary<ushort, string> seenEntityIDs)
	{
		INamedTypeSymbol type = (INamedTypeSymbol)context.Symbol;

		if (type.TypeKind != TypeKind.Class) return;

		AttributeData entityAttr = type.GetAttributes().FirstOrDefault(a => a.AttributeClass.Name.StartsWith("EntityMarker"));

		if (entityAttr == null) return;

		if (!type.IsSealed)
		{
			Location location = type.Locations.FirstOrDefault();

			if (location != null) context.ReportDiagnostic(Diagnostic.Create(SealedRule, location, type.Name));
		}

		ushort ID = (ushort)entityAttr.ConstructorArguments[0].Value;

		if (seenEntityIDs.TryGetValue(ID, out string existingType))
		{
			Location location = type.Locations.FirstOrDefault();

			if (location != null) context.ReportDiagnostic(Diagnostic.Create(DuplicateIDRule, location, type.Name, ID, existingType));
		}
		else
		{
			seenEntityIDs[ID] = type.Name;
		}
	}
}
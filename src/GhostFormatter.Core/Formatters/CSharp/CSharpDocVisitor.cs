using System.Text.RegularExpressions;
using GhostFormatter.Core.Modals;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static GhostFormatter.Core.Docs.Docs;

namespace GhostFormatter.Core.Formatters.CSharp;

public sealed record CSharpDocVisitorOptions(bool ThrowOnUnsupportedSyntax = true)
{
    public static readonly CSharpDocVisitorOptions Default = new();
}

/// <summary>
/// Thrown from DefaultVisit when a syntax node has no dedicated Visit override and
/// ThrowOnUnsupportedSyntax is set. This exists so unsupported syntax is a loud, specific,
/// catchable failure - "GhostFormatter doesn't know how to format a FunctionPointerType yet" -
/// rather than a silently-degraded ToFullString() dump that looks like real output. See point 18
/// of the brief.
/// </summary>
public sealed class UnsupportedSyntaxException : Exception
{
    public SyntaxKind SyntaxKind { get; }
    public string Location { get; }
    public string SourceSnippet { get; }

    public UnsupportedSyntaxException(SyntaxNode node)
        : base(
            $"GhostFormatter has no visitor for {node.Kind()} ({node.GetType().Name}) at "
                + $"{node.GetLocation().GetLineSpan()}. Source: {Snippet(node)}"
        )
    {
        SyntaxKind = node.Kind();
        Location = node.GetLocation().GetLineSpan().ToString();
        SourceSnippet = Snippet(node);
    }

    private static string Snippet(SyntaxNode node)
    {
        var text = node.ToString();
        return text.Length > 120 ? text[..120] + "..." : text;
    }
}

/// <summary>
/// Roslyn-AST -> Doc-IR visitor for the full practical C# syntax surface. See the accompanying
/// written response for the architectural audit this was built against; in short:
///   - the class must derive from CSharpSyntaxVisitor&lt;Doc&gt; (generic), not the non-generic,
///     void-returning CSharpSyntaxVisitor the original file declared - that could not have
///     compiled as written.
///   - trivia (comments, blank lines, directives) is threaded through explicitly via WithTrivia
///     at every statement/member/list-element call site (CSharpDocVisitor.Trivia.cs), instead of
///     being lost to ToFullString().
///   - "is this optional-brace statement body a block, or a single bare statement" and similar
///     structural questions are answered from the real child-node types, never string-sniffed.
///
/// The class is split into partial-class files purely to keep each file a manageable size:
///   CSharpDocVisitor.cs              - this file: compilation unit, types, members, shared helpers
///   CSharpDocVisitor.Statements.cs   - statements
///   CSharpDocVisitor.Expressions.cs  - expressions
///   CSharpDocVisitor.Patterns.cs     - patterns, switch expressions, query expressions
///   CSharpDocVisitor.TypesAndMisc.cs - type syntax, literals, attributes
///   CSharpDocVisitor.Trivia.cs       - comment/trivia plumbing
/// </summary>
public class CSharpDocVisitor : CSharpSyntaxVisitor<Doc>
{
    // ============================================================
    // (originally in CSharpDocVisitor.cs)
    // ============================================================
    private readonly CSharpDocVisitorOptions _options;

    public CSharpDocVisitor()
        : this(CSharpDocVisitorOptions.Default) { }

    public CSharpDocVisitor(CSharpDocVisitorOptions options)
    {
        _options = options;
    }

    // ---------------------------------------------------------------------------------------
    // Small dispatch helpers
    // ---------------------------------------------------------------------------------------

    /// <summary>Visits a required (non-null) child node, producing a clear failure instead of a
    /// silent empty string if a supposedly-required child is somehow missing (malformed/partial
    /// source being formatted while the user is still typing, for example).</summary>
    private Doc VisitR(SyntaxNode node) =>
        Visit(node)
        ?? throw new InvalidOperationException($"Visit({node.Kind()}) returned null unexpectedly.");

    /// <summary>Visits an optional child node, returning `whenNull` (default: empty) if absent.</summary>
    private Doc VisitO(SyntaxNode? node, Doc? whenNull = null) =>
        node == null ? whenNull ?? (Doc)"" : VisitR(node);

    public override Doc DefaultVisit(SyntaxNode node)
    {
        if (_options.ThrowOnUnsupportedSyntax)
            throw new UnsupportedSyntaxException(node);

        // Only reachable when the caller explicitly opted into lenient mode. The marker comment
        // makes it structurally impossible to mistake this for real formatted output, and the
        // raw text is still routed through FromRawText so it doesn't corrupt the printer's column
        // tracking if it happens to contain newlines.
        return Concat(
            $"/* GHOSTFORMATTER:UNSUPPORTED {node.Kind()} */",
            HardLine,
            FromRawText(node.ToFullString().Trim())
        );
    }

    // =========================================================================================
    // Compilation unit / usings / externs / namespaces
    // =========================================================================================

    public override Doc VisitCompilationUnit(CompilationUnitSyntax node)
    {
        var sections = new List<Doc>();

        if (node.Externs.Count > 0)
            sections.Add(JoinWithBlankLines(node.Externs, e => WithTrivia(e, VisitR(e))));

        if (node.Usings.Count > 0)
            sections.Add(JoinWithBlankLines(node.Usings, u => WithTrivia(u, VisitR(u))));

        if (node.AttributeLists.Count > 0)
            sections.Add(
                RenderAttributeLists(node.AttributeLists, inline: false, trailingHardLine: false)
            );

        if (node.Members.Count > 0)
            sections.Add(
                JoinWithBlankLines(
                    node.Members,
                    m => WithTrivia(m, VisitR(m)),
                    forceBlankLine: true
                )
            );

        if (sections.Count == 0)
        {
            // An empty file. Still preserve any comments that were floating at end-of-file.
            var eof = BuildLeadingTrivia(node.EndOfFileToken.LeadingTrivia);
            return eof.Doc;
        }

        var body = Join(Concat(HardLine, HardLine), sections);
        var eofTrivia = BuildLeadingTrivia(node.EndOfFileToken.LeadingTrivia);
        return IsEmpty(eofTrivia.Doc) ? body : Concat(body, HardLine, HardLine, eofTrivia.Doc);
    }

    public override Doc VisitExternAliasDirective(ExternAliasDirectiveSyntax node) =>
        Concat("extern alias ", node.Identifier.Text, ";");

    public override Doc VisitUsingDirective(UsingDirectiveSyntax node)
    {
        var parts = new List<Doc>();
        if (!node.GlobalKeyword.IsKind(SyntaxKind.None))
            parts.Add("global ");
        parts.Add("using ");
        if (!node.StaticKeyword.IsKind(SyntaxKind.None))
            parts.Add("static ");
        if (node.Alias != null)
        {
            parts.Add(node.Alias.Name.Identifier.Text);
            parts.Add(" = ");
        }

        parts.Add(VisitR(node.Name!));
        parts.Add(";");
        return Concat([.. parts]);
    }

    public override Doc VisitFileScopedNamespaceDeclaration(
        FileScopedNamespaceDeclarationSyntax node
    )
    {
        var parts = new List<Doc>
        {
            RenderAttributeLists(node.AttributeLists, inline: false, trailingHardLine: true),
            "namespace ",
            VisitR(node.Name),
            ";",
        };

        if (node.Externs.Count > 0)
            parts.Add(
                Concat(
                    HardLine,
                    HardLine,
                    JoinWithBlankLines(node.Externs, e => WithTrivia(e, VisitR(e)))
                )
            );
        if (node.Usings.Count > 0)
            parts.Add(
                Concat(
                    HardLine,
                    HardLine,
                    JoinWithBlankLines(node.Usings, u => WithTrivia(u, VisitR(u)))
                )
            );
        if (node.Members.Count > 0)
            parts.Add(
                Concat(
                    HardLine,
                    HardLine,
                    JoinWithBlankLines(
                        node.Members,
                        m => WithTrivia(m, VisitR(m)),
                        forceBlankLine: true
                    )
                )
            );

        return Concat([.. parts]);
    }

    public override Doc VisitNamespaceDeclaration(NamespaceDeclarationSyntax node)
    {
        var header = Concat(
            RenderAttributeLists(node.AttributeLists, inline: false, trailingHardLine: true),
            "namespace ",
            VisitR(node.Name)
        );

        var innerSections = new List<Doc>();
        if (node.Externs.Count > 0)
            innerSections.Add(JoinWithBlankLines(node.Externs, e => WithTrivia(e, VisitR(e))));
        if (node.Usings.Count > 0)
            innerSections.Add(JoinWithBlankLines(node.Usings, u => WithTrivia(u, VisitR(u))));
        if (node.Members.Count > 0)
            innerSections.Add(
                JoinWithBlankLines(
                    node.Members,
                    m => WithTrivia(m, VisitR(m)),
                    forceBlankLine: true
                )
            );

        var inner =
            innerSections.Count == 0 ? null : Join(Concat(HardLine, HardLine), innerSections);
        return Concat(header, RenderBracedBlock(inner, node.OpenBraceToken, node.CloseBraceToken));
    }

    // =========================================================================================
    // Type declarations (class / struct / interface / record / record struct)
    // =========================================================================================

    public override Doc VisitClassDeclaration(ClassDeclarationSyntax node) =>
        VisitTypeDeclarationCore(node, "class");

    public override Doc VisitStructDeclaration(StructDeclarationSyntax node) =>
        VisitTypeDeclarationCore(node, "struct");

    public override Doc VisitInterfaceDeclaration(InterfaceDeclarationSyntax node) =>
        VisitTypeDeclarationCore(node, "interface");

    public override Doc VisitRecordDeclaration(RecordDeclarationSyntax node)
    {
        var keyword =
            node.ClassOrStructKeyword.IsKind(SyntaxKind.StructKeyword) ? "record struct"
            : node.ClassOrStructKeyword.IsKind(SyntaxKind.ClassKeyword) ? "record class"
            : "record";
        return VisitTypeDeclarationCore(node, keyword);
    }

    private Doc VisitTypeDeclarationCore(TypeDeclarationSyntax node, string keyword)
    {
        var headerParts = new List<Doc>
        {
            RenderAttributeLists(node.AttributeLists, inline: false, trailingHardLine: true),
            RenderModifiers(node.Modifiers),
            keyword,
            " ",
            node.Identifier.Text,
            VisitO(node.TypeParameterList),
        };

        // Primary constructors (`class Foo(int x)`, `record Point(int X, int Y)`) live on the
        // shared TypeDeclarationSyntax base as of C# 12 - classes and structs can carry one now,
        // not just records, so this must not be gated on the node being a RecordDeclarationSyntax.
        if (node.ParameterList != null)
            headerParts.Add(RenderParameterList(node.ParameterList));

        if (node.BaseList != null)
            headerParts.Add(
                Concat(" : ", Join(", ", node.BaseList.Types.Select(t => WithTrivia(t, VisitR(t)))))
            );

        headerParts.Add(RenderConstraintClauses(node.ConstraintClauses));

        var header = Concat(headerParts.ToArray());

        var hasBody = !node.OpenBraceToken.IsKind(SyntaxKind.None);
        if (!hasBody)
            return Concat(header, ";");

        Doc? members =
            node.Members.Count == 0
                ? null
                : JoinWithBlankLines(
                    node.Members,
                    m => WithTrivia(m, VisitR(m)),
                    forceBlankLine: true
                );

        return Concat(
            header,
            RenderBracedBlock(members, node.OpenBraceToken, node.CloseBraceToken)
        );
    }

    public override Doc VisitEnumDeclaration(EnumDeclarationSyntax node)
    {
        var header = Concat(
            RenderAttributeLists(node.AttributeLists, inline: false, trailingHardLine: true),
            RenderModifiers(node.Modifiers),
            "enum ",
            node.Identifier.Text,
            node.BaseList != null
                ? Concat(" : ", Join(", ", node.BaseList.Types.Select(t => VisitR(t))))
                : ""
        );

        var dangling = BuildLeadingTrivia(node.CloseBraceToken.LeadingTrivia);
        if (node.Members.Count == 0)
        {
            return IsEmpty(dangling.Doc)
                ? Concat(header, " { }")
                : Concat(header, HardLine, "{", Indent(HardLine, dangling.Doc), HardLine, "}");
        }

        var hadTrailingComma = node.Members.SeparatorCount == node.Members.Count;
        var memberDocs = node.Members.Select(m => WithTrivia(m, VisitR(m))).ToList();
        var body = Concat(Join(Concat(",", HardLine), memberDocs), hadTrailingComma ? "," : "");
        if (!IsEmpty(dangling.Doc))
            body = Concat(body, HardLine, HardLine, dangling.Doc);

        return Concat(header, HardLine, "{", Indent(HardLine, body), HardLine, "}");
    }

    public override Doc VisitEnumMemberDeclaration(EnumMemberDeclarationSyntax node) =>
        Concat(
            RenderAttributeLists(node.AttributeLists, inline: false, trailingHardLine: true),
            node.Identifier.Text,
            node.EqualsValue != null ? Concat(" = ", VisitR(node.EqualsValue.Value)) : ""
        );

    public override Doc VisitDelegateDeclaration(DelegateDeclarationSyntax node) =>
        Concat(
            RenderAttributeLists(node.AttributeLists, inline: false, trailingHardLine: true),
            RenderModifiers(node.Modifiers),
            "delegate ",
            VisitR(node.ReturnType),
            " ",
            node.Identifier.Text,
            VisitO(node.TypeParameterList),
            RenderParameterList(node.ParameterList),
            RenderConstraintClauses(node.ConstraintClauses),
            ";"
        );

    // =========================================================================================
    // Members
    // =========================================================================================

    public override Doc VisitFieldDeclaration(FieldDeclarationSyntax node) =>
        Concat(
            RenderAttributeLists(node.AttributeLists, inline: false, trailingHardLine: true),
            RenderModifiers(node.Modifiers),
            RenderVariableDeclaration(node.Declaration),
            ";"
        );

    public override Doc VisitEventFieldDeclaration(EventFieldDeclarationSyntax node) =>
        Concat(
            RenderAttributeLists(node.AttributeLists, inline: false, trailingHardLine: true),
            RenderModifiers(node.Modifiers),
            "event ",
            RenderVariableDeclaration(node.Declaration),
            ";"
        );

    private Doc RenderVariableDeclaration(VariableDeclarationSyntax declaration)
    {
        var vars = declaration.Variables.Select(v => WithTrivia(v, VisitR(v))).ToList();
        return Group(VisitR(declaration.Type), " ", Indent(Join(Concat(",", Line), vars)));
    }

    public override Doc VisitVariableDeclarator(VariableDeclaratorSyntax node)
    {
        Doc name = node.Identifier.Text;
        if (node.ArgumentList != null) // fixed-size buffer: fixed int _buf[10];
            name = Concat(name, RenderBracketedArgumentList(node.ArgumentList));

        if (node.Initializer == null)
            return name;

        return Group(name, " =", RenderAssignmentRhs(node.Initializer.Value));
    }

    public override Doc VisitConstructorDeclaration(ConstructorDeclarationSyntax node)
    {
        var header = Concat(
            RenderAttributeLists(node.AttributeLists, inline: false, trailingHardLine: true),
            RenderModifiers(node.Modifiers),
            node.Identifier.Text,
            RenderParameterList(node.ParameterList)
        );

        var initializer =
            node.Initializer != null ? Indent(HardLine, ": ", VisitR(node.Initializer)) : (Doc)"";

        return Concat(
            header,
            initializer,
            RenderMemberBody(node.Body, node.ExpressionBody, node.SemicolonToken)
        );
    }

    public override Doc VisitConstructorInitializer(ConstructorInitializerSyntax node) =>
        Concat(node.ThisOrBaseKeyword.Text, RenderArgumentList(node.ArgumentList));

    public override Doc VisitDestructorDeclaration(DestructorDeclarationSyntax node) =>
        Concat(
            RenderAttributeLists(node.AttributeLists, inline: false, trailingHardLine: true),
            RenderModifiers(node.Modifiers),
            "~",
            node.Identifier.Text,
            RenderParameterList(node.ParameterList),
            RenderMemberBody(node.Body, node.ExpressionBody, node.SemicolonToken)
        );

    public override Doc VisitMethodDeclaration(MethodDeclarationSyntax node) =>
        Concat(
            RenderAttributeLists(node.AttributeLists, inline: false, trailingHardLine: true),
            RenderModifiers(node.Modifiers),
            VisitR(node.ReturnType),
            " ",
            node.ExplicitInterfaceSpecifier != null
                ? Concat(VisitR(node.ExplicitInterfaceSpecifier.Name), ".")
                : "",
            node.Identifier.Text,
            VisitO(node.TypeParameterList),
            RenderParameterList(node.ParameterList),
            RenderConstraintClauses(node.ConstraintClauses),
            RenderMemberBody(node.Body, node.ExpressionBody, node.SemicolonToken)
        );

    public override Doc VisitOperatorDeclaration(OperatorDeclarationSyntax node) =>
        Concat(
            RenderAttributeLists(node.AttributeLists, inline: false, trailingHardLine: true),
            RenderModifiers(node.Modifiers),
            VisitR(node.ReturnType),
            " operator ",
            node.OperatorToken.Text,
            RenderParameterList(node.ParameterList),
            RenderMemberBody(node.Body, node.ExpressionBody, node.SemicolonToken)
        );

    public override Doc VisitConversionOperatorDeclaration(
        ConversionOperatorDeclarationSyntax node
    ) =>
        Concat(
            RenderAttributeLists(node.AttributeLists, inline: false, trailingHardLine: true),
            RenderModifiers(node.Modifiers),
            node.ImplicitOrExplicitKeyword.Text,
            " operator ",
            VisitR(node.Type),
            RenderParameterList(node.ParameterList),
            RenderMemberBody(node.Body, node.ExpressionBody, node.SemicolonToken)
        );

    public override Doc VisitPropertyDeclaration(PropertyDeclarationSyntax node)
    {
        var header = Concat(
            RenderAttributeLists(node.AttributeLists, inline: false, trailingHardLine: true),
            RenderModifiers(node.Modifiers),
            VisitR(node.Type),
            " ",
            node.ExplicitInterfaceSpecifier != null
                ? Concat(VisitR(node.ExplicitInterfaceSpecifier.Name), ".")
                : "",
            node.Identifier.Text
        );

        if (node.ExpressionBody != null)
            return Concat(
                header,
                Group(Indent(Line, "=> ", VisitR(node.ExpressionBody.Expression))),
                ";"
            );

        var accessors =
            node.AccessorList != null ? RenderAccessorList(node.AccessorList) : (Doc)" { }";
        var initializer =
            node.Initializer != null
                ? Group(" =", RenderAssignmentRhs(node.Initializer.Value))
                : (Doc)"";
        var trailingSemicolon = node.Initializer != null ? ";" : "";

        return Concat(header, accessors, initializer, trailingSemicolon);
    }

    public override Doc VisitIndexerDeclaration(IndexerDeclarationSyntax node)
    {
        var header = Concat(
            RenderAttributeLists(node.AttributeLists, inline: false, trailingHardLine: true),
            RenderModifiers(node.Modifiers),
            VisitR(node.Type),
            " ",
            node.ExplicitInterfaceSpecifier != null
                ? Concat(VisitR(node.ExplicitInterfaceSpecifier.Name), ".")
                : "",
            "this",
            RenderBracketedParameterList(node.ParameterList)
        );

        if (node.ExpressionBody != null)
            return Concat(
                header,
                Group(Indent(Line, "=> ", VisitR(node.ExpressionBody.Expression))),
                ";"
            );

        var accessors =
            node.AccessorList != null ? RenderAccessorList(node.AccessorList) : (Doc)" { }";
        return Concat(header, accessors);
    }

    public override Doc VisitEventDeclaration(EventDeclarationSyntax node)
    {
        var header = Concat(
            RenderAttributeLists(node.AttributeLists, inline: false, trailingHardLine: true),
            RenderModifiers(node.Modifiers),
            "event ",
            VisitR(node.Type),
            " ",
            node.ExplicitInterfaceSpecifier != null
                ? Concat(VisitR(node.ExplicitInterfaceSpecifier.Name), ".")
                : "",
            node.Identifier.Text
        );

        var accessors =
            node.AccessorList != null ? RenderAccessorList(node.AccessorList) : (Doc)";";
        return Concat(header, accessors);
    }

    public override Doc VisitAccessorList(AccessorListSyntax node) => RenderAccessorList(node);

    private Doc RenderAccessorList(AccessorListSyntax node)
    {
        var accessors = node.Accessors;
        var allSimple = accessors.All(a => a.Body == null && a.ExpressionBody == null);

        if (allSimple)
        {
            // { get; set; } - kept on one line, matching CSharpier's auto-property formatting.
            // Still a Group so pathologically long attribute/modifier combinations can force a
            // break rather than overflow the print width.
            var simple = Join(" ", accessors.Select(a => WithTrivia(a, RenderSimpleAccessor(a))));
            return Group(" {", Indent(Line, simple), Line, "}");
        }

        var docs = accessors.Select(a => WithTrivia(a, VisitR(a))).ToList();
        return Concat(HardLine, "{", Indent(HardLine, Join(HardLine, docs)), HardLine, "}");
    }

    private Doc RenderSimpleAccessor(AccessorDeclarationSyntax node) =>
        Concat(
            RenderAttributeLists(node.AttributeLists, inline: true, trailingHardLine: false),
            RenderModifiers(node.Modifiers),
            node.Keyword.Text,
            ";"
        );

    public override Doc VisitAccessorDeclaration(AccessorDeclarationSyntax node)
    {
        var header = Concat(
            RenderAttributeLists(node.AttributeLists, inline: true, trailingHardLine: false),
            RenderModifiers(node.Modifiers),
            node.Keyword.Text
        );

        if (node.ExpressionBody != null)
            return Concat(
                header,
                Group(Indent(Line, "=> ", VisitR(node.ExpressionBody.Expression))),
                ";"
            );

        if (node.Body != null)
            return Concat(header, HardLine, VisitR(node.Body));

        return Concat(header, ";");
    }

    /// <summary>Shared "block body, or expression body, or semicolon" rendering used by methods,
    /// constructors, destructors, operators and local functions.</summary>
    private Doc RenderMemberBody(
        BlockSyntax? body,
        ArrowExpressionClauseSyntax? expressionBody,
        SyntaxToken semicolon
    )
    {
        if (expressionBody != null)
            return Concat(Group(Indent(Line, "=> ", VisitR(expressionBody.Expression))), ";");

        if (body != null)
            return Concat(HardLine, VisitR(body));

        return ";"; // abstract/extern/partial member with no body
    }

    // =========================================================================================
    // Shared: modifiers, attributes, parameter lists, type parameters, constraints, blocks
    // =========================================================================================

    private static Doc RenderModifiers(SyntaxTokenList modifiers) =>
        modifiers.Count == 0 ? "" : Concat(string.Join(" ", modifiers.Select(m => m.Text)), " ");

    private Doc RenderAttributeLists(
        SyntaxList<AttributeListSyntax> lists,
        bool inline,
        bool trailingHardLine
    )
    {
        if (lists.Count == 0)
            return "";

        if (inline)
            return Concat(Join(" ", lists.Select(l => WithTrivia(l, VisitR(l)))), " ");

        var rendered = lists.Select(l => WithTrivia(l, VisitR(l))).ToList();
        var parts = new List<Doc>(rendered.Count * 2);
        for (var i = 0; i < rendered.Count; i++)
        {
            parts.Add(rendered[i]);
            if (i < rendered.Count - 1 || trailingHardLine)
                parts.Add(HardLine);
        }

        return Concat(parts.ToArray());
    }

    public override Doc VisitAttributeList(AttributeListSyntax node)
    {
        var target = node.Target != null ? Concat(node.Target.Identifier.Text, ": ") : (Doc)"";
        var attrs = node.Attributes.Select(a => VisitR(a)).ToList();
        return Group("[", target, Indent(SoftLine, Join(Concat(",", Line), attrs)), SoftLine, "]");
    }

    public override Doc VisitAttribute(AttributeSyntax node) =>
        Concat(VisitR(node.Name), node.ArgumentList != null ? VisitR(node.ArgumentList) : "");

    public override Doc VisitAttributeArgumentList(AttributeArgumentListSyntax node)
    {
        if (node.Arguments.Count == 0)
            return "()";
        var args = node.Arguments.Select(a => VisitR(a)).ToList();
        return Group("(", Indent(SoftLine, Join(Concat(",", Line), args)), SoftLine, ")");
    }

    public override Doc VisitAttributeArgument(AttributeArgumentSyntax node)
    {
        Doc prefix = "";
        if (node.NameEquals != null)
            prefix = Concat(node.NameEquals.Name.Identifier.Text, " = ");
        else if (node.NameColon != null)
            prefix = Concat(node.NameColon.Name.Identifier.Text, ": ");

        return Concat(prefix, VisitR(node.Expression));
    }

    private Doc RenderParameterList(ParameterListSyntax node)
    {
        if (node.Parameters.Count == 0)
            return "()";

        var parameters = node.Parameters.Select(p => WithTrivia(p, VisitR(p))).ToList();
        return Group("(", Indent(SoftLine, Join(Concat(",", Line), parameters)), SoftLine, ")");
    }

    public override Doc VisitParameterList(ParameterListSyntax node) => RenderParameterList(node);

    private Doc RenderBracketedParameterList(BracketedParameterListSyntax node)
    {
        var parameters = node.Parameters.Select(p => WithTrivia(p, VisitR(p))).ToList();
        if (parameters.Count == 0)
            return "[]";
        return Group("[", Indent(SoftLine, Join(Concat(",", Line), parameters)), SoftLine, "]");
    }

    public override Doc VisitBracketedParameterList(BracketedParameterListSyntax node) =>
        RenderBracketedParameterList(node);

    public override Doc VisitParameter(ParameterSyntax node)
    {
        var parts = new List<Doc>
        {
            RenderAttributeLists(node.AttributeLists, inline: true, trailingHardLine: false),
        };

        if (node.Modifiers.Count > 0)
            parts.Add(Concat(string.Join(" ", node.Modifiers.Select(m => m.Text)), " "));

        if (node.Type != null)
        {
            parts.Add(VisitR(node.Type));
            parts.Add(" ");
        }

        parts.Add(node.Identifier.Text);

        if (node.Default != null)
            parts.Add(Concat(" = ", VisitR(node.Default.Value)));

        return Concat(parts.ToArray());
    }

    private Doc RenderTypeParameterList(TypeParameterListSyntax? node) =>
        node == null ? "" : VisitR(node);

    public override Doc VisitTypeParameterList(TypeParameterListSyntax node)
    {
        var parameters = node.Parameters.Select(p => VisitR(p)).ToList();
        return Concat("<", Join(", ", parameters), ">");
    }

    public override Doc VisitTypeParameter(TypeParameterSyntax node) =>
        Concat(
            RenderAttributeLists(node.AttributeLists, inline: true, trailingHardLine: false),
            node.VarianceKeyword.IsKind(SyntaxKind.None) ? "" : node.VarianceKeyword.Text + " ",
            node.Identifier.Text
        );

    private Doc RenderConstraintClauses(SyntaxList<TypeParameterConstraintClauseSyntax> clauses)
    {
        if (clauses.Count == 0)
            return "";

        var docs = clauses.Select(c => Concat(HardLine, VisitR(c)));
        return Indent(Concat(docs.ToArray()));
    }

    public override Doc VisitTypeParameterConstraintClause(TypeParameterConstraintClauseSyntax node)
    {
        var constraints = node.Constraints.Select(c => VisitR(c)).ToList();
        return Concat("where ", node.Name.Identifier.Text, " : ", Join(", ", constraints));
    }

    public override Doc VisitClassOrStructConstraint(ClassOrStructConstraintSyntax node) =>
        node.ClassOrStructKeyword.Text + (node.QuestionToken.IsKind(SyntaxKind.None) ? "" : "?");

    public override Doc VisitTypeConstraint(TypeConstraintSyntax node) => VisitR(node.Type);

    public override Doc VisitConstructorConstraint(ConstructorConstraintSyntax node) => "new()";

    public override Doc VisitDefaultConstraint(DefaultConstraintSyntax node) => "default";

    // NOTE: C# 13's "allows ref struct" anti-constraint (AllowsConstraintClauseSyntax) is
    // deliberately NOT hand-rolled here - the exact shape of that syntax type wasn't something
    // this response could verify against the real Roslyn assembly (see "Remaining limitations").
    // It will hit DefaultVisit and throw UnsupportedSyntaxException, which is the correct outcome
    // per point 18 of the brief: a wrong guess at an API shape that fails to compile is strictly
    // worse than an honest, loud "not supported yet".

    public override Doc VisitSimpleBaseType(SimpleBaseTypeSyntax node) => VisitR(node.Type);

    public override Doc VisitPrimaryConstructorBaseType(PrimaryConstructorBaseTypeSyntax node) =>
        Concat(VisitR(node.Type), RenderArgumentList(node.ArgumentList));

    /// <summary>Renders "{ }" for an empty, comment-free body, or a full Allman-style braced
    /// block otherwise. Shared by namespaces and type declarations; BlockSyntax (statement bodies)
    /// has its own near-identical VisitBlock in CSharpDocVisitor.Statements.cs since it also has
    /// to deal with a dangling trailing comment before the closing brace.</summary>
    private Doc RenderBracedBlock(Doc? content, SyntaxToken openBrace, SyntaxToken closeBrace)
    {
        var danglingComment = BuildLeadingTrivia(closeBrace.LeadingTrivia);
        var hasDangling = !IsEmpty(danglingComment.Doc);

        if (content == null && !hasDangling)
            return " { }";

        Doc inner;
        if (content == null)
            inner = danglingComment.Doc;
        else if (hasDangling)
            inner = Concat(content, HardLine, HardLine, danglingComment.Doc);
        else
            inner = content;

        return Concat(HardLine, "{", Indent(HardLine, inner), HardLine, "}");
    }

    // ============================================================
    // (originally in CSharpDocVisitor.Trivia.cs)
    // ============================================================
    private readonly record struct LeadingTriviaResult(Doc Doc, bool BlankLineBeforeNode);

    /// <summary>
    /// Wraps a rendered node with its own leading comments/directives and same-line trailing
    /// comment. This is the single place trivia enters the pipeline - it is called explicitly at
    /// statement, member, and list-element granularity (see call sites), never automatically, so
    /// a token's trivia is never accidentally rendered twice by two different ancestor nodes.
    /// </summary>
    private Doc WithTrivia(SyntaxNode node, Doc content)
    {
        var leading = BuildLeadingTrivia(node.GetLeadingTrivia());
        var trailing = BuildTrailingTrivia(node.GetTrailingTrivia());
        return Concat(leading.Doc, content, trailing);
    }

    /// <summary>True if there is at least one blank source line between the previous sibling and
    /// the start of `node`'s own leading trivia (i.e. before its first attached comment, if any -
    /// blank lines between multiple leading comments are preserved separately, inside
    /// BuildLeadingTrivia's own output).</summary>
    private bool HasBlankLineBefore(SyntaxNode node) =>
        BuildLeadingTrivia(node.GetLeadingTrivia()).BlankLineBeforeNode;

    /// <summary>
    /// Joins a list of already-rendered sibling docs with a hard line, additionally inserting a
    /// second hard line (a blank line) wherever the original source had at least one blank line
    /// between those two siblings. Comments attached to each element must already be included in
    /// that element's own doc (typically via WithTrivia) - this only controls inter-sibling
    /// spacing, mirroring CSharpier's "collapse 2+ blank lines to 1, preserve 0 as 0" behavior for
    /// members and statements.
    /// </summary>
    /// <summary>
    /// Joins a list of already-rendered sibling docs with a hard line. When forceBlankLine is
    /// false (the default - used for statements inside a method body), a second hard line is
    /// inserted only where the original source already had a blank line between those two
    /// siblings, collapsing 2+ blank lines to 1 and preserving 0 as 0. When forceBlankLine is
    /// true (used for type members - fields, properties, methods, nested types - at class/struct
    /// /interface/record/namespace/file level), a blank line is always inserted between every
    /// pair of siblings, regardless of what the source did, matching the common "one blank line
    /// between every member" C# convention (e.g. StyleCop's SA1516). Comments attached to each
    /// element must already be included in that element's own doc (typically via WithTrivia) -
    /// this only controls inter-sibling spacing.
    /// </summary>
    private Doc JoinWithBlankLines<T>(
        IReadOnlyList<T> nodes,
        System.Func<T, Doc> render,
        bool forceBlankLine = false
    )
        where T : SyntaxNode
    {
        if (nodes.Count == 0)
            return "";

        var parts = new List<Doc>(nodes.Count * 2);
        for (var i = 0; i < nodes.Count; i++)
        {
            if (i > 0)
            {
                parts.Add(HardLine);
                if (forceBlankLine || HasBlankLineBefore(nodes[i]))
                    parts.Add(HardLine);
            }

            parts.Add(render(nodes[i]));
        }

        return Concat(parts.ToArray());
    }

    private static bool IsCommentTrivia(SyntaxTrivia trivia) =>
        trivia.IsKind(SyntaxKind.SingleLineCommentTrivia)
        || trivia.IsKind(SyntaxKind.MultiLineCommentTrivia)
        || trivia.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia)
        || trivia.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia);

    private static bool IsDirectiveTrivia(SyntaxTrivia trivia) =>
        trivia.IsKind(SyntaxKind.RegionDirectiveTrivia)
        || trivia.IsKind(SyntaxKind.EndRegionDirectiveTrivia)
        || trivia.IsKind(SyntaxKind.IfDirectiveTrivia)
        || trivia.IsKind(SyntaxKind.ElifDirectiveTrivia)
        || trivia.IsKind(SyntaxKind.ElseDirectiveTrivia)
        || trivia.IsKind(SyntaxKind.EndIfDirectiveTrivia)
        || trivia.IsKind(SyntaxKind.PragmaWarningDirectiveTrivia)
        || trivia.IsKind(SyntaxKind.PragmaChecksumDirectiveTrivia)
        || trivia.IsKind(SyntaxKind.NullableDirectiveTrivia)
        || trivia.IsKind(SyntaxKind.DefineDirectiveTrivia)
        || trivia.IsKind(SyntaxKind.UndefDirectiveTrivia)
        || trivia.IsKind(SyntaxKind.LineDirectiveTrivia)
        || trivia.IsKind(SyntaxKind.WarningDirectiveTrivia)
        || trivia.IsKind(SyntaxKind.ErrorDirectiveTrivia);

    /// <summary>
    /// Parses a leading trivia list into a Doc that reproduces every comment, XML doc comment,
    /// preprocessor directive and disabled (#if-excluded) region it contains, each on its own
    /// hard-broken line, preserving at most one blank line between consecutive pieces and between
    /// the last piece and the node - and reports whether there was a blank line *before* the
    /// first piece (or, if there were no comments/directives at all, before the node itself), so
    /// the caller can decide sibling-to-sibling spacing.
    /// </summary>
    private LeadingTriviaResult BuildLeadingTrivia(SyntaxTriviaList triviaList)
    {
        var parts = new List<Doc>();
        var newlineRun = 0;
        var sawContent = false;
        var blankBeforeFirst = false;

        foreach (var trivia in triviaList)
        {
            if (trivia.IsKind(SyntaxKind.WhitespaceTrivia))
                continue;

            if (trivia.IsKind(SyntaxKind.EndOfLineTrivia))
            {
                newlineRun++;
                continue;
            }

            if (trivia.IsKind(SyntaxKind.DisabledTextTrivia))
            {
                if (!sawContent)
                    blankBeforeFirst = newlineRun >= 2;
                else if (newlineRun >= 2)
                    parts.Add(HardLine);

                // Disabled (#if false ... #endif) source is reproduced byte-for-byte: it is, by
                // definition, code GhostFormatter must not try to parse or reformat.
                parts.Add(FromRawText(trivia.ToFullString().TrimEnd('\n', '\r')));
                newlineRun = 0;
                sawContent = true;
                continue;
            }

            if (!IsCommentTrivia(trivia) && !IsDirectiveTrivia(trivia))
                continue;

            if (!sawContent)
                blankBeforeFirst = newlineRun >= 2;
            else if (newlineRun >= 2)
                parts.Add(HardLine);

            parts.Add(RenderTriviaPiece(trivia));
            parts.Add(HardLine);
            newlineRun = 0;
            sawContent = true;
        }

        bool blankLineBeforeNode;
        if (sawContent)
        {
            if (newlineRun >= 2)
                parts.Add(HardLine); // blank line between the last comment/directive and the node
            blankLineBeforeNode = blankBeforeFirst;
        }
        else
        {
            blankLineBeforeNode = newlineRun >= 2;
        }

        return new LeadingTriviaResult(
            parts.Count > 0 ? Concat(parts.ToArray()) : (Doc)"",
            blankLineBeforeNode
        );
    }

    /// <summary>Renders a same-line trailing comment (if any) using LineSuffix, so it attaches to
    /// the end of the current output line regardless of what other Doc content for this node is
    /// still queued after it.</summary>
    private Doc BuildTrailingTrivia(SyntaxTriviaList triviaList)
    {
        foreach (var trivia in triviaList)
        {
            if (trivia.IsKind(SyntaxKind.EndOfLineTrivia))
                break; // trailing trivia only ever covers up to the end of the current line

            if (IsCommentTrivia(trivia))
                return LineSuffix(Concat(" ", RenderTriviaPiece(trivia)));
        }

        return "";
    }

    /// <summary>Returns true if `node`'s trailing trivia (up to the next newline) contains a
    /// same-line comment - used by statement/member lists so the terminating hard line for that
    /// element is not swallowed ahead of a comment that must stay attached to it.</summary>
    private static bool HasTrailingComment(SyntaxNode node) =>
        node.GetTrailingTrivia()
            .TakeWhile(t => !t.IsKind(SyntaxKind.EndOfLineTrivia))
            .Any(IsCommentTrivia);

    private Doc RenderTriviaPiece(SyntaxTrivia trivia)
    {
        if (trivia.IsKind(SyntaxKind.SingleLineCommentTrivia))
            return NormalizeSingleLineComment(trivia.ToString());

        if (trivia.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia))
            return FormatDocComment(trivia);

        if (trivia.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia))
            return FormatDocComment(trivia);

        if (trivia.IsKind(SyntaxKind.MultiLineCommentTrivia))
            return FromRawText(trivia.ToString());

        // Preprocessor directives: re-emit verbatim (minus surrounding whitespace/EOL, which the
        // caller supplies via HardLine) - their internal spacing/casing is not a formatting concern.
        return trivia.ToFullString().Trim();
    }

    /// <summary>Single-line "//" comments are reproduced exactly, except trailing whitespace is
    /// trimmed - CSharpier does not otherwise touch comment contents (changing them would risk
    /// altering meaning, e.g. inside commented-out code or ASCII diagrams).</summary>
    private static string NormalizeSingleLineComment(string text) => text.TrimEnd();

    /// <summary>Reproduces a "/// ..." or "/** ... */" documentation comment. Roslyn represents
    /// this as a DocumentationCommentTrivia structured node in the trivia's own tiny syntax tree;
    /// re-parsing that is unnecessary for our purposes (doc comments are prose, not code we
    /// reformat token-by-token) - the safe, semantics-preserving move is to reproduce the trivia's
    /// exact text, keyed only for stray trailing whitespace per line.</summary>
    private static Doc FormatDocComment(SyntaxTrivia trivia)
    {
        var text = trivia.ToFullString();
        var lines = Regex.Split(text.Replace("\r\n", "\n"), "\n");
        var trimmed = lines.Select(l => l.TrimEnd()).ToArray();
        return FromRawText(string.Join("\n", trimmed));
    }

    // ============================================================
    // (originally in CSharpDocVisitor.Statements.cs)
    // ============================================================
    // =========================================================================================
    // Blocks
    // =========================================================================================

    public override Doc VisitBlock(BlockSyntax node)
    {
        var dangling = BuildLeadingTrivia(node.CloseBraceToken.LeadingTrivia);

        if (node.Statements.Count == 0)
        {
            return IsEmpty(dangling.Doc)
                ? "{ }"
                : Concat("{", Indent(HardLine, dangling.Doc), HardLine, "}");
        }

        var body = JoinWithBlankLines(node.Statements, s => WithTrivia(s, RenderStatement(s)));
        if (!IsEmpty(dangling.Doc))
            body = Concat(body, HardLine, HardLine, dangling.Doc);

        return Concat("{", Indent(HardLine, body), HardLine, "}");
    }

    /// <summary>Dispatches to Visit(statement), except for ExpressionStatement/LocalDeclaration
    /// etc. which are already handled by their own VisitXxx overrides - this indirection exists
    /// purely so call sites don't need to special-case "does this statement kind already include
    /// its own trailing semicolon".</summary>
    private Doc RenderStatement(StatementSyntax statement) => VisitR(statement);

    public override Doc VisitEmptyStatement(EmptyStatementSyntax node) => ";";

    public override Doc VisitGlobalStatement(GlobalStatementSyntax node) => VisitR(node.Statement);

    /// <summary>Renders the body of an if/for/foreach/while/lock/using/fixed statement: a full
    /// Allman block if the source used one, otherwise the single statement always placed on its
    /// own indented line. CSharpier does NOT insert braces around brace-optional bodies (confirmed
    /// via belav/csharpier#303, "Always break statements without braces" - the fix made the
    /// bare statement always move to its own line, it did not make CSharpier add { }). The
    /// PRIMARY OBJECTIVE example in the brief assumed brace-insertion; this reproduces verified
    /// CSharpier behavior instead - see the written response's limitations section.</summary>
    private Doc RenderEmbeddedStatement(StatementSyntax statement)
    {
        if (statement is BlockSyntax block)
            return Concat(HardLine, VisitR(block));

        if (statement is EmptyStatementSyntax)
            return ";"; // `if (x) ;` - keep degenerate empty body inline, nothing to break onto its own line.

        return Indent(HardLine, WithTrivia(statement, VisitR(statement)));
    }

    // =========================================================================================
    // Declarations / expression statements
    // =========================================================================================

    public override Doc VisitLocalDeclarationStatement(LocalDeclarationStatementSyntax node)
    {
        var parts = new List<Doc>();
        if (!node.AwaitKeyword.IsKind(SyntaxKind.None))
            parts.Add("await ");
        if (!node.UsingKeyword.IsKind(SyntaxKind.None))
            parts.Add("using ");
        if (node.IsConst)
            parts.Add("const ");

        parts.Add(RenderVariableDeclaration(node.Declaration));
        parts.Add(";");
        return Concat(parts.ToArray());
    }

    public override Doc VisitLocalFunctionStatement(LocalFunctionStatementSyntax node) =>
        Concat(
            RenderAttributeLists(node.AttributeLists, inline: false, trailingHardLine: true),
            RenderModifiers(node.Modifiers),
            VisitR(node.ReturnType),
            " ",
            node.Identifier.Text,
            VisitO(node.TypeParameterList),
            RenderParameterList(node.ParameterList),
            RenderConstraintClauses(node.ConstraintClauses),
            RenderMemberBody(node.Body, node.ExpressionBody, node.SemicolonToken)
        );

    public override Doc VisitExpressionStatement(ExpressionStatementSyntax node) =>
        Concat(VisitR(node.Expression), ";");

    // =========================================================================================
    // if / else
    // =========================================================================================

    public override Doc VisitIfStatement(IfStatementSyntax node)
    {
        var header = Concat("if (", RenderParenthesizedCondition(node.Condition), ")");
        var result = Concat(header, RenderEmbeddedStatement(node.Statement));

        if (node.Else != null)
            result = Concat(result, RenderElseClause(node.Else));

        return result;
    }

    private Doc RenderElseClause(ElseClauseSyntax node)
    {
        var leading = BuildLeadingTrivia(node.ElseKeyword.LeadingTrivia);
        var prefix = IsEmpty(leading.Doc) ? HardLine : Concat(HardLine, leading.Doc);

        if (node.Statement is IfStatementSyntax nestedIf)
        {
            var nestedHeader = Concat(
                "else if (",
                RenderParenthesizedCondition(nestedIf.Condition),
                ")"
            );
            var nestedResult = Concat(
                prefix,
                nestedHeader,
                RenderEmbeddedStatement(nestedIf.Statement)
            );
            return nestedIf.Else != null
                ? Concat(nestedResult, RenderElseClause(nestedIf.Else))
                : nestedResult;
        }

        return Concat(prefix, "else", RenderEmbeddedStatement(node.Statement));
    }

    private Doc RenderParenthesizedCondition(ExpressionSyntax condition) =>
        Group(Indent(SoftLine, VisitR(condition)), SoftLine);

    // =========================================================================================
    // switch statement
    // =========================================================================================

    public override Doc VisitSwitchStatement(SwitchStatementSyntax node)
    {
        var header = Concat("switch (", RenderParenthesizedCondition(node.Expression), ")");
        var dangling = BuildLeadingTrivia(node.CloseBraceToken.LeadingTrivia);

        if (node.Sections.Count == 0)
        {
            return IsEmpty(dangling.Doc)
                ? Concat(header, HardLine, "{ }")
                : Concat(header, HardLine, "{", Indent(HardLine, dangling.Doc), HardLine, "}");
        }

        var body = JoinWithBlankLines(node.Sections, s => WithTrivia(s, VisitR(s)));
        if (!IsEmpty(dangling.Doc))
            body = Concat(body, HardLine, HardLine, dangling.Doc);

        return Concat(header, HardLine, "{", Indent(HardLine, body), HardLine, "}");
    }

    public override Doc VisitSwitchSection(SwitchSectionSyntax node)
    {
        var labels = Join(HardLine, node.Labels.Select(l => WithTrivia(l, VisitR(l))));

        if (node.Statements.Count == 1 && node.Statements[0] is BlockSyntax onlyBlock)
        {
            // `case 1: { ... }` keeps the block attached to the label line rather than indenting
            // it below - matches CSharpier's documented fix for "extra newline in switch case
            // statement with curly braces" (belav/csharpier#1192).
            return Concat(labels, " ", WithTrivia(onlyBlock, VisitR(onlyBlock)));
        }

        var statements = JoinWithBlankLines(
            node.Statements,
            s => WithTrivia(s, RenderStatement(s))
        );
        return Concat(labels, Indent(HardLine, statements));
    }

    public override Doc VisitCaseSwitchLabel(CaseSwitchLabelSyntax node) =>
        Concat("case ", VisitR(node.Value), ":");

    public override Doc VisitDefaultSwitchLabel(DefaultSwitchLabelSyntax node) => "default:";

    // =========================================================================================
    // loops
    // =========================================================================================

    public override Doc VisitForStatement(ForStatementSyntax node)
    {
        var initializers =
            node.Declaration != null
                ? new List<Doc> { RenderVariableDeclaration(node.Declaration) }
                : node.Initializers.Select(e => VisitR(e)).ToList();

        var condition = node.Condition != null ? VisitR(node.Condition) : (Doc)"";
        var incrementors = node.Incrementors.Select(e => VisitR(e)).ToList();

        var clauses = Concat(
            Join(", ", initializers),
            ";",
            Line,
            condition,
            ";",
            Line,
            Join(", ", incrementors)
        );

        var header = Concat("for (", Group(Indent(SoftLine, clauses), SoftLine), ")");
        return Concat(header, RenderEmbeddedStatement(node.Statement));
    }

    public override Doc VisitForEachStatement(ForEachStatementSyntax node)
    {
        var awaitPrefix = node.AwaitKeyword.IsKind(SyntaxKind.None) ? "" : "await ";

        var header = Concat(
            awaitPrefix,
            "foreach (",
            VisitR(node.Type),
            " ",
            node.Identifier.Text,
            " in ",
            VisitR(node.Expression),
            ")"
        );
        return Concat(header, RenderEmbeddedStatement(node.Statement));
    }

    public override Doc VisitForEachVariableStatement(ForEachVariableStatementSyntax node)
    {
        var awaitPrefix = node.AwaitKeyword.IsKind(SyntaxKind.None) ? "" : "await ";
        var header = Concat(
            awaitPrefix,
            "foreach (",
            VisitR(node.Variable),
            " in ",
            VisitR(node.Expression),
            ")"
        );
        return Concat(header, RenderEmbeddedStatement(node.Statement));
    }

    public override Doc VisitWhileStatement(WhileStatementSyntax node)
    {
        var header = Concat("while (", RenderParenthesizedCondition(node.Condition), ")");
        return Concat(header, RenderEmbeddedStatement(node.Statement));
    }

    public override Doc VisitDoStatement(DoStatementSyntax node)
    {
        var bodyPart = node.Statement is BlockSyntax block
            ? Concat(HardLine, VisitR(block), " ")
            : Concat(
                Indent(HardLine, WithTrivia(node.Statement, VisitR(node.Statement))),
                HardLine
            );

        return Concat(
            "do",
            bodyPart,
            "while (",
            RenderParenthesizedCondition(node.Condition),
            ");"
        );
    }

    // =========================================================================================
    // jumps
    // =========================================================================================

    public override Doc VisitBreakStatement(BreakStatementSyntax node) => "break;";

    public override Doc VisitContinueStatement(ContinueStatementSyntax node) => "continue;";

    public override Doc VisitReturnStatement(ReturnStatementSyntax node) =>
        node.Expression == null
            ? "return;"
            : Concat("return", RenderStatementLevelExpressionSuffix(node.Expression), ";");

    public override Doc VisitThrowStatement(ThrowStatementSyntax node) =>
        node.Expression == null
            ? "throw;"
            : Concat("throw", RenderStatementLevelExpressionSuffix(node.Expression), ";");

    public override Doc VisitYieldStatement(YieldStatementSyntax node)
    {
        if (node.Expression == null)
            return "yield break;";

        return Concat("yield return", RenderStatementLevelExpressionSuffix(node.Expression), ";");
    }

    public override Doc VisitGotoStatement(GotoStatementSyntax node)
    {
        if (node.Expression == null)
            return "goto default;"; // `goto default;` inside a switch (CaseOrDefaultKeyword handles this)
        if (!node.CaseOrDefaultKeyword.IsKind(SyntaxKind.None))
            return Concat("goto case ", VisitR(node.Expression), ";");
        return Concat("goto ", VisitR(node.Expression), ";");
    }

    public override Doc VisitLabeledStatement(LabeledStatementSyntax node) =>
        Concat(
            node.Identifier.Text,
            ":",
            HardLine,
            WithTrivia(node.Statement, RenderStatement(node.Statement))
        );

    /// <summary>Shared "keyword expr;" wrapping for return/throw/yield return: keeps the value
    /// glued to the keyword when it fits, otherwise moves it to an indented continuation line -
    /// except for expressions whose own layout already starts with a brace-like construct (object/
    /// collection initializers, lambdas), which stay glued so their own internal Allman braces do
    /// the wrapping instead of doubling up on indentation.</summary>
    private Doc RenderStatementLevelExpressionSuffix(ExpressionSyntax expr) =>
        IsBlockLikeRhs(expr) ? Concat(" ", VisitR(expr)) : Group(Indent(Line, VisitR(expr)));

    // =========================================================================================
    // try / catch / finally
    // =========================================================================================

    public override Doc VisitTryStatement(TryStatementSyntax node)
    {
        var parts = new List<Doc> { "try", HardLine, VisitR(node.Block) };

        foreach (var catchClause in node.Catches)
            parts.Add(Concat(HardLine, WithTrivia(catchClause, VisitR(catchClause))));

        if (node.Finally != null)
            parts.Add(Concat(HardLine, WithTrivia(node.Finally, VisitR(node.Finally))));

        return Concat(parts.ToArray());
    }

    public override Doc VisitCatchClause(CatchClauseSyntax node)
    {
        var declaration =
            node.Declaration != null ? Concat(" (", VisitR(node.Declaration), ")") : (Doc)"";
        var filter = node.Filter != null ? Concat(" ", VisitR(node.Filter)) : (Doc)"";
        return Concat("catch", declaration, filter, HardLine, VisitR(node.Block));
    }

    public override Doc VisitCatchDeclaration(CatchDeclarationSyntax node) =>
        Concat(
            VisitR(node.Type),
            node.Identifier.IsKind(SyntaxKind.None) ? "" : Concat(" ", node.Identifier.Text)
        );

    public override Doc VisitCatchFilterClause(CatchFilterClauseSyntax node) =>
        Concat("when (", Group(Indent(SoftLine, VisitR(node.FilterExpression)), SoftLine), ")");

    public override Doc VisitFinallyClause(FinallyClauseSyntax node) =>
        Concat("finally", HardLine, VisitR(node.Block));

    // =========================================================================================
    // lock / using / fixed / checked / unchecked / unsafe
    // =========================================================================================

    public override Doc VisitLockStatement(LockStatementSyntax node)
    {
        var header = Concat("lock (", RenderParenthesizedCondition(node.Expression), ")");
        return Concat(header, RenderEmbeddedStatement(node.Statement));
    }

    public override Doc VisitUsingStatement(UsingStatementSyntax node)
    {
        var awaitPrefix = node.AwaitKeyword.IsKind(SyntaxKind.None) ? "" : "await ";
        Doc resource =
            node.Declaration != null
                ? RenderVariableDeclaration(node.Declaration)
                : VisitR(node.Expression!);

        var header = Concat(
            awaitPrefix,
            "using (",
            Group(Indent(SoftLine, resource), SoftLine),
            ")"
        );
        return Concat(header, RenderEmbeddedStatement(node.Statement));
    }

    public override Doc VisitFixedStatement(FixedStatementSyntax node)
    {
        var header = Concat("fixed (", RenderVariableDeclaration(node.Declaration), ")");
        return Concat(header, RenderEmbeddedStatement(node.Statement));
    }

    public override Doc VisitCheckedStatement(CheckedStatementSyntax node) =>
        Concat(node.Keyword.Text, HardLine, VisitR(node.Block));

    public override Doc VisitUnsafeStatement(UnsafeStatementSyntax node) =>
        Concat("unsafe", HardLine, VisitR(node.Block));

    // ============================================================
    // (originally in CSharpDocVisitor.Expressions.cs)
    // ============================================================
    // =========================================================================================
    // Shared expression-level helpers
    // =========================================================================================

    /// <summary>RHS expression kinds whose own printed form starts with a header that must stay
    /// glued to whatever precedes it (an "=", "return", etc.) - object/array/collection creation
    /// with an initializer, anonymous objects, lambdas, anonymous methods and switch expressions
    /// all manage *their own* internal line breaking (e.g. moving just the "{" down), so the
    /// assignment/return/local-declaration machinery must not additionally try to move the whole
    /// expression onto its own indented line.</summary>
    private static bool IsBlockLikeRhs(ExpressionSyntax expr) =>
        expr switch
        {
            ObjectCreationExpressionSyntax { Initializer: not null } => true,
            ImplicitObjectCreationExpressionSyntax { Initializer: not null } => true,
            ArrayCreationExpressionSyntax { Initializer: not null } => true,
            ImplicitArrayCreationExpressionSyntax => true,
            AnonymousObjectCreationExpressionSyntax => true,
            CollectionExpressionSyntax => true,
            SimpleLambdaExpressionSyntax => true,
            ParenthesizedLambdaExpressionSyntax => true,
            AnonymousMethodExpressionSyntax => true,
            SwitchExpressionSyntax => true,
            _ => false,
        };

    private Doc RenderAssignmentRhs(ExpressionSyntax expr) =>
        IsBlockLikeRhs(expr) ? Concat(" ", VisitR(expr)) : Indent(Line, VisitR(expr));

    /// <summary>True if there is a line break between token `a` and the start of `b` in the
    /// original source. Used to decide whether an initializer/collection-expression body should
    /// be forced multi-line, mirroring Prettier/CSharpier's "preserve whether the author put a
    /// break after the opening brace" heuristic rather than an arbitrary element-count cutoff.</summary>
    private static bool HasLineBreakBetween(SyntaxToken a, SyntaxNodeOrToken b)
    {
        if (a.IsKind(SyntaxKind.None))
            return false;
        var lineA = a.GetLocation().GetLineSpan().EndLinePosition.Line;
        var lineB = b.GetLocation().GetLineSpan().StartLinePosition.Line;
        return lineB > lineA;
    }

    /// <summary>Glues `header` (e.g. "new Point()") to a brace-delimited initializer, keeping the
    /// opening brace on the same line when the initializer collapses to one line and moving it to
    /// its own Allman-style line only when the initializer actually ends up broken - the brace's
    /// placement is tied to the initializer's own group via a generated group id, since the
    /// connector is a sibling *before* that group, not inside it.</summary>
    private Doc RenderWithInitializer(Doc header, InitializerExpressionSyntax initializer)
    {
        var groupId = "init_" + Guid.NewGuid().ToString("N");
        var group = BuildInitializerGroup(initializer, groupId);
        return Concat(header, IfBreak(HardLine, " ", groupId), group);
    }

    private Doc BuildInitializerGroup(InitializerExpressionSyntax node, string? groupId)
    {
        if (node.Expressions.Count == 0)
            return "{ }";

        var forceBreak = HasLineBreakBetween(node.OpenBraceToken, node.Expressions[0]);
        var hadTrailingComma = node.Expressions.SeparatorCount == node.Expressions.Count;
        var elements = node.Expressions.Select(e => WithTrivia(e, VisitR(e))).ToList();
        var body = Concat(
            Join(Concat(",", Line), elements),
            hadTrailingComma ? IfBreak(",") : (Doc)""
        );

        return Group(
            Concat("{", Indent(Line, body), Line, "}"),
            shouldBreak: forceBreak,
            id: groupId
        );
    }

    public override Doc VisitInitializerExpression(InitializerExpressionSyntax node) =>
        BuildInitializerGroup(node, null);

    // =========================================================================================
    // Literals / names / this / base - see also CSharpDocVisitor.TypesAndMisc.cs for the plain
    // type-position visitors (IdentifierName, GenericName, PredefinedType, ...) which are shared
    // between type syntax and expression syntax since Roslyn reuses the same node kinds for both.
    // =========================================================================================

    public override Doc VisitLiteralExpression(LiteralExpressionSyntax node) =>
        FromRawText(node.Token.Text);

    public override Doc VisitThisExpression(ThisExpressionSyntax node) => "this";

    public override Doc VisitBaseExpression(BaseExpressionSyntax node) => "base";

    public override Doc VisitParenthesizedExpression(ParenthesizedExpressionSyntax node) =>
        Concat("(", VisitR(node.Expression), ")");

    // =========================================================================================
    // Member access / invocation / element access / conditional access
    // =========================================================================================

    public override Doc VisitMemberAccessExpression(MemberAccessExpressionSyntax node) =>
        Concat(VisitR(node.Expression), node.OperatorToken.Text, VisitR(node.Name));

    public override Doc VisitInvocationExpression(InvocationExpressionSyntax node)
    {
        var (root, links) = UnrollInvocationChain(node);
        var callLinkCount = links.Count(l => l.IsCall);

        // Only special-case genuine multi-call fluent chains (a.B().C().D()...); anything shorter
        // just renders inline, letting the surrounding context's own Group decide whether the
        // whole call needs to wrap via its argument list. This is a deliberate simplification of
        // CSharpier's considerably more elaborate method-chain-breaking heuristics - see the
        // written response's limitations section.
        if (root == null || callLinkCount < 3)
            return Concat(VisitR(node.Expression), RenderArgumentList(node.ArgumentList));

        var rootDoc = VisitR(root);
        var chained = Concat(links.Select(l => Concat(SoftLine, l.Doc)).ToArray());
        return Group(rootDoc, Indent(chained));
    }

    private readonly record struct ChainLink(bool IsCall, Doc Doc);

    private (ExpressionSyntax? Root, List<ChainLink> Links) UnrollInvocationChain(
        ExpressionSyntax expr
    )
    {
        var links = new List<ChainLink>();
        var current = expr;

        while (true)
        {
            if (
                current is InvocationExpressionSyntax inv
                && inv.Expression is MemberAccessExpressionSyntax ma
            )
            {
                links.Insert(
                    0,
                    new ChainLink(
                        true,
                        Concat(
                            ma.OperatorToken.Text,
                            VisitR(ma.Name),
                            RenderArgumentList(inv.ArgumentList)
                        )
                    )
                );
                current = ma.Expression;
                continue;
            }

            if (current is MemberAccessExpressionSyntax plainMember)
            {
                links.Insert(
                    0,
                    new ChainLink(
                        false,
                        Concat(plainMember.OperatorToken.Text, VisitR(plainMember.Name))
                    )
                );
                current = plainMember.Expression;
                continue;
            }

            break;
        }

        return (current, links);
    }

    public override Doc VisitElementAccessExpression(ElementAccessExpressionSyntax node) =>
        Concat(VisitR(node.Expression), RenderBracketedArgumentList(node.ArgumentList));

    /// <summary>`["a"] = value` inside an object initializer (index initializer syntax) - the
    /// `["a"]` part on its own, with no receiver expression before it, is a distinct node kind
    /// from a normal element-access.</summary>
    public override Doc VisitImplicitElementAccess(ImplicitElementAccessSyntax node) =>
        RenderBracketedArgumentList(node.ArgumentList);

    public override Doc VisitConditionalAccessExpression(ConditionalAccessExpressionSyntax node) =>
        Concat(VisitR(node.Expression), "?", VisitR(node.WhenNotNull));

    public override Doc VisitMemberBindingExpression(MemberBindingExpressionSyntax node) =>
        Concat(".", VisitR(node.Name));

    public override Doc VisitElementBindingExpression(ElementBindingExpressionSyntax node) =>
        RenderBracketedArgumentList(node.ArgumentList);

    // =========================================================================================
    // Argument lists
    // =========================================================================================

    private Doc RenderArgumentList(ArgumentListSyntax node)
    {
        if (node.Arguments.Count == 0)
            return "()";
        var args = node.Arguments.Select(a => WithTrivia(a, VisitR(a))).ToList();
        return Group("(", Indent(SoftLine, Join(Concat(",", Line), args)), SoftLine, ")");
    }

    public override Doc VisitArgumentList(ArgumentListSyntax node) => RenderArgumentList(node);

    private Doc RenderBracketedArgumentList(BracketedArgumentListSyntax node)
    {
        var args = node.Arguments.Select(a => WithTrivia(a, VisitR(a))).ToList();
        if (args.Count == 0)
            return "[]";
        return Group("[", Indent(SoftLine, Join(Concat(",", Line), args)), SoftLine, "]");
    }

    public override Doc VisitBracketedArgumentList(BracketedArgumentListSyntax node) =>
        RenderBracketedArgumentList(node);

    public override Doc VisitArgument(ArgumentSyntax node)
    {
        var nameColon =
            node.NameColon != null ? Concat(node.NameColon.Name.Identifier.Text, ": ") : (Doc)"";
        var refKind = node.RefKindKeyword.IsKind(SyntaxKind.None)
            ? ""
            : node.RefKindKeyword.Text + " ";
        return Concat(nameColon, refKind, VisitR(node.Expression));
    }

    // =========================================================================================
    // Object / array / collection creation
    // =========================================================================================

    public override Doc VisitObjectCreationExpression(ObjectCreationExpressionSyntax node)
    {
        var args = node.ArgumentList != null ? RenderArgumentList(node.ArgumentList) : "()";
        var header = Concat("new ", VisitR(node.Type), args);
        return node.Initializer == null ? header : RenderWithInitializer(header, node.Initializer);
    }

    public override Doc VisitImplicitObjectCreationExpression(
        ImplicitObjectCreationExpressionSyntax node
    )
    {
        var header = Concat("new", RenderArgumentList(node.ArgumentList));
        return node.Initializer == null ? header : RenderWithInitializer(header, node.Initializer);
    }

    public override Doc VisitArrayCreationExpression(ArrayCreationExpressionSyntax node)
    {
        var header = Concat("new ", VisitR(node.Type));
        return node.Initializer == null ? header : RenderWithInitializer(header, node.Initializer);
    }

    public override Doc VisitImplicitArrayCreationExpression(
        ImplicitArrayCreationExpressionSyntax node
    )
    {
        var commas = new string(',', node.Commas.Count);
        var header = Concat("new[", commas, "]");
        return RenderWithInitializer(header, node.Initializer);
    }

    public override Doc VisitStackAllocArrayCreationExpression(
        StackAllocArrayCreationExpressionSyntax node
    )
    {
        var header = Concat("stackalloc ", VisitR(node.Type));
        return node.Initializer == null ? header : RenderWithInitializer(header, node.Initializer);
    }

    public override Doc VisitImplicitStackAllocArrayCreationExpression(
        ImplicitStackAllocArrayCreationExpressionSyntax node
    ) => RenderWithInitializer("stackalloc []", node.Initializer);

    public override Doc VisitAnonymousObjectCreationExpression(
        AnonymousObjectCreationExpressionSyntax node
    )
    {
        if (node.Initializers.Count == 0)
            return "new { }";

        var forceBreak = HasLineBreakBetween(node.OpenBraceToken, node.Initializers[0]);
        var hadTrailingComma = node.Initializers.SeparatorCount == node.Initializers.Count;
        var elements = node.Initializers.Select(e => WithTrivia(e, VisitR(e))).ToList();
        var body = Concat(
            Join(Concat(",", Line), elements),
            hadTrailingComma ? IfBreak(",") : (Doc)""
        );

        return Concat(
            "new ",
            Group(Concat("{", Indent(Line, body), Line, "}"), shouldBreak: forceBreak)
        );
    }

    public override Doc VisitAnonymousObjectMemberDeclarator(
        AnonymousObjectMemberDeclaratorSyntax node
    ) =>
        node.NameEquals != null
            ? Concat(node.NameEquals.Name.Identifier.Text, " = ", VisitR(node.Expression))
            : VisitR(node.Expression);

    public override Doc VisitCollectionExpression(CollectionExpressionSyntax node)
    {
        if (node.Elements.Count == 0)
            return "[]";

        var forceBreak = HasLineBreakBetween(node.OpenBracketToken, node.Elements[0]);
        var hadTrailingComma = node.Elements.SeparatorCount == node.Elements.Count;
        var elements = node.Elements.Select(e => WithTrivia(e, VisitR(e))).ToList();
        var body = Concat(
            Join(Concat(",", Line), elements),
            hadTrailingComma ? IfBreak(",") : (Doc)""
        );

        return Group(Concat("[", Indent(Line, body), Line, "]"), shouldBreak: forceBreak);
    }

    public override Doc VisitExpressionElement(ExpressionElementSyntax node) =>
        VisitR(node.Expression);

    public override Doc VisitSpreadElement(SpreadElementSyntax node) =>
        Concat("..", VisitR(node.Expression));

    // =========================================================================================
    // Operators
    // =========================================================================================

    private static int GetBinaryPrecedence(string op) =>
        op switch
        {
            "*" or "/" or "%" => 11,
            "+" or "-" => 10,
            "<<" or ">>" or ">>>" => 9,
            "<" or ">" or "<=" or ">=" or "is" or "as" => 8,
            "==" or "!=" => 7,
            "&" => 6,
            "^" => 5,
            "|" => 4,
            "&&" => 3,
            "||" => 2,
            "??" => 1,
            _ => -1,
        };

    public override Doc VisitBinaryExpression(BinaryExpressionSyntax node)
    {
        var opText = node.OperatorToken.Text;
        if (opText is "is" or "as")
            return Concat(VisitR(node.Left), " ", opText, " ", VisitR(node.Right));

        return RenderBinaryChain(node);
    }

    /// <summary>
    /// Flattens a run of same-precedence binary operators into one Doc.Group instead of nesting
    /// an Indent per level, so `a + b + c + d` prints as:
    ///   a + b + c + d                  (fits)
    ///   a
    ///       + b
    ///       + c
    ///       + d                       (doesn't fit)
    /// rather than progressively-deeper-indented nested groups. A right-hand operand that is
    /// itself a *different*-precedence binary expression is rendered through the normal Visit
    /// dispatch, which recurses into VisitBinaryExpression again and produces its own,
    /// independently-breakable nested Group - this is what gives correct "Group A / Group B"
    /// nesting for e.g. `a && b || c`.
    /// </summary>
    private Doc RenderBinaryChain(BinaryExpressionSyntax node)
    {
        var precedence = GetBinaryPrecedence(node.OperatorToken.Text);
        var rightAssociative = node.OperatorToken.Text == "??";

        var operands = new List<Doc>();
        var operators = new List<string>();

        if (rightAssociative)
        {
            ExpressionSyntax current = node;
            while (
                current is BinaryExpressionSyntax bin
                && GetBinaryPrecedence(bin.OperatorToken.Text) == precedence
            )
            {
                operands.Add(VisitR(bin.Left));
                operators.Add(bin.OperatorToken.Text);
                current = bin.Right;
            }
            operands.Add(VisitR(current));
        }
        else
        {
            var stack = new Stack<(string Op, ExpressionSyntax Operand)>();
            ExpressionSyntax current = node;
            while (
                current is BinaryExpressionSyntax bin
                && GetBinaryPrecedence(bin.OperatorToken.Text) == precedence
            )
            {
                stack.Push((bin.OperatorToken.Text, bin.Right));
                current = bin.Left;
            }
            operands.Add(VisitR(current));
            while (stack.Count > 0)
            {
                var (op, operand) = stack.Pop();
                operators.Add(op);
                operands.Add(VisitR(operand));
            }
        }

        var continuation = new List<Doc>();
        for (var i = 0; i < operators.Count; i++)
        {
            continuation.Add(Line);
            continuation.Add(operators[i]);
            continuation.Add(" ");
            continuation.Add(operands[i + 1]);
        }

        return Group(Concat(operands[0], Indent(Concat(continuation.ToArray()))));
    }

    public override Doc VisitPrefixUnaryExpression(PrefixUnaryExpressionSyntax node) =>
        Concat(node.OperatorToken.Text, VisitR(node.Operand));

    public override Doc VisitPostfixUnaryExpression(PostfixUnaryExpressionSyntax node) =>
        Concat(VisitR(node.Operand), node.OperatorToken.Text);

    public override Doc VisitAssignmentExpression(AssignmentExpressionSyntax node) =>
        Group(VisitR(node.Left), " ", node.OperatorToken.Text, RenderAssignmentRhs(node.Right));

    public override Doc VisitConditionalExpression(ConditionalExpressionSyntax node) =>
        Group(
            VisitR(node.Condition),
            Indent(Line, "? ", VisitR(node.WhenTrue), Line, ": ", VisitR(node.WhenFalse))
        );

    public override Doc VisitCastExpression(CastExpressionSyntax node) =>
        Concat("(", VisitR(node.Type), ")", VisitR(node.Expression));

    public override Doc VisitRefExpression(RefExpressionSyntax node) =>
        Concat("ref ", VisitR(node.Expression));

    public override Doc VisitCheckedExpression(CheckedExpressionSyntax node) =>
        Concat(node.Keyword.Text, "(", VisitR(node.Expression), ")");

    public override Doc VisitDefaultExpression(DefaultExpressionSyntax node) =>
        Concat("default(", VisitR(node.Type), ")");

    public override Doc VisitTypeOfExpression(TypeOfExpressionSyntax node) =>
        Concat("typeof(", VisitR(node.Type), ")");

    public override Doc VisitSizeOfExpression(SizeOfExpressionSyntax node) =>
        Concat("sizeof(", VisitR(node.Type), ")");

    public override Doc VisitThrowExpression(ThrowExpressionSyntax node) =>
        Concat("throw ", VisitR(node.Expression));

    public override Doc VisitAwaitExpression(AwaitExpressionSyntax node) =>
        Concat("await ", VisitR(node.Expression));

    public override Doc VisitRangeExpression(RangeExpressionSyntax node) =>
        Concat(
            node.LeftOperand != null ? VisitR(node.LeftOperand) : "",
            "..",
            node.RightOperand != null ? VisitR(node.RightOperand) : ""
        );

    public override Doc VisitWithExpression(WithExpressionSyntax node) =>
        Concat(VisitR(node.Expression), " with ", BuildInitializerGroup(node.Initializer, null));

    // =========================================================================================
    // Declaration expressions / designations (out var x, deconstruction)
    // =========================================================================================

    public override Doc VisitDeclarationExpression(DeclarationExpressionSyntax node) =>
        Concat(VisitR(node.Type), " ", VisitR(node.Designation));

    public override Doc VisitSingleVariableDesignation(SingleVariableDesignationSyntax node) =>
        node.Identifier.Text;

    public override Doc VisitDiscardDesignation(DiscardDesignationSyntax node) => "_";

    public override Doc VisitParenthesizedVariableDesignation(
        ParenthesizedVariableDesignationSyntax node
    ) => Concat("(", Join(", ", node.Variables.Select(v => VisitR(v))), ")");

    // =========================================================================================
    // Tuples
    // =========================================================================================

    public override Doc VisitTupleExpression(TupleExpressionSyntax node)
    {
        var elements = node.Arguments.Select(a => VisitR(a)).ToList();
        return Group("(", Indent(SoftLine, Join(Concat(",", Line), elements)), SoftLine, ")");
    }

    // =========================================================================================
    // Interpolated strings
    // =========================================================================================

    public override Doc VisitInterpolatedStringExpression(
        InterpolatedStringExpressionSyntax node
    ) =>
        Concat(
            node.StringStartToken.Text,
            Concat(node.Contents.Select(c => VisitR(c)).ToArray()),
            node.StringEndToken.Text
        );

    public override Doc VisitInterpolatedStringText(InterpolatedStringTextSyntax node) =>
        FromRawText(node.TextToken.Text);

    public override Doc VisitInterpolation(InterpolationSyntax node)
    {
        var alignment =
            node.AlignmentClause != null
                ? Concat(",", VisitR(node.AlignmentClause.Value))
                : (Doc)"";
        var format =
            node.FormatClause != null
                ? Concat(":", FromRawText(node.FormatClause.FormatStringToken.Text))
                : (Doc)"";
        return Concat("{", VisitR(node.Expression), alignment, format, "}");
    }

    // =========================================================================================
    // Lambdas / anonymous methods
    // =========================================================================================

    public override Doc VisitSimpleLambdaExpression(SimpleLambdaExpressionSyntax node)
    {
        var modifiers = RenderLambdaModifiers(node.Modifiers);
        var header = Concat(modifiers, VisitR(node.Parameter), " =>");
        return RenderLambdaBody(header, node.Body);
    }

    public override Doc VisitParenthesizedLambdaExpression(ParenthesizedLambdaExpressionSyntax node)
    {
        var modifiers = RenderLambdaModifiers(node.Modifiers);
        var returnType = node.ReturnType != null ? Concat(VisitR(node.ReturnType), " ") : (Doc)"";
        var header = Concat(modifiers, returnType, RenderParameterList(node.ParameterList), " =>");
        return RenderLambdaBody(header, node.Body);
    }

    public override Doc VisitAnonymousMethodExpression(AnonymousMethodExpressionSyntax node)
    {
        var modifiers = RenderLambdaModifiers(node.Modifiers);
        var parameters =
            node.ParameterList != null ? RenderParameterList(node.ParameterList) : (Doc)"";
        return Concat(modifiers, "delegate", parameters, " ", VisitR(node.Block));
    }

    private static Doc RenderLambdaModifiers(SyntaxTokenList modifiers) =>
        modifiers.Count == 0 ? "" : Concat(string.Join(" ", modifiers.Select(m => m.Text)), " ");

    /// <summary>Lambda bodies keep their opening brace glued to "=>" on the same line (verified
    /// against CSharpier's own changelog example: `x => { return x; }`) rather than the Allman
    /// placement used for type/method declarations.</summary>
    private Doc RenderLambdaBody(Doc header, CSharpSyntaxNode body)
    {
        if (body is BlockSyntax block)
            return Concat(header, " ", VisitR(block));

        return Concat(header, Group(Indent(Line, VisitR(body))));
    }

    // =========================================================================================
    // Query expressions
    // =========================================================================================

    public override Doc VisitQueryExpression(QueryExpressionSyntax node) =>
        Group(VisitR(node.FromClause), Indent(VisitR(node.Body)));

    public override Doc VisitQueryBody(QueryBodySyntax node)
    {
        var clauses = node.Clauses.Select(c => Concat(HardLine, VisitR(c))).ToArray();
        var selectOrGroup = Concat(HardLine, VisitR(node.SelectOrGroup));
        var continuation =
            node.Continuation != null ? Concat(HardLine, VisitR(node.Continuation)) : (Doc)"";
        return Concat(Concat(clauses), selectOrGroup, continuation);
    }

    public override Doc VisitFromClause(FromClauseSyntax node) =>
        Concat(
            "from ",
            node.Type != null ? Concat(VisitR(node.Type), " ") : (Doc)"",
            node.Identifier.Text,
            " in ",
            VisitR(node.Expression)
        );

    public override Doc VisitLetClause(LetClauseSyntax node) =>
        Concat("let ", node.Identifier.Text, " = ", VisitR(node.Expression));

    public override Doc VisitWhereClause(WhereClauseSyntax node) =>
        Concat("where ", VisitR(node.Condition));

    public override Doc VisitJoinClause(JoinClauseSyntax node)
    {
        var into = node.Into != null ? Concat(" into ", node.Into.Identifier.Text) : (Doc)"";
        return Concat(
            "join ",
            node.Type != null ? Concat(VisitR(node.Type), " ") : (Doc)"",
            node.Identifier.Text,
            " in ",
            VisitR(node.InExpression),
            " on ",
            VisitR(node.LeftExpression),
            " equals ",
            VisitR(node.RightExpression),
            into
        );
    }

    public override Doc VisitOrderByClause(OrderByClauseSyntax node) =>
        Concat("orderby ", Join(", ", node.Orderings.Select(o => VisitR(o))));

    public override Doc VisitOrdering(OrderingSyntax node) =>
        Concat(
            VisitR(node.Expression),
            node.AscendingOrDescendingKeyword.IsKind(SyntaxKind.None)
                ? ""
                : " " + node.AscendingOrDescendingKeyword.Text
        );

    public override Doc VisitSelectClause(SelectClauseSyntax node) =>
        Concat("select ", VisitR(node.Expression));

    public override Doc VisitGroupClause(GroupClauseSyntax node) =>
        Concat("group ", VisitR(node.GroupExpression), " by ", VisitR(node.ByExpression));

    public override Doc VisitQueryContinuation(QueryContinuationSyntax node) =>
        Concat("into ", node.Identifier.Text, Indent(VisitR(node.Body)));

    // ============================================================
    // (originally in CSharpDocVisitor.Patterns.cs)
    // ============================================================
    public override Doc VisitIsPatternExpression(IsPatternExpressionSyntax node) =>
        Concat(VisitR(node.Expression), " is ", VisitR(node.Pattern));

    public override Doc VisitDeclarationPattern(DeclarationPatternSyntax node) =>
        Concat(VisitR(node.Type), " ", VisitR(node.Designation));

    public override Doc VisitConstantPattern(ConstantPatternSyntax node) => VisitR(node.Expression);

    public override Doc VisitDiscardPattern(DiscardPatternSyntax node) => "_";

    public override Doc VisitVarPattern(VarPatternSyntax node) =>
        Concat("var ", VisitR(node.Designation));

    public override Doc VisitTypePattern(TypePatternSyntax node) => VisitR(node.Type);

    public override Doc VisitRelationalPattern(RelationalPatternSyntax node) =>
        Concat(node.OperatorToken.Text, " ", VisitR(node.Expression));

    /// <summary>Unlike binary expressions, "and"/"or" pattern chains are not flattened into a
    /// single-level Group - patterns are rarely long enough in practice for that extra complexity
    /// to earn its keep, so this is a straightforward recursive nesting instead.</summary>
    public override Doc VisitBinaryPattern(BinaryPatternSyntax node) =>
        Group(VisitR(node.Left), Indent(Line, node.OperatorToken.Text, " ", VisitR(node.Right)));

    public override Doc VisitUnaryPattern(UnaryPatternSyntax node) =>
        Concat("not ", VisitR(node.Pattern));

    public override Doc VisitParenthesizedPattern(ParenthesizedPatternSyntax node) =>
        Concat("(", VisitR(node.Pattern), ")");

    public override Doc VisitRecursivePattern(RecursivePatternSyntax node)
    {
        var type = node.Type != null ? VisitR(node.Type) : (Doc)"";
        var positional =
            node.PositionalPatternClause != null ? VisitR(node.PositionalPatternClause) : (Doc)"";
        var property =
            node.PropertyPatternClause != null ? VisitR(node.PropertyPatternClause) : (Doc)"";
        var designation =
            node.Designation != null ? Concat(" ", VisitR(node.Designation)) : (Doc)"";
        return Concat(type, positional, property, designation);
    }

    public override Doc VisitPositionalPatternClause(PositionalPatternClauseSyntax node)
    {
        var subs = node.Subpatterns.Select(s => VisitR(s)).ToList();
        if (subs.Count == 0)
            return "()";
        return Group("(", Indent(SoftLine, Join(Concat(",", Line), subs)), SoftLine, ")");
    }

    public override Doc VisitPropertyPatternClause(PropertyPatternClauseSyntax node)
    {
        if (node.Subpatterns.Count == 0)
            return " { }";
        var subs = node.Subpatterns.Select(s => WithTrivia(s, VisitR(s))).ToList();
        return Group(" {", Indent(Line, Join(Concat(",", Line), subs)), Line, "}");
    }

    public override Doc VisitSubpattern(SubpatternSyntax node) =>
        node.NameColon != null
            ? Concat(node.NameColon.Name.Identifier.Text, ": ", VisitR(node.Pattern))
            : VisitR(node.Pattern);

    public override Doc VisitListPattern(ListPatternSyntax node)
    {
        var designation =
            node.Designation != null ? Concat(" ", VisitR(node.Designation)) : (Doc)"";
        if (node.Patterns.Count == 0)
            return Concat("[]", designation);
        var patterns = node.Patterns.Select(p => VisitR(p)).ToList();
        return Concat(
            Group("[", Indent(SoftLine, Join(Concat(",", Line), patterns)), SoftLine, "]"),
            designation
        );
    }

    public override Doc VisitSlicePattern(SlicePatternSyntax node) =>
        node.Pattern != null ? Concat("..", VisitR(node.Pattern)) : "..";

    // =========================================================================================
    // Switch expressions
    // =========================================================================================

    public override Doc VisitSwitchExpression(SwitchExpressionSyntax node)
    {
        var header = Concat(VisitR(node.GoverningExpression), " switch");

        if (node.Arms.Count == 0)
            return Concat(header, " { }");

        var hadTrailingComma = node.Arms.SeparatorCount == node.Arms.Count;
        var arms = node.Arms.Select(a => WithTrivia(a, VisitR(a))).ToList();
        var body = Concat(Join(Concat(",", HardLine), arms), hadTrailingComma ? "," : "");

        return Concat(header, HardLine, "{", Indent(HardLine, body), HardLine, "}");
    }

    public override Doc VisitSwitchExpressionArm(SwitchExpressionArmSyntax node)
    {
        var when = node.WhenClause != null ? Concat(" ", VisitR(node.WhenClause)) : (Doc)"";
        return Group(VisitR(node.Pattern), when, Indent(Line, "=> ", VisitR(node.Expression)));
    }

    public override Doc VisitWhenClause(WhenClauseSyntax node) =>
        Concat("when ", VisitR(node.Condition));

    public override Doc VisitCasePatternSwitchLabel(CasePatternSwitchLabelSyntax node)
    {
        var when = node.WhenClause != null ? Concat(" ", VisitR(node.WhenClause)) : (Doc)"";
        return Concat("case ", VisitR(node.Pattern), when, ":");
    }

    // ============================================================
    // (originally in CSharpDocVisitor.TypesAndMisc.cs)
    // ============================================================
    // IdentifierNameSyntax is used by Roslyn both as a type reference (`Foo x`) and as a plain
    // expression (`x = Foo`) - one visitor covers both uses.
    public override Doc VisitIdentifierName(IdentifierNameSyntax node) => node.Identifier.Text;

    public override Doc VisitGenericName(GenericNameSyntax node) =>
        Concat(node.Identifier.Text, VisitR(node.TypeArgumentList));

    public override Doc VisitTypeArgumentList(TypeArgumentListSyntax node)
    {
        var args = node.Arguments.Select(a => VisitR(a)).ToList();
        return Group("<", Indent(SoftLine, Join(Concat(",", Line), args)), SoftLine, ">");
    }

    public override Doc VisitOmittedTypeArgument(OmittedTypeArgumentSyntax node) => "";

    public override Doc VisitQualifiedName(QualifiedNameSyntax node) =>
        Concat(VisitR(node.Left), ".", VisitR(node.Right));

    public override Doc VisitAliasQualifiedName(AliasQualifiedNameSyntax node) =>
        Concat(VisitR(node.Alias), "::", VisitR(node.Name));

    public override Doc VisitPredefinedType(PredefinedTypeSyntax node) => node.Keyword.Text;

    public override Doc VisitNullableType(NullableTypeSyntax node) =>
        Concat(VisitR(node.ElementType), "?");

    public override Doc VisitArrayType(ArrayTypeSyntax node) =>
        Concat(
            VisitR(node.ElementType),
            Concat(node.RankSpecifiers.Select(r => VisitR(r)).ToArray())
        );

    public override Doc VisitArrayRankSpecifier(ArrayRankSpecifierSyntax node)
    {
        // A bare, unsized rank marker (`int[,]`, part of an array *type*) never has spaces
        // around its commas; an array *creation* with explicit sizes (`new int[3, 3]`) does.
        var allOmitted = node.Sizes.All(s => s is OmittedArraySizeExpressionSyntax);
        if (allOmitted)
            return Concat("[", new string(',', node.Sizes.Count - 1), "]");

        var sizes = node.Sizes.Select(s => VisitR(s)).ToList();
        return Concat("[", Join(", ", sizes), "]");
    }

    public override Doc VisitOmittedArraySizeExpression(OmittedArraySizeExpressionSyntax node) =>
        "";

    public override Doc VisitPointerType(PointerTypeSyntax node) =>
        Concat(VisitR(node.ElementType), "*");

    public override Doc VisitRefType(RefTypeSyntax node)
    {
        var kind = node.ReadOnlyKeyword.IsKind(SyntaxKind.None) ? "ref " : "ref readonly ";
        return Concat(kind, VisitR(node.Type));
    }

    public override Doc VisitTupleType(TupleTypeSyntax node)
    {
        var elements = node.Elements.Select(e => VisitR(e)).ToList();
        return Group("(", Indent(SoftLine, Join(Concat(",", Line), elements)), SoftLine, ")");
    }

    public override Doc VisitTupleElement(TupleElementSyntax node) =>
        node.Identifier.IsKind(SyntaxKind.None)
            ? VisitR(node.Type)
            : Concat(VisitR(node.Type), " ", node.Identifier.Text);

    // Function pointer types (`delegate* <int, int>`, `delegate* unmanaged[Cdecl]<int, void>`)
    // are given reduced-fidelity but functional support: parameter attributes/modifiers beyond
    // the bare type are not reproduced. This is a genuinely obscure corner of the language: flag
    // it explicitly rather than silently dropping detail - see "Remaining limitations".
    public override Doc VisitFunctionPointerType(FunctionPointerTypeSyntax node)
    {
        var callingConvention =
            node.CallingConvention != null ? Concat(" ", VisitR(node.CallingConvention)) : (Doc)"";
        var parameterTypes = node.ParameterList.Parameters.Select(p => VisitR(p.Type)).ToList();
        return Concat("delegate*", callingConvention, "<", Join(", ", parameterTypes), ">");
    }

    public override Doc VisitFunctionPointerCallingConvention(
        FunctionPointerCallingConventionSyntax node
    )
    {
        var unmanagedList =
            node.UnmanagedCallingConventionList != null
                ? Concat(
                    "[",
                    Join(
                        ", ",
                        node.UnmanagedCallingConventionList.CallingConventions.Select(c =>
                            (Doc)c.Name.Text
                        )
                    ),
                    "]"
                )
                : (Doc)"";
        return Concat(node.ManagedOrUnmanagedKeyword.Text, unmanagedList);
    }
}

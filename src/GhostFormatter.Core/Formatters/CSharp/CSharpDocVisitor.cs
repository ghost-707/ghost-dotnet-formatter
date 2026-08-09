using System.Collections.Generic;
using System.Linq;
using GhostFormatter.Core.Docs;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static GhostFormatter.Core.Docs.Docs;

namespace GhostFormatter.Core.Formatters.CSharp;

public class CSharpDocVisitor : CSharpSyntaxVisitor<Doc>
{
    public override Doc VisitCompilationUnit(CompilationUnitSyntax node)
    {
        var usings = node.Usings.Select(Visit).Where(d => d != null).ToList();
        var members = node.Members.Select(Visit).Where(d => d != null).ToList();

        var parts = new List<Doc>();
        if (usings.Count > 0)
        {
            parts.Add(Join(HardLine, usings));
            parts.Add(HardLine);
            parts.Add(HardLine);
        }
        parts.Add(Join(Concat(HardLine, HardLine), members));

        return Concat(parts.ToArray());
    }

    public override Doc VisitUsingDirective(UsingDirectiveSyntax node)
    {
        return Concat("using ", node.Name.ToString(), ";");
    }

    public override Doc VisitNamespaceDeclaration(NamespaceDeclarationSyntax node)
    {
        var name = node.Name.ToString();
        var members = node.Members.Select(Visit).Where(d => d != null).ToList();

        return Concat(
            "namespace ",
            name,
            HardLine,
            "{",
            Indent(HardLine, Join(Concat(HardLine, HardLine), members)),
            HardLine,
            "}"
        );
    }

    public override Doc VisitClassDeclaration(ClassDeclarationSyntax node)
    {
        var modifiers = node.Modifiers.ToString();
        var name = node.Identifier.Text;
        var bases = node.BaseList != null ? Concat(" : ", node.BaseList.Types.ToString()) : "";
        var members = node.Members.Select(Visit).Where(d => d != null).ToList();

        return Concat(
            modifiers.Length > 0 ? modifiers + " " : "",
            "class ",
            name,
            bases,
            HardLine,
            "{",
            Indent(HardLine, Join(Concat(HardLine, HardLine), members)),
            HardLine,
            "}"
        );
    }

    // HANDLES: Property Declarations (e.g. public string ToWallet { set; get; })
    public override Doc VisitPropertyDeclaration(PropertyDeclarationSyntax node)
    {
        var attributes = node.AttributeLists.Select(Visit).ToList();
        var modifiers = node.Modifiers.ToString();
        var type = node.Type.ToString();
        var name = node.Identifier.Text;

        var accessors =
            node.AccessorList != null
                ? Concat(
                    "{ ",
                    Join(" ", node.AccessorList.Accessors.Select(a => a.Keyword.Text + ";").ToList()),
                    " }"
                )
                : "";

        var initializer =
            node.Initializer != null ? Concat(" = ", Visit(node.Initializer.Value), ";") : "";

        var propDoc = Concat(
            modifiers.Length > 0 ? modifiers + " " : "",
            type,
            " ",
            name,
            " ",
            accessors,
            initializer
        );

        if (attributes.Count > 0)
        {
            return Concat(Join(HardLine, attributes), HardLine, propDoc);
        }
        return propDoc;
    }

    public override Doc VisitAttributeList(AttributeListSyntax node)
    {
        return Concat("[", Join(", ", node.Attributes.Select(Visit)), "]");
    }

    public override Doc VisitAttribute(AttributeSyntax node)
    {
        var name = node.Name.ToString();
        var args = node.ArgumentList != null ? Visit(node.ArgumentList) : "";
        return Concat(name, args);
    }

    public override Doc VisitFieldDeclaration(FieldDeclarationSyntax node)
    {
        var modifiers = node.Modifiers.ToString();
        var type = node.Declaration.Type.ToString();
        var vars = Join(Concat(",", Line), node.Declaration.Variables.Select(Visit));

        return Concat(modifiers.Length > 0 ? modifiers + " " : "", type, " ", vars, ";");
    }

    public override Doc VisitConstructorDeclaration(ConstructorDeclarationSyntax node)
    {
        var modifiers = node.Modifiers.ToString();
        var name = node.Identifier.Text;
        var parameters = Visit(node.ParameterList);
        var body = Visit(node.Body);

        return Concat(
            modifiers.Length > 0 ? modifiers + " " : "",
            name,
            parameters,
            HardLine,
            body
        );
    }

    public override Doc VisitMethodDeclaration(MethodDeclarationSyntax node)
    {
        var modifiers = node.Modifiers.ToString();
        var returnType = node.ReturnType.ToString();
        var name = node.Identifier.Text;

        var parameters = Visit(node.ParameterList);
        var body = Visit(node.Body);

        return Concat(
            modifiers.Length > 0 ? modifiers + " " : "",
            returnType,
            " ",
            name,
            parameters,
            HardLine,
            body
        );
    }

    public override Doc VisitParameterList(ParameterListSyntax node)
    {
        var parameters = node.Parameters.Select(Visit).Where(d => d != null).ToList();
        if (parameters.Count == 0)
            return "()";

        return Group("(", Indent(SoftLine, Join(Concat(",", Line), parameters)), SoftLine, ")");
    }

    public override Doc VisitParameter(ParameterSyntax node)
    {
        var type = node.Type?.ToString() ?? "";
        var name = node.Identifier.Text;
        return Concat(type, " ", name);
    }

    public override Doc VisitBlock(BlockSyntax node)
    {
        var statements = node.Statements.Select(Visit).Where(d => d != null).ToList();

        if (statements.Count == 0)
            return Concat("{", HardLine, "}");

        return Concat("{", Indent(HardLine, Join(HardLine, statements)), HardLine, "}");
    }

    // HANDLES: if (condition) { ... }
    public override Doc VisitIfStatement(IfStatementSyntax node)
    {
        var condition = Visit(node.Condition);
        var statement = Visit(node.Statement);

        // Ensure single-line blocks are wrapped in braces for safety, or formatted correctly
        if (node.Statement is not BlockSyntax)
        {
            statement = Concat("{", Indent(HardLine, statement), HardLine, "}");
        }

        return Concat("if (", condition, ")", HardLine, statement);
    }

    // HANDLES: condition1 || condition2
    public override Doc VisitBinaryExpression(BinaryExpressionSyntax node)
    {
        var left = Visit(node.Left);
        var right = Visit(node.Right);
        var operatorToken = node.OperatorToken.Text;

        // Grouping allows breaking on long binary conditions
        return Group(left, Line, operatorToken, " ", right);
    }

    // HANDLES: throw new ArgumentNullException(...)
    public override Doc VisitThrowStatement(ThrowStatementSyntax node)
    {
        return Concat("throw ", Visit(node.Expression), ";");
    }

    // HANDLES: $"{description} must be a valid value"
    public override Doc VisitInterpolatedStringExpression(InterpolatedStringExpressionSyntax node)
    {
        var contents = node
            .Contents.Select(c =>
            {
                if (c is InterpolatedStringTextSyntax text)
                    return (Doc)text.TextToken.Text;
                if (c is InterpolationSyntax interpolation)
                    return Concat("{", Visit(interpolation.Expression), "}");
                return (Doc)"";
            })
            .ToList();

        return Concat("$\"", Join("", contents), "\"");
    }

    public override Doc VisitExpressionStatement(ExpressionStatementSyntax node)
    {
        return Concat(Visit(node.Expression), ";");
    }

    public override Doc VisitInvocationExpression(InvocationExpressionSyntax node)
    {
        var expression = Visit(node.Expression);
        var args = Visit(node.ArgumentList);
        return Concat(expression, args);
    }

    public override Doc VisitArgumentList(ArgumentListSyntax node)
    {
        var args = node.Arguments.Select(Visit).Where(d => d != null).ToList();
        if (args.Count == 0)
            return "()";

        return Group("(", Indent(SoftLine, Join(Concat(",", Line), args)), SoftLine, ")");
    }

    public override Doc VisitArgument(ArgumentSyntax node)
    {
        var nameColon = node.NameColon != null ? Concat(node.NameColon.Name.ToString(), ": ") : "";
        return Concat(nameColon, Visit(node.Expression));
    }

    public override Doc VisitLocalDeclarationStatement(LocalDeclarationStatementSyntax node)
    {
        var type = node.Declaration.Type.ToString();
        var vars = Join(Concat(",", Line), node.Declaration.Variables.Select(Visit));
        return Concat(type, " ", vars, ";");
    }

    public override Doc VisitVariableDeclarator(VariableDeclaratorSyntax node)
    {
        var name = node.Identifier.Text;
        if (node.Initializer != null)
        {
            return Concat(name, " = ", Visit(node.Initializer.Value));
        }
        return name;
    }

    public override Doc VisitObjectCreationExpression(ObjectCreationExpressionSyntax node)
    {
        var type = node.Type.ToString();
        var args = node.ArgumentList != null ? Visit(node.ArgumentList) : "";
        var init = node.Initializer != null ? Concat(" ", Visit(node.Initializer)) : "";
        return Concat("new ", type, args, init);
    }

    public override Doc VisitImplicitObjectCreationExpression(
        ImplicitObjectCreationExpressionSyntax node
    )
    {
        var args = node.ArgumentList != null ? Visit(node.ArgumentList) : "()";
        var init = node.Initializer != null ? Concat(" ", Visit(node.Initializer)) : "";
        return Concat("new", args, init);
    }

    public override Doc VisitInitializerExpression(InitializerExpressionSyntax node)
    {
        var expressions = node.Expressions.Select(Visit).Where(d => d != null).ToList();
        if (expressions.Count == 0)
            return "{}";

        return Group(
            "{",
            Indent(HardLine, Join(Concat(",", HardLine), expressions)),
            ",",
            HardLine,
            "}"
        );
    }

    public override Doc VisitAssignmentExpression(AssignmentExpressionSyntax node)
    {
        return Concat(Visit(node.Left), " = ", Visit(node.Right));
    }

    public override Doc VisitCollectionExpression(CollectionExpressionSyntax node)
    {
        var elements = node.Elements.Select(Visit).Where(d => d != null).ToList();
        if (elements.Count == 0)
            return "[]";

        return Group(
            "[",
            Indent(HardLine, Join(Concat(",", HardLine), elements)),
            ",",
            HardLine,
            "]"
        );
    }

    public override Doc VisitTryStatement(TryStatementSyntax node)
    {
        var tryBlock = Visit(node.Block);
        var catches = node.Catches.Select(Visit).Where(d => d != null).ToList();

        return Concat(
            "try",
            HardLine,
            tryBlock,
            catches.Count > 0 ? Concat(HardLine, Join(HardLine, catches)) : ""
        );
    }

    public override Doc VisitCatchClause(CatchClauseSyntax node)
    {
        var declaration =
            node.Declaration != null ? Concat(" (", node.Declaration.ToString(), ")") : "";
        var block = Visit(node.Block);

        return Concat("catch", declaration, HardLine, block);
    }

    public override Doc VisitReturnStatement(ReturnStatementSyntax node)
    {
        if (node.Expression != null)
        {
            return Concat("return ", Visit(node.Expression), ";");
        }
        return "return;";
    }

    public override Doc VisitAwaitExpression(AwaitExpressionSyntax node)
    {
        return Concat("await ", Visit(node.Expression));
    }

    public override Doc VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
    {
        return Concat(Visit(node.Expression), ".", node.Name.ToString());
    }

    public override Doc VisitIdentifierName(IdentifierNameSyntax node) => node.Identifier.Text;

    public override Doc VisitGenericName(GenericNameSyntax node) => node.ToString();

    public override Doc VisitPredefinedType(PredefinedTypeSyntax node) => node.Keyword.Text;

    public override Doc VisitLiteralExpression(LiteralExpressionSyntax node) => node.Token.Text;

    public override Doc DefaultVisit(SyntaxNode node)
    {
        return node.ToFullString().Trim();
    }
}

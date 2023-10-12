﻿using DeltaKustoLib.KustoModel;
using Kusto.Language.Syntax;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Tracing;
using System.Linq;
using System.Text;

namespace DeltaKustoLib.CommandModel
{
    /// <summary>
    /// Models <see cref="https://docs.microsoft.com/en-us/azure/data-explorer/kusto/management/create-ingestion-mapping-command"/>
    /// </summary>
    [Command(1100, "Create table ingestion mappings")]
    public class CreateMappingCommand : CommandBase
    {
        public EntityName TableName { get; }

        public string MappingKind { get; }

        public QuotedText MappingName { get; }

        public QuotedText MappingAsJson { get; }

        public bool RemoveOldestIfRequired { get; }

        public override string CommandFriendlyName => ".create ingestion mapping";

        public override string SortIndex => $"{TableName.Name}_{MappingName.Text}_{MappingKind}";

        public override string ScriptPath => $"tables/ingestion-mappings/create/{TableName}";

        #region Constructors
        public CreateMappingCommand(
            EntityName tableName,
            string mappingKind,
            QuotedText mappingName,
            QuotedText mappingAsJson,
            bool removeOldestIfRequired)
        {
            TableName = tableName;
            MappingKind = mappingKind.ToLower().Trim();
            MappingName = mappingName;
            MappingAsJson = QuotedText.FromText(mappingAsJson.Text.Trim());
            RemoveOldestIfRequired = removeOldestIfRequired;
        }

        internal static CommandBase FromCode(SyntaxElement rootElement)
        {
            var tableNameDeclaration = rootElement
                .GetAtLeastOneDescendant<NameDeclaration>("Table Name")
                .First();
            var mappingNameExpression = rootElement.GetUniqueDescendant<LiteralExpression>(
                "Mapping Name",
                n => n.Kind == SyntaxKind.StringLiteralExpression
                && n.NameInParent == "MappingName");
            var mappingKindElement = rootElement.GetUniqueDescendant<SyntaxElement>(
                "Mapping Kind",
                n => n.Kind == SyntaxKind.IdentifierToken && n.NameInParent == "MappingKind");
            var mappingFormatExpression = rootElement.GetUniqueDescendant<LiteralExpression>(
                "Mapping Format",
                n => n.Kind == SyntaxKind.StringLiteralExpression
                && n.NameInParent == "MappingFormat");
            var skippedTokens = rootElement.GetAtMostOneDescendant<SkippedTokens>(
                "Skipped tokens",
                n => n.Kind == SyntaxKind.SkippedTokens && n.NameInParent == "SkippedTokens")
                ?.Tokens
                ?.ToImmutableArray();
            var splitTokens = SplitPropertyTokens(skippedTokens);
            var mappingTokens = splitTokens.Item1
                ?.Prepend(QuotedText.FromLiteral(mappingFormatExpression).Text);
            var mappingAsJson = mappingTokens == null
                ? QuotedText.FromLiteral(mappingFormatExpression)
                : QuotedText.FromText(string.Concat(mappingTokens));
            var withToken = rootElement.GetAtMostOneDescendant<SyntaxToken>(
                "With token",
                n => n.Kind == SyntaxKind.WithKeyword);
            var propertyTokens = GetPropertyTokens(withToken);
            var removeOldestIfRequired = GetRemoveOldestIfRequired(propertyTokens);

            var command = new CreateMappingCommand(
                EntityName.FromCode(tableNameDeclaration),
                EntityName.FromCode(mappingKindElement).Name,
                QuotedText.FromLiteral(mappingNameExpression),
                mappingAsJson,
                removeOldestIfRequired);

            return command;
        }

        private static IEnumerable<SyntaxToken> GetPropertyTokens(SyntaxToken? withToken)
        {
            if (withToken != null)
            {
                var element = withToken.GetNextSibling();

                if (element.Kind != SyntaxKind.OpenParenToken)
                {
                    throw new DeltaException("Expected an open parenthesis after a with keyword");
                }
                element = element.GetNextSibling();
                //  Parsing totally depends on context (weird)
                if (element.Kind == SyntaxKind.List)
                {
                    var descendants = element.GetDescendants<SyntaxToken>();

                    foreach (var i in descendants)
                    {
                        yield return i;
                    }
                }
                else
                {
                    while (element.Kind != SyntaxKind.CloseParenToken)
                    {
                        var token = element as SyntaxToken;

                        if (token != null)
                        {
                            yield return token;
                        }
                        element = element.GetNextSibling();
                    }
                }
            }
        }

        private static bool GetRemoveOldestIfRequired(IEnumerable<SyntaxToken> propertyTokens)
        {
            var value = ParseProperties(propertyTokens)
                .Where(p => p.name.ToLower() == "removeoldestifrequired")
                .Select(p => p.value)
                .FirstOrDefault();

            if (value is bool boolValue)
            {
                return boolValue;
            }

            return false;
        }

        private static IEnumerable<(string name, object value)> ParseProperties(
            IEnumerable<SyntaxToken> propertyTokens)
        {
            var previous = (SyntaxToken?)null;
            var name = (string?)null;
            var isValue = false;

            foreach (var token in propertyTokens)
            {
                if (isValue)
                {
                    isValue = false;

                    yield return (name!, token.Value);
                }
                else if (token.Kind == SyntaxKind.EqualToken)
                {
                    if (previous != null)
                    {
                        name = previous.Text;
                        isValue = true;
                    }
                }
                previous = token;
            }
        }

        private static (IImmutableList<string>?, IImmutableList<SyntaxToken>?)
            SplitPropertyTokens(IImmutableList<SyntaxToken>? skippedTokens)
        {
            if (skippedTokens == null)
            {
                return (null, null);
            }
            else
            {
                var index = 0;

                while (index < skippedTokens.Count())
                {
                    if (skippedTokens[index].Kind == SyntaxKind.WithKeyword)
                    {
                        return (
                            skippedTokens
                            .Take(index)
                            .Select(t => t.Text)
                            .ToImmutableArray(),
                            skippedTokens
                            .Skip(index + 1)
                            .ToImmutableArray());
                    }
                    ++index;
                }

                return (
                    skippedTokens
                    .Select(t => t.Text)
                    .ToImmutableArray(),
                    null);
            }
        }
        #endregion

        public override bool Equals(CommandBase? other)
        {
            var otherCommand = other as CreateMappingCommand;
            var areEqualed = otherCommand != null
                && otherCommand.TableName.Equals(TableName)
                && otherCommand.MappingName.Equals(MappingName)
                && otherCommand.MappingKind.Equals(MappingKind)
                && otherCommand.MappingAsJson.Equals(MappingAsJson)
                && otherCommand.RemoveOldestIfRequired == RemoveOldestIfRequired;

            return areEqualed;
        }

        public override string ToScript(ScriptingContext? context)
        {
            var builder = new StringBuilder();

            builder.Append(".create-or-alter table ");
            builder.Append(TableName);
            builder.Append(" ingestion ");
            builder.Append(MappingKind);
            builder.Append(" mapping ");
            builder.Append(MappingName);
            builder.AppendLine();
            builder.AppendLine("```");
            builder.Append(MappingAsJson.Text.Trim());
            builder.AppendLine();
            builder.AppendLine("```");
            if (RemoveOldestIfRequired)
            {
                builder.AppendLine("with (removeOldestIfRequired=true)");
            }

            return builder.ToString();
        }

        internal MappingModel ToModel()
        {
            return new MappingModel(
                MappingName,
                MappingKind,
                MappingAsJson,
                RemoveOldestIfRequired);
        }
    }
}
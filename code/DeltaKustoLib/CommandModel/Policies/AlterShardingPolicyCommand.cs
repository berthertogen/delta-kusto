﻿using Kusto.Language.Syntax;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace DeltaKustoLib.CommandModel.Policies
{
    /// <summary>
    /// Models <see cref="https://docs.microsoft.com/en-us/azure/data-explorer/kusto/management/sharding-policy#alter-policy"/>
    /// </summary>
    [Command(15100, "Alter Sharding Policies")]
    public class AlterShardingPolicyCommand : EntityPolicyCommandBase
    {
        public override string CommandFriendlyName => ".alter <entity> policy sharding";

        public override string ScriptPath => EntityType == EntityType.Database
            ? $"tables/policies/sharding/create/{EntityName}"
            : $"databases/policies/sharding/create";

        public AlterShardingPolicyCommand(
            EntityType entityType,
            EntityName entityName,
            JsonDocument policy) : base(entityType, entityName, policy)
        {
        }

        public AlterShardingPolicyCommand(
            EntityType entityType,
            EntityName entityName,
            int maxRowCount,
            int maxExtentSizeInMb,
            int maxOriginalSizeInMb)
            : this(
                  entityType,
                  entityName,
                  ToJsonDocument(new
                  {
                      MaxRowCount = maxRowCount,
                      MaxExtentSizeInMb = maxExtentSizeInMb,
                      MaxOriginalSizeInMb = maxOriginalSizeInMb
                  }))
        {
        }

        internal static CommandBase FromCode(SyntaxElement rootElement)
        {
            var entityType = ExtractEntityType(rootElement);
            var entityName = rootElement.GetDescendants<NameReference>().Last();
            var policyText = QuotedText.FromLiteral(
                rootElement.GetUniqueDescendant<LiteralExpression>(
                    "Sharding",
                    e => e.NameInParent == "ShardingPolicy"));
            var policy = Deserialize<JsonDocument>(policyText.Text);

            if (policy == null)
            {
                throw new DeltaException(
                    $"Can't extract policy objects from {policyText.ToScript()}");
            }

            return new AlterShardingPolicyCommand(
                entityType,
                EntityName.FromCode(entityName.Name),
                policy);
        }

        public override string ToScript(ScriptingContext? context)
        {
            var builder = new StringBuilder();

            builder.Append(".alter ");
            builder.Append(EntityType == EntityType.Table ? "table" : "database");
            builder.Append(" ");
            if (EntityType == EntityType.Database && context?.CurrentDatabaseName != null)
            {
                builder.Append(context.CurrentDatabaseName.ToScript());
            }
            else
            {
                builder.Append(EntityName.ToScript());
            }
            builder.Append(" policy sharding");
            builder.AppendLine();
            builder.Append("```");
            builder.Append(SerializePolicy());
            builder.AppendLine();
            builder.Append("```");

            return builder.ToString();
        }

        internal static IEnumerable<CommandBase> ComputeDelta(
            AlterShardingPolicyCommand? currentCommand,
            AlterShardingPolicyCommand? targetCommand)
        {
            var hasCurrent = currentCommand != null;
            var hasTarget = targetCommand != null;

            if (hasCurrent && !hasTarget)
            {   //  No target, we remove the current policy
                yield return new DeleteShardingPolicyCommand(
                    currentCommand!.EntityType,
                    currentCommand!.EntityName);
            }
            else if (hasTarget)
            {
                if (!hasCurrent || !currentCommand!.Equals(targetCommand!))
                {   //  There is a target and either no current or the current is different
                    yield return targetCommand!;
                }
            }
            else
            {   //  Both target and current are null:  no delta
            }
        }
    }
}
﻿using DeltaKustoIntegration.Parameterization;
using DeltaKustoLib;
using DeltaKustoLib.CommandModel;
using Kusto.Data;
using Kusto.Data.Common;
using Kusto.Data.Net.Client;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Data;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace DeltaKustoIntegration.Kusto
{
    /// <summary>
    /// Basically wraps REST APIs <see cref="https://docs.microsoft.com/en-us/azure/data-explorer/kusto/api/rest/"/>.
    /// </summary>
    internal class KustoManagementGateway : IKustoManagementGateway
    {
        private readonly Uri _clusterUri;
        private readonly string _database;
        private readonly ICslAdminProvider _commandProvider;
        private readonly ITracer _tracer;

        public KustoManagementGateway(
            Uri clusterUri,
            string database,
            TokenProviderParameterization tokenProvider,
            ITracer tracer)
        {
            _clusterUri = clusterUri;
            _database = database;

            var kustoConnectionStringBuilder = CreateKustoConnectionStringBuilder(
                clusterUri,
                tokenProvider);

            _commandProvider =
                KustoClientFactory.CreateCslCmAdminProvider(kustoConnectionStringBuilder);
            _tracer = tracer;
        }

        async Task<IImmutableList<CommandBase>> IKustoManagementGateway.ReverseEngineerDatabaseAsync(
            CancellationToken ct)
        {
            var tracerTimer = new TracerTimer(_tracer);

            _tracer.WriteLine(true, "Fetch schema commands start");

            var schemaOutputTask = ExecuteCommandAsync(".show database schema as csl script", ct);
            var mappingsOutputTask = ExecuteCommandAsync(
                ".show ingestion mappings | where Database==current_database()",
                ct);
            var schemaOutput = await schemaOutputTask;
            var mappingsOutput = await mappingsOutputTask;

            _tracer.WriteLine(true, "Fetch schema commands end");
            tracerTimer.WriteTime(true, "Fetch schema commands time");

            var schemaCommandText = Select(
                schemaOutput,
                r => (string)r["DatabaseSchemaScript"]);
            var schemaCommands = CommandBase.FromScript(string.Join("\n\n", schemaCommandText), true);
            var mappingCommands = Select(
                mappingsOutput,
                r => new CreateMappingCommand(
                    new EntityName((string)r["Name"]),
                    (string)r["Kind"],
                    new QuotedText((string)r["Mapping"]),
                    new QuotedText((string)r["Table"])))
                .Cast<CommandBase>()
                .ToImmutableArray();
            var allCommands = schemaCommands
                .Concat(mappingCommands)
                .ToImmutableArray();

            return allCommands;
        }

        async Task IKustoManagementGateway.ExecuteCommandsAsync(
            IEnumerable<CommandBase> commands,
            CancellationToken ct)
        {
            if (commands.Any())
            {
                _tracer.WriteLine(true, ".execute database script commands start");

                var tracerTimer = new TracerTimer(_tracer);
                var scriptingContext = new ScriptingContext
                {
                    CurrentDatabaseName = new EntityName(_database)
                };
                var commandScripts = commands.Select(c => c.ToScript(scriptingContext));
                var fullScript = ".execute database script with (ThrowOnErrors=true) <|"
                    + Environment.NewLine
                    + string.Join(Environment.NewLine + Environment.NewLine, commandScripts);
                var output = await ExecuteCommandAsync(fullScript, ct);

                _tracer.WriteLine(true, ".execute database script commands end");
                tracerTimer.WriteTime(true, ".execute database script commands time");

                var content = Select(
                    output,
                    r => new
                    {
                        Result = (string)r["Result"],
                        Reason = (string)r["Reason"],
                        CommandText = (string)r["CommandText"],
                        OperationId = (Guid)r["OperationId"]
                    });
                var failedItems = content.Where(t => t.Result != "Completed");

                if (failedItems.Any())
                {
                    var failedItem = failedItems.First();
                    var failedItemCommand = PackageString(failedItem.CommandText);
                    var allCommands = string.Join(
                        ", ",
                        content.Select(i => $"({i.Result}:  '{PackageString(i.CommandText)}')"));

                    throw new InvalidOperationException(
                        $"Command failed to execute with reason '{failedItem.Reason}'.  "
                        + $"Operation ID:  {failedItem.OperationId}.  "
                        + $"Cluster URI:  {_clusterUri}.  "
                        + $"Database:  {_database}.  "
                        + $"Command:  '{failedItemCommand}'.  "
                        + $"All commands:  {allCommands}");
                }
            }
        }

        private static KustoConnectionStringBuilder CreateKustoConnectionStringBuilder(
            Uri clusterUri,
            TokenProviderParameterization tokenProvider)
        {
            var builder = new KustoConnectionStringBuilder(clusterUri.ToString());

            if (tokenProvider.Login != null)
            {
                return builder.WithAadApplicationKeyAuthentication(
                    tokenProvider.Login.ClientId,
                    tokenProvider.Login.Secret,
                    tokenProvider.Login.TenantId);
            }
            else if (tokenProvider.Tokens != null)
            {
                var token = tokenProvider.Tokens.Values
                    .Where(t => t.ClusterUri != null)
                    .Where(t => t.ClusterUri!.Trim().ToLower() == clusterUri.ToString().Trim().ToLower())
                    .Select(t => t.Token)
                    .FirstOrDefault();

                if (token != null)
                {
                    return builder.WithAadUserTokenAuthentication(token);
                }
                else
                {
                    throw new DeltaException($"No token was provided for {clusterUri}");
                }
            }
            else
            {
                throw new NotSupportedException("Token provider isn't supported");
            }
        }
        private static string PackageString(string text)
        {
            return text.Replace("\n", "\\n").Replace("\r", "\\r");
        }

        private static IEnumerable<T> Select<T>(IDataReader reader, Func<IDataReader, T> projection)
        {
            while (reader.Read())
            {
                yield return projection(reader);
            }
        }

        private async Task<IDataReader> ExecuteCommandAsync(
            string commandScript,
            CancellationToken ct)
        {
            try
            {
                var reader = await _commandProvider.ExecuteControlCommandAsync(_database, commandScript);

                return reader;
            }
            catch (Exception ex)
            {
                throw new DeltaException(
                    $"Issue running Kusto script '{commandScript}' on cluster '{_clusterUri}' / "
                    + $"database '{_database}'",
                    ex);
            }
        }
    }
}
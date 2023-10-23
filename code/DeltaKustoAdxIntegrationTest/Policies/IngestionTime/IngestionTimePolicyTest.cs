using DeltaKustoIntegration.Database;
using DeltaKustoLib.CommandModel;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace DeltaKustoAdxIntegrationTest.Policies.IngestionTime
{
    public class IngestionTimePolicyTest : AdxAutoIntegrationTestBase
    {
        protected override string StatesFolderPath => "Policies/IngestionTime";
    }
}
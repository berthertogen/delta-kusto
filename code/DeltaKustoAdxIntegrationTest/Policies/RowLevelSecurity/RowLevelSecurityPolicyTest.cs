using DeltaKustoIntegration.Database;
using DeltaKustoLib.CommandModel;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace DeltaKustoAdxIntegrationTest.Policies.RowLevelSecurity
{
    public class RowLevelSecurityPolicyTest : AdxAutoIntegrationTestBase
    {
        protected override string StatesFolderPath => "Policies/RowLevelSecurity";
    }
}